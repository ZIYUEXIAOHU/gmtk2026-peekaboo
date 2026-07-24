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
    [Tooltip("ManualDiscovery 当前固定使用的游戏端口，仅在 serverId 缺省端口时兜底")]
    [SerializeField] private int defaultPort = 7777;

    // ---- IRoomStateReadonly ----
    public RoomConnectionState ConnectionState { get; private set; } = RoomConnectionState.Disconnected;
    public IReadOnlyList<RoomInfo> RoomList => _roomListSnapshot;

    // ---- IRoomEvents ----
    public event Action<RoomConnectionState> OnConnectionStateChanged;
    public event Action<IReadOnlyList<RoomInfo>> OnRoomListUpdated;
    public event Action<RoomError> OnRoomError;

    private readonly Dictionary<string, RoomItemData> _discoveredRooms = new Dictionary<string, RoomItemData>();
    private readonly Dictionary<string, float> _lastSeenAt = new Dictionary<string, float>();
    private List<RoomInfo> _roomListSnapshot = new List<RoomInfo>();

    private RoomOp _pendingOp = RoomOp.Unknown;
    private Coroutine _joinTimeoutCoroutine;
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
            RaiseError(RoomOp.Refresh, RoomErrorReason.ConnectionFailed, "ManualDiscovery 未找到");
            return;
        }

        discovery.StopListening();
        if (!discovery.StartListening())
        {
            RaiseError(RoomOp.Refresh, RoomErrorReason.ConnectionFailed, "局域网监听端口启动失败");
        }
    }

    public void CreateRoom(string roomName, int maxPlayers)
    {
        EnsureRefs();

        if (ConnectionState != RoomConnectionState.Disconnected &&
            ConnectionState != RoomConnectionState.Failed)
        {
            RaiseError(RoomOp.Create, RoomErrorReason.AlreadyInRoom, $"当前状态 {ConnectionState} 下不允许创建房间");
            return;
        }

        if (networkManager == null)
        {
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Create, RoomErrorReason.ConnectionFailed, "CustomNetworkManager 未找到");
            return;
        }

        _leaveRequested = false;
        _pendingOp = RoomOp.Create;
        SetConnectionState(RoomConnectionState.Connecting);

        try
        {
            string finalName = string.IsNullOrWhiteSpace(roomName) ? "躲猫猫房间" : roomName;
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
            RaiseError(RoomOp.Create, RoomErrorReason.ConnectionFailed, "StartHost 后 NetworkServer 未激活");
            return;
        }

        _pendingOp = RoomOp.Unknown;
        SetConnectionState(RoomConnectionState.InRoom);
        discovery?.StartBroadcasting();
    }

    public void JoinRoom(string serverId)
    {
        EnsureRefs();

        if (ConnectionState != RoomConnectionState.Disconnected &&
            ConnectionState != RoomConnectionState.Failed)
        {
            RaiseError(RoomOp.Join, RoomErrorReason.AlreadyInRoom, $"当前状态 {ConnectionState} 下不允许加入房间");
            return;
        }

        if (networkManager == null)
        {
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Join, RoomErrorReason.ConnectionFailed, "CustomNetworkManager 未找到");
            return;
        }

        if (!TryParseServerId(serverId, out string ip, out int port))
        {
            RaiseError(RoomOp.Join, RoomErrorReason.RoomNotFound, $"无法解析 serverId：{serverId}");
            return;
        }

        if (_discoveredRooms.TryGetValue(serverId, out RoomItemData room) &&
            room.currentPlayers >= room.maxPlayers &&
            room.maxPlayers > 0)
        {
            RaiseError(RoomOp.Join, RoomErrorReason.RoomFull, $"房间已满：{serverId}");
            return;
        }

        _leaveRequested = false;
        _pendingOp = RoomOp.Join;
        SetConnectionState(RoomConnectionState.Connecting);

        try
        {
            // Mirror NetworkManager.networkAddress 只接受主机名/IP；端口写入 Transport（PortTransport：KCP/Telepathy/SimpleWeb）。
            networkManager.networkAddress = ip;
            ApplyJoinTransportPort(port);
            networkManager.StartClient();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkRoomService] 加入房间失败：{e.Message}");
            _pendingOp = RoomOp.Unknown;
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(RoomOp.Join, RoomErrorReason.ConnectionFailed, e.Message);
            return;
        }

        if (_joinTimeoutCoroutine != null) StopCoroutine(_joinTimeoutCoroutine);
        _joinTimeoutCoroutine = StartCoroutine(JoinTimeoutRoutine());
    }

    public void LeaveRoom()
    {
        EnsureRefs();

        _leaveRequested = true;
        CancelJoinTimeout();
        discovery?.StopBroadcasting();

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
        _pendingOp = RoomOp.Unknown;
        SetConnectionState(RoomConnectionState.InRoom);
    }

    public void NotifyClientDisconnected()
    {
        CancelJoinTimeout();

        // LeaveRoom 已主动设为 Disconnected，忽略后续 Mirror 回调，避免覆盖。
        if (_leaveRequested)
        {
            _leaveRequested = false;
            _pendingOp = RoomOp.Unknown;
            if (ConnectionState != RoomConnectionState.Disconnected)
                SetConnectionState(RoomConnectionState.Disconnected);
            return;
        }

        if (ConnectionState == RoomConnectionState.Connecting)
        {
            RoomOp op = _pendingOp == RoomOp.Unknown ? RoomOp.Join : _pendingOp;
            _pendingOp = RoomOp.Unknown;
            SetConnectionState(RoomConnectionState.Failed);
            RaiseError(op, RoomErrorReason.ConnectionFailed, "连接过程中断开");
            return;
        }

        if (ConnectionState == RoomConnectionState.InRoom)
        {
            _pendingOp = RoomOp.Unknown;
            SetConnectionState(RoomConnectionState.Disconnected);
            return;
        }

        // Failed / Disconnected：保持现状（超时路径可能已设 Failed）
        _pendingOp = RoomOp.Unknown;
    }

    public void NotifyClientError(string reason)
    {
        CancelJoinTimeout();

        if (_leaveRequested)
        {
            _pendingOp = RoomOp.Unknown;
            return;
        }

        RoomOp op = _pendingOp == RoomOp.Unknown ? RoomOp.Join : _pendingOp;
        _pendingOp = RoomOp.Unknown;
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

    private System.Collections.IEnumerator JoinTimeoutRoutine()
    {
        yield return new WaitForSecondsRealtime(joinTimeoutSeconds);
        _joinTimeoutCoroutine = null;

        if (ConnectionState != RoomConnectionState.Connecting) yield break;

        Debug.LogWarning("[NetworkRoomService] 加入房间超时");
        _pendingOp = RoomOp.Unknown;
        SetConnectionState(RoomConnectionState.Failed);
        RaiseError(RoomOp.Join, RoomErrorReason.Timeout, "连接超时");

        if (networkManager != null && NetworkClient.active)
            networkManager.StopClient();
    }

    // ================================================================
    // 供 ManualDiscovery 上报发现的房间（UDP 广播解析结果）
    // ================================================================

    public void ReportDiscoveredRoom(RoomItemData data)
    {
        if (data == null || string.IsNullOrEmpty(data.serverId)) return;

        _discoveredRooms[data.serverId] = data;
        _lastSeenAt[data.serverId] = Time.unscaledTime;
        PublishRoomList();
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
