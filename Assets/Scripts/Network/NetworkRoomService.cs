// ============================================================
// 程序 1：房间模块的 Network 权威实现
// 同时实现 IRoomStateReadonly / IRoomCommands / IRoomEvents，
// 启动时通过 GameContract.BindRoom(this, this, this) 注入，
// 是程序 2（UI）访问房间功能的唯一权威路径。
//
// 内部依赖：
//   - CustomNetworkManager：Mirror NetworkManager 子类，负责 StartHost/StartClient/StopHost/StopClient
//   - ManualDiscovery：UDP 局域网发现，负责广播/监听，发现结果通过 ReportDiscoveredRoom 上报到这里
//
// 对外只暴露 RoomInfo（值类型快照），内部发现数据用 RoomItemData（Data/RoomItemData.cs）。
// 约定：先更新 State，再触发对应 Event；事件均在主线程触发。
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkRoomService : MonoBehaviour, IRoomStateReadonly, IRoomCommands, IRoomEvents
{
    public static NetworkRoomService Instance { get; private set; }

    [Header("依赖（留空则自动 FindObjectOfType）")]
    [SerializeField] private CustomNetworkManager networkManager;
    [SerializeField] private ManualDiscovery discovery;

    [Header("发现设置")]
    [Tooltip("超过该秒数未再次收到某房间的广播，则视为该房间已失效，从列表中移除")]
    [SerializeField] private float roomStaleTimeout = 6f;
    [Tooltip("JoinRoom 连接超时秒数，超时未连上则回 OnRoomError(Timeout)")]
    [SerializeField] private float joinTimeoutSeconds = 8f;
    [Tooltip("JoinRoomByCode 在局域网寻找广播的超时秒数")]
    [SerializeField] private float findByCodeTimeoutSeconds = 8f;
    [Tooltip("ManualDiscovery 当前固定使用的游戏端口，仅在 serverId 缺省端口时兜底")]
    [SerializeField] private int defaultPort = 7777;

    // ---- IRoomStateReadonly ----
    public RoomConnectionState ConnectionState { get; private set; } = RoomConnectionState.Disconnected;
    public IReadOnlyList<RoomInfo> RoomList => _roomListSnapshot;
    public string CurrentRoomCode { get; private set; } = string.Empty;
    public RoomInfo? FoundRoom { get; private set; }
    public PlayerRole PreferredRole { get; private set; } = PlayerRole.None;

    // ---- IRoomEvents ----
    public event Action<RoomConnectionState> OnConnectionStateChanged;
    public event Action<IReadOnlyList<RoomInfo>> OnRoomListUpdated;
    public event Action<RoomInfo?> OnFoundRoomChanged;
    public event Action<RoomError> OnRoomError;

    private readonly Dictionary<string, RoomItemData> _discoveredRooms = new Dictionary<string, RoomItemData>();
    private readonly Dictionary<string, float> _lastSeenAt = new Dictionary<string, float>();
    private List<RoomInfo> _roomListSnapshot = new List<RoomInfo>();

    private RoomOp _pendingOp = RoomOp.Unknown;
    private Coroutine _joinTimeoutCoroutine;
    private Coroutine _findByCodeTimeoutCoroutine;
    private Coroutine _applyPreferredRoleCoroutine;
    /// <summary>非空表示正在按短码寻找房间（已规范化大写）。</summary>
    private string _searchingRoomCode;
    /// <summary>true = FindRoomByCode（不自动连）；false = JoinRoomByCode（命中后自动连）。</summary>
    private bool _findOnlyMode;
    /// <summary>按码加入时暂存，连接成功后再写入 CurrentRoomCode。</summary>
    private string _pendingJoinRoomCode;
    private bool _leaveRequested;

    // ------------------------------------------------------------------
    // 自动引导：无需手动在场景里挂节点，首次场景加载后自动创建并常驻。
    // 若场景里已经手动放置了 NetworkRoomService（并在 Inspector 里配好引用），
    // 会走 Awake 里的单例检查，跳过自动创建的那份。
    // ------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject(nameof(NetworkRoomService));
        go.AddComponent<NetworkRoomService>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[NetworkRoomService] 已存在实例，销毁重复对象");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureRefs();
        GameContract.BindRoom(this, this, this);
    }

    void Update()
    {
        PruneStaleRooms();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            GameContract.UnbindRoom();
        }
    }

    private void EnsureRefs()
    {
        if (networkManager == null) networkManager = FindObjectOfType<CustomNetworkManager>();
        if (discovery == null) discovery = FindObjectOfType<ManualDiscovery>();
    }

    // ================================================================
    // IRoomCommands
    // ================================================================

    public void RefreshRoomList()
    {
        EnsureRefs();

        _discoveredRooms.Clear();
        _lastSeenAt.Clear();
        PublishRoomList();

        if (discovery == null)
        {
            RaiseError(RoomOp.Refresh, RoomErrorReason.ConnectionFailed, "ManualDiscovery not found");
            return;
        }

        discovery.StopListening();
        if (!discovery.StartListening())
        {
            RaiseError(RoomOp.Refresh, RoomErrorReason.ConnectionFailed, "Failed to start LAN listen port");
        }
    }

    public void CreateRoom(string roomName, int maxPlayers)
    {
        EnsureRefs();

        if (ConnectionState != RoomConnectionState.Disconnected &&
            ConnectionState != RoomConnectionState.Failed)
        {
            RaiseError(RoomOp.Create, RoomErrorReason.AlreadyInRoom, $"Cannot create room while {ConnectionState}");
            return;
        }

        if (networkManager == null)
        {
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Create, RoomErrorReason.ConnectionFailed, "CustomNetworkManager not found");
            return;
        }

        _leaveRequested = false;
        _pendingOp = RoomOp.Create;
        CancelFindByCodeTimeout();
        ClearRoomCodeSearch();
        _pendingJoinRoomCode = null;
        ClearFoundRoom();
        // 保留 PreferredRole（创建前已选身份）
        CurrentRoomCode = string.Empty;
        SetConnectionState(RoomConnectionState.Connecting);

        try
        {
            string finalName = string.IsNullOrWhiteSpace(roomName) ? "Peekaboo Room" : roomName;
            // 与 ManualDiscovery.BroadcastData 共用同一份 PlayerPrefs 房间名，
            // 兼容旧 CreateRoomController 的直接调用路径。
            PlayerPrefs.SetString("RoomName", finalName);
            networkManager.maxConnections = Mathf.Max(1, maxPlayers);

            networkManager.StartHost();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkRoomService] 创建房间失败：{e.Message}");
            _pendingOp = RoomOp.Unknown;
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Create, RoomErrorReason.ConnectionFailed, e.Message);
            return;
        }

        // Mirror Host 本地连接通常同步建立；再校验一次服务端是否真正起来。
        if (!NetworkServer.active)
        {
            _pendingOp = RoomOp.Unknown;
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Create, RoomErrorReason.ConnectionFailed, "NetworkServer not active after StartHost");
            return;
        }

        _pendingOp = RoomOp.Unknown;
        CurrentRoomCode = RoomCodeUtil.Generate();
        SetConnectionState(RoomConnectionState.InRoom);
        discovery?.StartBroadcasting();
        Debug.Log($"[NetworkRoomService] 房间已创建，短码：{CurrentRoomCode}");
        StartApplyPreferredRole();
    }

    public void JoinRoom(string serverId)
    {
        EnsureRefs();

        if (ConnectionState != RoomConnectionState.Disconnected &&
            ConnectionState != RoomConnectionState.Failed)
        {
            RaiseError(RoomOp.Join, RoomErrorReason.AlreadyInRoom, $"Cannot join room while {ConnectionState}");
            return;
        }

        if (networkManager == null)
        {
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Join, RoomErrorReason.ConnectionFailed, "CustomNetworkManager not found");
            return;
        }

        if (!TryParseServerId(serverId, out string ip, out int port))
        {
            RaiseError(RoomOp.Join, RoomErrorReason.RoomNotFound, $"Cannot parse serverId: {serverId}");
            return;
        }

        if (_discoveredRooms.TryGetValue(serverId, out RoomItemData room) &&
            room.currentPlayers >= room.maxPlayers &&
            room.maxPlayers > 0)
        {
            RaiseError(RoomOp.Join, RoomErrorReason.RoomFull, $"Room is full: {serverId}");
            return;
        }

        BeginClientJoin(ip, port);
    }

    public void FindRoomByCode(string roomCode)
    {
        BeginSearchByCode(roomCode, findOnly: true);
    }

    public bool TrySelectRoleBeforeEnter(PlayerRole role)
    {
        if (role != PlayerRole.Hider && role != PlayerRole.Seeker)
        {
            RaiseError(RoomOp.Find, RoomErrorReason.Unknown, $"Invalid role: {role}");
            return false;
        }

        // 加入路径：已找到房间则按投影名额校验
        if (FoundRoom.HasValue)
        {
            RoomInfo found = FoundRoom.Value;
            RoleSlots projected = RoleSlots.ProjectForJoiner(
                found.currentPlayers, found.seekerCount, found.hiderCount);

            if (role == PlayerRole.Hider && projected.HiderFull)
            {
                RaiseError(RoomOp.Find, RoomErrorReason.SlotFull, "Hider");
                return false;
            }

            if (role == PlayerRole.Seeker && projected.SeekerFull)
            {
                RaiseError(RoomOp.Find, RoomErrorReason.SlotFull, "Seeker");
                return false;
            }
        }

        PreferredRole = role;
        Debug.Log($"[NetworkRoomService] 进房前身份：{role}");
        return true;
    }

    public void JoinFoundRoom()
    {
        EnsureRefs();

        if (!FoundRoom.HasValue || string.IsNullOrEmpty(FoundRoom.Value.serverId))
        {
            RaiseError(RoomOp.Join, RoomErrorReason.RoomNotFound, "No room found yet; call FindRoomByCode first");
            return;
        }

        if (PreferredRole == PlayerRole.None)
        {
            RaiseError(RoomOp.Join, RoomErrorReason.RoleNotSelected, "Please select a role first");
            return;
        }

        // 再次校验投影名额（广播可能已更新）
        RoomInfo found = FoundRoom.Value;
        RoleSlots projected = RoleSlots.ProjectForJoiner(
            found.currentPlayers, found.seekerCount, found.hiderCount);
        if ((PreferredRole == PlayerRole.Hider && projected.HiderFull) ||
            (PreferredRole == PlayerRole.Seeker && projected.SeekerFull))
        {
            RaiseError(RoomOp.Join, RoomErrorReason.SlotFull,
                PreferredRole == PlayerRole.Hider ? "Hider" : "Seeker");
            return;
        }

        if (found.currentPlayers >= found.maxPlayers && found.maxPlayers > 0)
        {
            RaiseError(RoomOp.Join, RoomErrorReason.RoomFull, $"Room is full: {found.serverId}");
            return;
        }

        if (ConnectionState != RoomConnectionState.Disconnected &&
            ConnectionState != RoomConnectionState.Failed)
        {
            RaiseError(RoomOp.Join, RoomErrorReason.AlreadyInRoom, $"Cannot join room while {ConnectionState}");
            return;
        }

        if (networkManager == null)
        {
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Join, RoomErrorReason.ConnectionFailed, "CustomNetworkManager not found");
            return;
        }

        if (!TryParseServerId(found.serverId, out string ip, out int port))
        {
            RaiseError(RoomOp.Join, RoomErrorReason.RoomNotFound, $"Cannot parse serverId: {found.serverId}");
            return;
        }

        CancelFindByCodeTimeout();
        ClearRoomCodeSearch();
        _pendingJoinRoomCode = string.IsNullOrEmpty(found.roomCode) ? _pendingJoinRoomCode : found.roomCode;
        if (string.IsNullOrEmpty(_pendingJoinRoomCode) &&
            RoomCodeUtil.TryNormalize(found.roomCode, out string norm))
            _pendingJoinRoomCode = norm;

        discovery?.StopListening();
        BeginClientJoin(ip, port);
        Debug.Log($"[NetworkRoomService] JoinFoundRoom → {ip}:{port} role={PreferredRole} code={_pendingJoinRoomCode}");
    }

    public void JoinRoomByCode(string roomCode)
    {
        // 兼容：寻找命中后自动连接（若已选身份则等同 JoinFoundRoom 路径）
        BeginSearchByCode(roomCode, findOnly: false);
    }

    private void BeginSearchByCode(string roomCode, bool findOnly)
    {
        EnsureRefs();

        if (ConnectionState != RoomConnectionState.Disconnected &&
            ConnectionState != RoomConnectionState.Failed)
        {
            RaiseError(findOnly ? RoomOp.Find : RoomOp.Join, RoomErrorReason.AlreadyInRoom,
                $"Cannot {(findOnly ? "find" : "join")} room while {ConnectionState}");
            return;
        }

        if (!RoomCodeUtil.TryNormalize(roomCode, out string normalized))
        {
            RaiseError(findOnly ? RoomOp.Find : RoomOp.Join, RoomErrorReason.RoomNotFound,
                $"Invalid room code: {roomCode}");
            return;
        }

        if (discovery == null)
        {
            RaiseError(findOnly ? RoomOp.Find : RoomOp.Join, RoomErrorReason.ConnectionFailed,
                "ManualDiscovery not found");
            return;
        }

        if (!findOnly && networkManager == null)
        {
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Join, RoomErrorReason.ConnectionFailed, "CustomNetworkManager not found");
            return;
        }

        _leaveRequested = false;
        _pendingOp = findOnly ? RoomOp.Find : RoomOp.Join;
        _searchingRoomCode = normalized;
        _findOnlyMode = findOnly;
        _pendingJoinRoomCode = normalized;
        ClearFoundRoom();

        // 已缓存可立刻命中
        if (TryFindDiscoveredServerIdByCode(normalized, out string cachedServerId) &&
            _discoveredRooms.TryGetValue(cachedServerId, out RoomItemData cachedRoom))
        {
            TryHandleCodeMatch(cachedRoom);
            return;
        }

        // Find 不改变 Connecting；Join 兼容路径进入 Connecting
        if (!findOnly)
            SetConnectionState(RoomConnectionState.Connecting);

        discovery.StopListening();
        if (!discovery.StartListening())
        {
            ClearRoomCodeSearch();
            _pendingJoinRoomCode = null;
            _pendingOp = RoomOp.Unknown;
            if (!findOnly)
                SetConnectionState(RoomConnectionState.Failed);
            RaiseError(findOnly ? RoomOp.Find : RoomOp.Join, RoomErrorReason.ConnectionFailed,
                "Failed to start LAN listen port");
            return;
        }

        if (_findByCodeTimeoutCoroutine != null) StopCoroutine(_findByCodeTimeoutCoroutine);
        _findByCodeTimeoutCoroutine = StartCoroutine(FindByCodeTimeoutRoutine());
        Debug.Log($"[NetworkRoomService] 正在按短码{(findOnly ? "寻找" : "加入")}：{normalized}");
    }

    public void LeaveRoom()
    {
        EnsureRefs();

        _leaveRequested = true;
        CancelJoinTimeout();
        CancelFindByCodeTimeout();
        CancelApplyPreferredRole();
        ClearRoomCodeSearch();
        _pendingJoinRoomCode = null;
        PreferredRole = PlayerRole.None;
        ClearFoundRoom();
        CurrentRoomCode = string.Empty;
        discovery?.StopBroadcasting();
        discovery?.StopListening();

        if (networkManager != null)
        {
            if (NetworkServer.active) networkManager.StopHost();
            else if (NetworkClient.active) networkManager.StopClient();
        }

        _pendingOp = RoomOp.Unknown;
        SetConnectionState(RoomConnectionState.Disconnected);
    }

    // ================================================================
    // 供 CustomNetworkManager 回调（客户端连接结果），仅内部 Network 代码使用
    // ================================================================

    public void NotifyClientConnected()
    {
        // Host 的 CreateRoom 已在 StartHost 返回后设为 InRoom；此处覆盖 Join 路径。
        if (_pendingOp == RoomOp.Create && ConnectionState == RoomConnectionState.InRoom)
            return;

        CancelJoinTimeout();
        CancelFindByCodeTimeout();
        ClearRoomCodeSearch();
        if (!string.IsNullOrEmpty(_pendingJoinRoomCode))
        {
            CurrentRoomCode = _pendingJoinRoomCode;
            _pendingJoinRoomCode = null;
        }
        _pendingOp = RoomOp.Unknown;
        SetConnectionState(RoomConnectionState.InRoom);
        StartApplyPreferredRole();
    }

    public void NotifyClientDisconnected()
    {
        CancelJoinTimeout();
        CancelFindByCodeTimeout();
        CancelApplyPreferredRole();

        // LeaveRoom 已主动设为 Disconnected，忽略后续 Mirror 回调，避免覆盖。
        if (_leaveRequested)
        {
            _leaveRequested = false;
            _pendingOp = RoomOp.Unknown;
            ClearRoomCodeSearch();
            _pendingJoinRoomCode = null;
            PreferredRole = PlayerRole.None;
            ClearFoundRoom();
            CurrentRoomCode = string.Empty;
            if (ConnectionState != RoomConnectionState.Disconnected)
                SetConnectionState(RoomConnectionState.Disconnected);
            return;
        }

        if (ConnectionState == RoomConnectionState.Connecting)
        {
            RoomOp op = _pendingOp == RoomOp.Unknown ? RoomOp.Join : _pendingOp;
            _pendingOp = RoomOp.Unknown;
            ClearRoomCodeSearch();
            _pendingJoinRoomCode = null;
            CurrentRoomCode = string.Empty;
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(op, RoomErrorReason.ConnectionFailed, "Disconnected while connecting");
            return;
        }

        if (ConnectionState == RoomConnectionState.InRoom)
        {
            _pendingOp = RoomOp.Unknown;
            _pendingJoinRoomCode = null;
            PreferredRole = PlayerRole.None;
            ClearFoundRoom();
            CurrentRoomCode = string.Empty;
            SetConnectionState(RoomConnectionState.Disconnected);
            return;
        }

        // Failed / Disconnected：保持现状（超时路径可能已设 Failed）
        _pendingOp = RoomOp.Unknown;
    }

    public void NotifyClientError(string reason)
    {
        CancelJoinTimeout();
        CancelFindByCodeTimeout();
        CancelApplyPreferredRole();

        if (_leaveRequested)
        {
            _pendingOp = RoomOp.Unknown;
            return;
        }

        RoomOp op = _pendingOp == RoomOp.Unknown ? RoomOp.Join : _pendingOp;
        _pendingOp = RoomOp.Unknown;
        ClearRoomCodeSearch();
        _pendingJoinRoomCode = null;
        if (ConnectionState != RoomConnectionState.InRoom)
            CurrentRoomCode = string.Empty;
        SetConnectionState(RoomConnectionState.Failed);
        RaiseError(op, RoomErrorReason.ConnectionFailed, reason);
    }

    private void CancelJoinTimeout()
    {
        if (_joinTimeoutCoroutine != null)
        {
            StopCoroutine(_joinTimeoutCoroutine);
            _joinTimeoutCoroutine = null;
        }
    }

    private void CancelFindByCodeTimeout()
    {
        if (_findByCodeTimeoutCoroutine != null)
        {
            StopCoroutine(_findByCodeTimeoutCoroutine);
            _findByCodeTimeoutCoroutine = null;
        }
    }

    private void CancelApplyPreferredRole()
    {
        if (_applyPreferredRoleCoroutine != null)
        {
            StopCoroutine(_applyPreferredRoleCoroutine);
            _applyPreferredRoleCoroutine = null;
        }
    }

    private void ClearRoomCodeSearch()
    {
        _searchingRoomCode = null;
        _findOnlyMode = false;
    }

    private void ClearFoundRoom()
    {
        if (!FoundRoom.HasValue) return;
        FoundRoom = null;
        OnFoundRoomChanged?.Invoke(null);
    }

    private void SetFoundRoom(RoomInfo info)
    {
        FoundRoom = info;
        OnFoundRoomChanged?.Invoke(info);
    }

    private void StartApplyPreferredRole()
    {
        CancelApplyPreferredRole();
        if (PreferredRole == PlayerRole.None) return;
        _applyPreferredRoleCoroutine = StartCoroutine(ApplyPreferredRoleRoutine());
    }

    private IEnumerator ApplyPreferredRoleRoutine()
    {
        const float timeout = 8f;
        float elapsed = 0f;
        PlayerRole role = PreferredRole;

        while (elapsed < timeout)
        {
            if (role == PlayerRole.None) yield break;

            if (GameContract.IsBound &&
                GameContract.State != null &&
                GameContract.State.Phase == GamePhase.Waiting &&
                GameContract.Commands != null)
            {
                Debug.Log($"[NetworkRoomService] 进房后自动 SelectRole：{role}");
                GameContract.Commands.SelectRole(role);
                _applyPreferredRoleCoroutine = null;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogWarning("[NetworkRoomService] 自动 SelectRole 超时：对局契约未就绪");
        _applyPreferredRoleCoroutine = null;
    }

    private IEnumerator JoinTimeoutRoutine()
    {
        yield return new WaitForSecondsRealtime(joinTimeoutSeconds);
        _joinTimeoutCoroutine = null;

        if (ConnectionState != RoomConnectionState.Connecting) yield break;

        Debug.LogWarning("[NetworkRoomService] 加入房间超时");
        _pendingOp = RoomOp.Unknown;
        _pendingJoinRoomCode = null;
        CurrentRoomCode = string.Empty;
        SetConnectionState(RoomConnectionState.Failed);
        RaiseError(RoomOp.Join, RoomErrorReason.Timeout, "Connection timed out");

        if (networkManager != null && NetworkClient.active)
            networkManager.StopClient();
    }

    private IEnumerator FindByCodeTimeoutRoutine()
    {
        yield return new WaitForSecondsRealtime(findByCodeTimeoutSeconds);
        _findByCodeTimeoutCoroutine = null;

        if (string.IsNullOrEmpty(_searchingRoomCode)) yield break;
        // Find-only 时 ConnectionState 仍为 Disconnected；Join 兼容路径为 Connecting
        if (!_findOnlyMode && ConnectionState != RoomConnectionState.Connecting) yield break;
        if (_findOnlyMode && FoundRoom.HasValue) yield break;

        string code = _searchingRoomCode;
        bool findOnly = _findOnlyMode;
        ClearRoomCodeSearch();
        _pendingJoinRoomCode = null;
        _pendingOp = RoomOp.Unknown;
        if (!findOnly)
        {
            CurrentRoomCode = string.Empty;
            SetConnectionState(RoomConnectionState.Failed);
        }

        RaiseError(findOnly ? RoomOp.Find : RoomOp.Join, RoomErrorReason.Timeout,
            $"No room found for code: {code}");
        Debug.LogWarning($"[NetworkRoomService] 按短码寻找超时：{code}");
    }

    // ================================================================
    // 供 ManualDiscovery 上报发现的房间（UDP 广播解析结果）
    // ================================================================

    public void ReportDiscoveredRoom(RoomItemData data)
    {
        if (data == null || string.IsNullOrEmpty(data.serverId)) return;

        if (_discoveredRooms.TryGetValue(data.serverId, out RoomItemData existing) &&
            data.ping < 0f &&
            existing.ping >= 0f)
        {
            data.ping = existing.ping;
        }

        if (_discoveredRooms.TryGetValue(data.serverId, out RoomItemData prev))
        {
            if (string.IsNullOrEmpty(data.roomCode) && !string.IsNullOrEmpty(prev.roomCode))
                data.roomCode = prev.roomCode;
            // ping 更新包可能未带名额计数
            if (data.seekerCount == 0 && data.hiderCount == 0 &&
                (prev.seekerCount > 0 || prev.hiderCount > 0) &&
                string.IsNullOrEmpty(data.roomCode) == string.IsNullOrEmpty(prev.roomCode))
            {
                // 仅当新包看起来像 ping 刷新（无码变化）时保留计数；若广播明确带了 0 也保留旧值更稳妥
            }
        }

        _discoveredRooms[data.serverId] = data;
        _lastSeenAt[data.serverId] = Time.unscaledTime;
        PublishRoomList();

        // 已找到同一房间则刷新 FoundRoom 名额
        if (FoundRoom.HasValue &&
            FoundRoom.Value.serverId == data.serverId)
        {
            SetFoundRoom(ToRoomInfo(data));
        }

        TryHandleCodeMatch(data);
    }

    /// <summary>短码命中：Find-only 写入 FoundRoom；Join 兼容路径直接连。</summary>
    private void TryHandleCodeMatch(RoomItemData data)
    {
        if (string.IsNullOrEmpty(_searchingRoomCode) || data == null) return;
        if (!RoomCodeUtil.TryNormalize(data.roomCode, out string discoveredCode)) return;
        if (!string.Equals(discoveredCode, _searchingRoomCode, StringComparison.Ordinal)) return;

        RoomInfo info = ToRoomInfo(data);

        if (_findOnlyMode)
        {
            CancelFindByCodeTimeout();
            // 继续监听以便名额刷新；仅结束「等待首次命中」超时
            _searchingRoomCode = discoveredCode; // 保持匹配码，后续广播可更新 FoundRoom
            _findOnlyMode = true;
            _pendingOp = RoomOp.Unknown;
            SetFoundRoom(info);
            Debug.Log($"[NetworkRoomService] 短码找到房间 {discoveredCode} → {data.serverId}");
            return;
        }

        // JoinRoomByCode 兼容：若已选身份则走 JoinFoundRoom；否则直接连
        CancelFindByCodeTimeout();
        SetFoundRoom(info);
        ClearRoomCodeSearch();
        _pendingJoinRoomCode = discoveredCode;

        if (PreferredRole != PlayerRole.None)
        {
            JoinFoundRoom();
            return;
        }

        if (data.currentPlayers >= data.maxPlayers && data.maxPlayers > 0)
        {
            _pendingJoinRoomCode = null;
            _pendingOp = RoomOp.Unknown;
            CurrentRoomCode = string.Empty;
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Join, RoomErrorReason.RoomFull, $"Room is full: {data.serverId}");
            return;
        }

        if (!TryParseServerId(data.serverId, out string ip, out int port))
        {
            _pendingJoinRoomCode = null;
            _pendingOp = RoomOp.Unknown;
            CurrentRoomCode = string.Empty;
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Join, RoomErrorReason.RoomNotFound, $"Cannot parse serverId: {data.serverId}");
            return;
        }

        discovery?.StopListening();
        Debug.Log($"[NetworkRoomService] 短码命中并加入 {discoveredCode} → {data.serverId}");
        BeginClientJoin(ip, port);
    }

    /// <summary>在已发现缓存中按短码查找 serverId。</summary>
    private bool TryFindDiscoveredServerIdByCode(string normalizedCode, out string serverId)
    {
        serverId = null;
        foreach (RoomItemData room in _discoveredRooms.Values)
        {
            if (!RoomCodeUtil.TryNormalize(room.roomCode, out string code)) continue;
            if (!string.Equals(code, normalizedCode, StringComparison.Ordinal)) continue;
            serverId = room.serverId;
            return !string.IsNullOrEmpty(serverId);
        }
        return false;
    }

    /// <summary>开始 Mirror StartClient（调用方已处理好状态门禁 / 满员检查）。</summary>
    private void BeginClientJoin(string ip, int port)
    {
        _leaveRequested = false;
        _pendingOp = RoomOp.Join;
        SetConnectionState(RoomConnectionState.Connecting);

        // 本机互测（双编辑器 / ParrelSync）时，发现到的常是虚拟网卡 IP（如 172.22.x），
        // TCP 连不上；若该 IP 属于本机则改连 127.0.0.1。
        string connectIp = LanAddressUtil.ResolveClientConnectAddress(ip);

        try
        {
            // Mirror NetworkManager.networkAddress 只接受主机名/IP；端口写入 Transport（PortTransport：KCP/Telepathy/SimpleWeb）。
            networkManager.networkAddress = connectIp;
            ApplyJoinTransportPort(port);
            Debug.Log($"[NetworkRoomService] StartClient → {connectIp}:{port}" +
                      (connectIp != ip ? $"（发现地址 {ip} 已改写为本机回环）" : string.Empty));
            networkManager.StartClient();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkRoomService] 加入房间失败：{e.Message}");
            _pendingOp = RoomOp.Unknown;
            _pendingJoinRoomCode = null;
            CurrentRoomCode = string.Empty;
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Join, RoomErrorReason.ConnectionFailed, e.Message);
            return;
        }

        if (_joinTimeoutCoroutine != null) StopCoroutine(_joinTimeoutCoroutine);
        _joinTimeoutCoroutine = StartCoroutine(JoinTimeoutRoutine());
    }

    // ================================================================
    // 内部工具
    // ================================================================

    private void PruneStaleRooms()
    {
        if (_lastSeenAt.Count == 0) return;

        List<string> stale = null;
        foreach (KeyValuePair<string, float> kv in _lastSeenAt)
        {
            if (Time.unscaledTime - kv.Value > roomStaleTimeout)
            {
                (stale ??= new List<string>()).Add(kv.Key);
            }
        }

        if (stale == null) return;

        foreach (string id in stale)
        {
            _discoveredRooms.Remove(id);
            _lastSeenAt.Remove(id);
        }
        PublishRoomList();
    }

    private void PublishRoomList()
    {
        // 先更新 State 快照，再触发 Event
        _roomListSnapshot = _discoveredRooms.Values.Select(ToRoomInfo).ToList();
        OnRoomListUpdated?.Invoke(_roomListSnapshot);
    }

    private static RoomInfo ToRoomInfo(RoomItemData data) => new RoomInfo
    {
        serverId = data.serverId,
        roomName = data.roomName,
        hostName = data.hostName,
        currentPlayers = data.currentPlayers,
        maxPlayers = data.maxPlayers,
        status = data.status,
        ping = data.ping,
        roomCode = data.roomCode,
        seekerCount = data.seekerCount,
        hiderCount = data.hiderCount,
    };

    private bool TryParseServerId(string serverId, out string ip, out int port)
    {
        ip = null;
        // 缺省端口优先跟当前 Transport 同源，再回落 Inspector defaultPort
        port = CustomNetworkManager.TryGetTransportPort(out ushort transportPort)
            ? transportPort
            : defaultPort;

        if (string.IsNullOrEmpty(serverId)) return false;

        string[] parts = serverId.Split(':');
        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0])) return false;

        ip = parts[0];
        if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort)) port = parsedPort;
        return true;
    }

    /// <summary>把 JoinRoom 解析到的端口写入当前 Mirror Transport（须在 StartClient 之前）。</summary>
    private void ApplyJoinTransportPort(int port)
    {
        if (port < 0 || port > 65535)
        {
            Debug.LogWarning($"[NetworkRoomService] Join 端口非法：{port}");
            return;
        }

        if (CustomNetworkManager.TrySetTransportPort((ushort)port))
        {
            Debug.Log($"[NetworkRoomService] JoinRoom 已写入 Transport 端口：{port}");
            return;
        }

        Debug.LogWarning(
            "[NetworkRoomService] 当前 Transport 不是 PortTransport，无法写入 Join 端口。" +
            "请确认 NetworkManager 使用 KCP/Telepathy/SimpleWeb 等带 Port 的 Transport。");
    }

    private void SetConnectionState(RoomConnectionState state)
    {
        if (ConnectionState == state) return;
        ConnectionState = state;
        OnConnectionStateChanged?.Invoke(state);
    }

    private void RaiseError(RoomOp op, RoomErrorReason reason, string message)
    {
        Debug.LogWarning($"[NetworkRoomService] 房间操作失败 op={op} reason={reason} msg={message}");
        OnRoomError?.Invoke(new RoomError { op = op, reason = reason, message = message });
    }
}
