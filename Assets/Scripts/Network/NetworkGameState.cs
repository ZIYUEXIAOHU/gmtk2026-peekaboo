// ============================================================
// Program 1: Match authority core (Wave 1 + Wave 2 + Wave 3)
// Implements contracts IGameStateReadonly / IGameCommands / IGameEvents,
// covering: phase state machine, role slots, host start,
// PlaceItem / Investigate / Slash / Capture,
// Hider periodic transform / Seeker heartbeat / results wrap-up.
//
// Player list source: all RoomPlayer in the scene.
// Host check: RoomPlayer.isRoomHost.
// IPlayerStateReadonly is attached to RoomPlayer.
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public class NetworkGameState : NetworkBehaviour, IGameStateReadonly, IGameCommands, IGameEvents
{
    public static NetworkGameState Instance { get; private set; }

    const string ResourcesPrefabPath = "NetworkGameState";
    const string PlaceholderPrefabPath = "InvestigableItemPlaceholder";

    /// <summary>
    /// 放置占位半径：与已有可调查物中心距离小于此值视为 NoSpace。
    /// 当前无完整碰撞关卡时，用圆心距离做合理占位检测（见 HasPlaceSpace）。
    /// </summary>
    const float PlaceOccupyRadius = 0.5f;

    /// <summary>ItemTable 为空时的兜底物品栏长度（itemId 0..N-1，外观走占位 prefab）。</summary>
    const int FallbackQueueLength = 3;

    [Header("身份名额（默认值，可按最终人数调整）")]
    [SerializeField] private int seekerMax = 3;
    [SerializeField] private int hiderMax = 3;

    [Header("Prep 阶段：躲藏者出生 / 地图切换")]
    [Tooltip("四房间出生点（优先）；留空则运行时收集场景中的 HiderSpawnPoint")]
    [SerializeField] private Transform[] hiderRoomSpawns;
    [Tooltip("四房间地图根节点；留空则按名称查找")]
    [SerializeField] private Transform matchMapRoot;
    [Tooltip("小队大厅玩法区；留空则按名称查找")]
    [SerializeField] private Transform lobbyPlayArea;
    [SerializeField] private string matchMapRootName = "GameScene";
    [SerializeField] private string lobbyPlayAreaName = "LobbyScene";
    [Tooltip("无房间出生点时的回退：圆心（留空则世界原点）")]
    [SerializeField] private Transform hiderSpawnCenter;
    [SerializeField] private float hiderSpawnRadius = 6f;

    [Header("Wave 2：放置 / 调查")]
    [Tooltip("共享物品表；可空——空表时用占位 prefab + 合成 itemId")]
    [SerializeField] private ItemTable itemTable;
    [Tooltip("ItemTable 条目 prefab 缺失时的网络占位物（Resources/InvestigableItemPlaceholder）")]
    [SerializeField] private GameObject investigablePlaceholderPrefab;

    // ---- 阶段机（服务端权威） ----
    [SyncVar]
    private GamePhase phase = GamePhase.Waiting;

    /// <summary>当前阶段结束时刻（服务端 NetworkTime.time）。</summary>
    [SyncVar]
    private double phaseEndTime;

    [SyncVar]
    private MatchResult result;

    private double matchStartServerTime;

    // ---- Wave 3：变身 / 心跳（仅服务端计时，Ended/Prep 不跑）----
    /// <summary>下一次全体变身时刻（NetworkTime.time）。SyncVar 供客户端算 NextTransformTimeLeft。</summary>
    [SyncVar]
    private double nextTransformTime;

    /// <summary>本轮变身隐身结束时刻；到点把仍 Invisible 的存活躲藏者改回 Disguised。</summary>
    private double invisibleRevealTime;

    private bool pendingInvisibleReveal;

    /// <summary>下一次心跳脉冲时刻。</summary>
    private double nextHeartbeatTime;

    private int heartbeatBeatIndex;

    /// <summary>
    /// 联调：练习大厅 Waiting 也广播心跳（无权威判定，仅方便程序 2 跟跳）。
    /// 正式局仅 Playing 发；Prep / Ended 一律不发。
    /// </summary>
    const bool HeartbeatInPracticeLobby = true;

    /// <summary>
    /// HostStart 已通过校验并请求切到 gameScene；等 OnServerSceneChanged 后再 StartPrep。
    /// 切场景后本对象 DontDestroyOnLoad 仍存活；勿在 Waiting+CanStart 时自动开局。
    /// </summary>
    bool pendingPrepAfterSceneChange;

    // ================================================================
    // 生命周期 / 生成
    // ================================================================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[NetworkGameState] 已存在实例，销毁重复对象");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        EnsureItemResources();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        EnsureItemResources();
        RegisterInvestigablePrefabs();
        GameContract.Bind(this, this, this);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        EnsureItemResources();
        RegisterInvestigablePrefabs();
        phase = GamePhase.Waiting;
        phaseEndTime = 0;
        result = default;
        DontDestroyOnLoad(gameObject);

        if (!GameContract.IsBound)
        {
            GameContract.Bind(this, this, this);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            if (GameContract.IsBound && ReferenceEquals(GameContract.State, this))
            {
                GameContract.Unbind();
            }
        }
    }

    [ServerCallback]
    void Update()
    {
        TickPhase();
        TickTransform();
        TickHeartbeat();
    }

    /// <summary>由 CustomNetworkManager 在 OnStartServer 调用：若尚无实例则从 Resources 生成并 Spawn。</summary>
    [Server]
    public static void ServerEnsureSpawned()
    {
        if (Instance != null) return;

        GameObject prefab = Resources.Load<GameObject>(ResourcesPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[NetworkGameState] Resources/{ResourcesPrefabPath} 预制体未找到，无法生成对局权威状态。");
            return;
        }

        NetworkClient.RegisterPrefab(prefab);
        GameObject go = Instantiate(prefab);
        DontDestroyOnLoad(go);
        NetworkServer.Spawn(go);
        Debug.Log("[NetworkGameState] 已在服务端生成并对客户端 Spawn。");
    }

    /// <summary>玩家进出导致名额变化时，由 CustomNetworkManager 调用刷新 Slots 事件。</summary>
    [Server]
    public void ServerNotifyRoleSlotsChanged()
    {
        RpcRoleSlotsChanged(ComputeSlots());
    }

    /// <summary>向指定连接补发当前名额（客户端场景/UI 晚于 Rpc 时兜底）。</summary>
    [Server]
    public void ServerSendRoleSlotsTo(NetworkConnectionToClient conn)
    {
        if (conn == null) return;
        TargetRoleSlotsChanged(conn, ComputeSlots());
    }

    /// <summary>按当前房间人数重算身份上限（与 LobbyRoomController 规则一致）。</summary>
    [Server]
    void RecalculateRoleMaxFromPlayerCount(int totalPlayers)
    {
        ComputeRoleMax(totalPlayers, out seekerMax, out hiderMax);
    }

    // ================================================================
    // IGameStateReadonly
    // ================================================================

    public GamePhase Phase => phase;

    public float PhaseTimeLeft
    {
        get
        {
            if (phase == GamePhase.Waiting || phase == GamePhase.Ended) return 0f;
            return Mathf.Max(0f, (float)(phaseEndTime - NetworkTime.time));
        }
    }

    public float NextTransformTimeLeft
    {
        get
        {
            if (phase != GamePhase.Playing) return 0f;
            if (nextTransformTime >= double.MaxValue / 2) return 0f;
            return Mathf.Max(0f, (float)(nextTransformTime - NetworkTime.time));
        }
    }

    public int AliveHiders => GetAllRoomPlayers()
        .Count(p => p.Role == PlayerRole.Hider && p.HiderState != HiderState.Captured);

    public int TotalHiders => GetAllRoomPlayers().Count(p => p.Role == PlayerRole.Hider);

    public IPlayerStateReadonly LocalPlayer
    {
        get
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            return local != null ? local.GetComponent<RoomPlayer>() : null;
        }
    }

    public IReadOnlyList<IPlayerStateReadonly> Players =>
        GetAllRoomPlayers().Cast<IPlayerStateReadonly>().ToList();

    public bool IsLocalPlayerHost
    {
        get
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            RoomPlayer rp = local != null ? local.GetComponent<RoomPlayer>() : null;
            return rp != null && rp.isRoomHost;
        }
    }

    public RoleSlots Slots => ComputeSlots();

    public bool IsPracticeLobby => phase == GamePhase.Waiting;

    public MatchResult Result => result;

    // ================================================================
    // IGameEvents
    // ================================================================

    public event Action<GamePhase, float> OnPhaseChanged;
    public event Action<RoleSlots> OnRoleSlotsChanged;
    public event Action<MatchResult> OnGameEnded;
    public event Action<CommandRejected> OnCommandRejected;

    public event Action<PlaceItemResult> OnPlaceResult;
    public event Action<TransformInfo> OnHiderTransformed;
    public event Action<RespawnInfo> OnHiderRespawned;

    public event Action<InvestigateInfo> OnInvestigated;
    public event Action<SlashInfo> OnSlashed;
    public event Action<CaptureInfo> OnCaptured;

    public event Action<HeartbeatPulse> OnHeartbeatPulse;

    // ================================================================
    // IGameCommands — 客户端入口（转发到 [Command]）
    // ================================================================

    public void SelectRole(PlayerRole role) => CmdSelectRole(role);

    public void HostStartGame() => CmdHostStartGame();

    public void ReturnToWaiting() => CmdReturnToWaiting();

    public void PlaceItem() => CmdPlaceItem();

    public void Investigate(Vector2 mouseWorldPosition) => CmdInvestigate(mouseWorldPosition);

    public void Slash(Vector2 effectWorldPosition) => CmdSlash(effectWorldPosition);

    // ================================================================
    // Commands — 服务端裁定（单例场景物体，requiresAuthority=false，靠 sender 区分调用者）
    // ================================================================

    [Command(requiresAuthority = false)]
    private void CmdSelectRole(PlayerRole role, NetworkConnectionToClient sender = null)
    {
        RoomPlayer rp = GetRoomPlayer(sender);
        if (rp == null) return;

        if (phase != GamePhase.Waiting)
        {
            RejectCommand(sender, GameCommandType.SelectRole, RejectReason.WrongPhase);
            return;
        }

        if (role == PlayerRole.None)
        {
            if (rp.role == PlayerRole.None) return;
            ServerClearPlayerRole(rp);
            RpcRoleSlotsChanged(ComputeSlots());
            return;
        }

        if (role != PlayerRole.Seeker && role != PlayerRole.Hider)
        {
            RejectCommand(sender, GameCommandType.SelectRole, RejectReason.InvalidRole);
            return;
        }

        if (rp.role == role) return;

        List<RoomPlayer> all = GetAllRoomPlayers();
        int seekerCount = all.Count(p => p.role == PlayerRole.Seeker) - (rp.role == PlayerRole.Seeker ? 1 : 0);
        int hiderCount = all.Count(p => p.role == PlayerRole.Hider) - (rp.role == PlayerRole.Hider ? 1 : 0);

        if (role == PlayerRole.Seeker && seekerCount >= seekerMax)
        {
            RejectCommand(sender, GameCommandType.SelectRole, RejectReason.RoleFull);
            return;
        }
        if (role == PlayerRole.Hider && hiderCount >= hiderMax)
        {
            RejectCommand(sender, GameCommandType.SelectRole, RejectReason.RoleFull);
            return;
        }

        // 1) 先更新 State
        rp.role = role;
        if (role == PlayerRole.Hider)
        {
            ServerFillHiderItemQueue(rp);
            rp.hiderState = HiderState.Disguised;
            rp.disguiseItemId = PickRandomDisguiseItemId(GameConstants.InvalidItemId);
        }
        else
        {
            rp.itemQueue.Clear();
            rp.hiderState = HiderState.Disguised;
            rp.disguiseItemId = GameConstants.InvalidItemId;
        }

        if (rp.isRoomHost)
            rp.isReady = true;

        // 2) 再触发 Event（全员）
        RpcRoleSlotsChanged(ComputeSlots());
    }

    [Command(requiresAuthority = false)]
    private void CmdHostStartGame(NetworkConnectionToClient sender = null)
    {
        RoomPlayer rp = GetRoomPlayer(sender);
        if (rp == null || !rp.isRoomHost)
        {
            RejectCommand(sender, GameCommandType.HostStartGame, RejectReason.NotHost);
            return;
        }

        if (phase != GamePhase.Waiting)
        {
            RejectCommand(sender, GameCommandType.HostStartGame, RejectReason.WrongPhase);
            return;
        }

        RoleSlots slots = ComputeSlots();
        if (!slots.CanStart)
        {
            RejectCommand(sender, GameCommandType.HostStartGame, RejectReason.NotEnoughPlayers);
            return;
        }

        List<RoomPlayer> all = GetAllRoomPlayers();
        if (all.Count < 2 || all.Any(p => p.role == PlayerRole.None || !p.isReady))
        {
            RejectCommand(sender, GameCommandType.HostStartGame, RejectReason.PlayersNotReady);
            return;
        }

        // 权威开局时序：校验通过 →（如需）切 gameScene → Prep。
        // NetworkGameState 为 DontDestroyOnLoad；切场景后由 OnServerSceneChanged 续跑 Prep。
        CustomNetworkManager nm = NetworkManager.singleton as CustomNetworkManager;
        string targetScene = nm != null ? nm.gameScene : null;
        string activeScene = SceneManager.GetActiveScene().name;

        if (!string.IsNullOrEmpty(targetScene) &&
            !string.Equals(activeScene, targetScene, StringComparison.Ordinal) &&
            nm != null)
        {
            pendingPrepAfterSceneChange = true;
            Debug.Log($"[NetworkGameState] HostStart：切场景 {activeScene} → {targetScene}，随后进入 Prep。");
            nm.ServerChangeScene(targetScene);
            return;
        }

        StartPrepPhase();
    }

    [Command(requiresAuthority = false)]
    private void CmdReturnToWaiting(NetworkConnectionToClient sender = null)
    {
        RoomPlayer rp = GetRoomPlayer(sender);
        if (rp == null) return;

        if (phase != GamePhase.Ended)
        {
            RejectCommand(sender, GameCommandType.ReturnToWaiting, RejectReason.WrongPhase);
            return;
        }

        ServerReturnToWaiting();
    }

    /// <summary>
    /// 由 CustomNetworkManager.OnServerSceneChanged 调用：仅当 HostStart 已挂起 Prep 时续跑。
    /// 进入 gameScene 且 phase 仍 Waiting / CanStart 时不会自动开局。
    /// </summary>
    [Server]
    public void ServerTryStartPendingPrepAfterSceneChange()
    {
        if (!pendingPrepAfterSceneChange) return;

        pendingPrepAfterSceneChange = false;
        if (phase != GamePhase.Waiting)
        {
            Debug.LogWarning($"[NetworkGameState] 切场景后挂起 Prep 已取消（当前 phase={phase}）。");
            return;
        }

        StartPrepPhase();
    }

    [Command(requiresAuthority = false)]
    private void CmdPlaceItem(NetworkConnectionToClient sender = null)
    {
        RoomPlayer rp = GetRoomPlayer(sender);
        if (rp == null) return;

        if (rp.role != PlayerRole.Hider)
        {
            SendPlaceResult(sender, new PlaceItemResult
            {
                hiderNetId = rp.netId,
                success = false,
                failReason = PlaceFailReason.WrongRole,
                itemId = GameConstants.InvalidItemId,
            });
            return;
        }

        if (phase != GamePhase.Prep && !IsPracticeLobby)
        {
            SendPlaceResult(sender, new PlaceItemResult
            {
                hiderNetId = rp.netId,
                success = false,
                failReason = PlaceFailReason.NotPrepPhase,
                itemId = GameConstants.InvalidItemId,
            });
            return;
        }

        if (rp.itemQueue.Count == 0)
        {
            SendPlaceResult(sender, new PlaceItemResult
            {
                hiderNetId = rp.netId,
                success = false,
                failReason = PlaceFailReason.NoItemLeft,
                itemId = GameConstants.InvalidItemId,
            });
            return;
        }

        int itemId = rp.itemQueue[0];
        Vector2 placePos = rp.transform.position;

        if (!HasPlaceSpace(placePos, rp))
        {
            SendPlaceResult(sender, new PlaceItemResult
            {
                hiderNetId = rp.netId,
                success = false,
                failReason = PlaceFailReason.NoSpace,
                itemId = itemId,
                position = placePos,
            });
            return;
        }

        GameObject prefab = ResolveItemPrefab(itemId);
        if (prefab == null)
        {
            Debug.LogError($"[NetworkGameState] PlaceItem：无法解析 itemId={itemId} 的 prefab（ItemTable/占位均缺失）。");
            SendPlaceResult(sender, new PlaceItemResult
            {
                hiderNetId = rp.netId,
                success = false,
                failReason = PlaceFailReason.NoItemLeft,
                itemId = itemId,
            });
            return;
        }

        // 1) 先更新 State：弹出队首 + Spawn 可调查物
        rp.itemQueue.RemoveAt(0);

        // Mirror：NetworkBehaviour 必须在预制体上；缺 InvestigableObject 时改用占位 prefab
        if (prefab.GetComponent<InvestigableObject>() == null ||
            prefab.GetComponent<NetworkIdentity>() == null)
        {
            if (investigablePlaceholderPrefab == null)
            {
                Debug.LogError("[NetworkGameState] PlaceItem：条目 prefab 不可网络生成且无占位 prefab，已回滚。");
                rp.itemQueue.Insert(0, itemId);
                SendPlaceResult(sender, new PlaceItemResult
                {
                    hiderNetId = rp.netId,
                    success = false,
                    failReason = PlaceFailReason.NoItemLeft,
                    itemId = itemId,
                });
                return;
            }
            Debug.LogWarning(
                $"[NetworkGameState] PlaceItem：itemId={itemId} prefab「{prefab.name}」缺少 NetworkIdentity/InvestigableObject，改用占位预制体。");
            prefab = investigablePlaceholderPrefab;
        }

        GameObject go = Instantiate(prefab, placePos, Quaternion.identity);
        InvestigableObject investigable = go.GetComponent<InvestigableObject>();
        // 放置物为诱饵，不关联躲藏者本体
        investigable.ServerInit(itemId, GameConstants.InvalidNetId);

        NetworkClient.RegisterPrefab(prefab);
        NetworkServer.Spawn(go);

        // 练习大厅：无限放置——放回一个同类 itemId，不阻塞正式局队列消耗逻辑
        if (IsPracticeLobby)
        {
            rp.itemQueue.Add(itemId);
        }

        PlaceItemResult ok = new PlaceItemResult
        {
            hiderNetId = rp.netId,
            success = true,
            failReason = PlaceFailReason.None,
            itemId = itemId,
            position = placePos,
        };

        // 2) 再触发 Event（成功 → 全员）
        SendPlaceResult(sender, ok);
    }

    [Command(requiresAuthority = false)]
    private void CmdInvestigate(Vector2 mouseWorldPosition, NetworkConnectionToClient sender = null)
    {
        RoomPlayer seeker = GetRoomPlayer(sender);
        if (seeker == null) return;

        if (seeker.role != PlayerRole.Seeker)
        {
            RejectCommand(sender, GameCommandType.Investigate, RejectReason.WrongRole);
            return;
        }
        if (phase != GamePhase.Playing && !IsPracticeLobby)
        {
            RejectCommand(sender, GameCommandType.Investigate, RejectReason.WrongPhase);
            return;
        }

        Vector2 origin = seeker.transform.position;
        if (!TryFindInvestigableUnderCursor(
                origin,
                mouseWorldPosition,
                GameConstants.InvestigateRange,
                GameConstants.InvestigateCursorPickRadius,
                out InvestigableTarget target))
        {
            RejectCommand(sender, GameCommandType.Investigate, RejectReason.InvalidTarget);
            return;
        }

        bool hitHider = false;
        RoomPlayer hitPlayer = null;

        if (target.linkedHider != null)
        {
            hitPlayer = target.linkedHider;
            // Invisible 无敌期内不可被调查成鬼魂：视为未命中躲藏者（仍广播噪音）。
            // 变身把 Ghost 拉回 Invisible 后，需再调查才能再次 Ghost。
            if (hitPlayer.hiderState == HiderState.Disguised)
            {
                hitHider = true;
            }
            else if (hitPlayer.hiderState == HiderState.Invisible)
            {
                hitHider = false;
            }
            else
            {
                // Ghost / Captured 不应进入候选；兜底按未命中
                hitHider = false;
            }
        }

        // 1) 先更新 State
        if (hitHider && hitPlayer != null)
        {
            hitPlayer.hiderState = HiderState.Ghost;
            // Ghost 持续到下次随机变身（TickTransform 会拉回 Invisible）。
        }

        InvestigateInfo info = new InvestigateInfo
        {
            seekerNetId = seeker.netId,
            targetNetId = target.netId,
            hitHider = hitHider,
            noisePosition = target.position,
        };

        // 2) 再触发 Event（全员）
        RpcInvestigated(info);
    }

    [Command(requiresAuthority = false)]
    private void CmdSlash(Vector2 effectWorldPosition, NetworkConnectionToClient sender = null)
    {
        RoomPlayer seeker = GetRoomPlayer(sender);
        if (seeker == null) return;

        if (seeker.role != PlayerRole.Seeker)
        {
            RejectCommand(sender, GameCommandType.Slash, RejectReason.WrongRole);
            return;
        }
        if (phase != GamePhase.Playing && !IsPracticeLobby)
        {
            RejectCommand(sender, GameCommandType.Slash, RejectReason.WrongPhase);
            return;
        }

        Vector2 origin = seeker.transform.position;
        Vector2 effectPos = ClampMouseSlashPosition(origin, effectWorldPosition);
        RoomPlayer ghost = FindNearestGhostHiderDual(
            origin, GameConstants.SlashRange,
            effectPos, GameConstants.MouseSlashRange);

        bool hitGhost = ghost != null;
        uint targetNetId = hitGhost ? ghost.netId : GameConstants.InvalidNetId;

        SlashInfo slashInfo = new SlashInfo
        {
            seekerNetId = seeker.netId,
            hitGhost = hitGhost,
            targetNetId = targetNetId,
            position = origin,
            effectPosition = effectPos,
        };

        // 练习大厅：无鬼魂也可「随意劈砍」——仍广播未命中的 OnSlashed，方便程序 2 播特效。
        // 1) 先更新 State（若命中）
        if (hitGhost)
        {
            ghost.hiderState = HiderState.Captured;
            int alive = AliveHiders;

            // 2a) OnSlashed 全员
            RpcSlashed(slashInfo);

            CaptureInfo captureInfo = new CaptureInfo
            {
                hiderNetId = ghost.netId,
                seekerNetId = seeker.netId,
                aliveHiders = alive,
            };
            // 2b) OnCaptured 全员
            RpcCaptured(captureInfo);

            if (IsPracticeLobby)
            {
                // 练习大厅无限复活：恢复伪装并广播复活（不推进 Ended）
                ServerPracticeRespawnHider(ghost);
            }
            else if (phase == GamePhase.Playing && alive <= 0)
            {
                EndGame(GameResult.SeekersWin);
            }
        }
        else
        {
            RpcSlashed(slashInfo);
        }
    }

    // ================================================================
    // 阶段机（服务端）
    // ================================================================

    [Server]
    private void TickPhase()
    {
        switch (phase)
        {
            case GamePhase.Prep:
                if (NetworkTime.time >= phaseEndTime) BeginPlaying();
                break;

            case GamePhase.Playing:
                if (TotalHiders > 0 && AliveHiders <= 0)
                {
                    EndGame(GameResult.SeekersWin);
                    return;
                }
                if (NetworkTime.time >= phaseEndTime) EndGame(GameResult.HidersWin);
                break;
        }
    }

    [Server]
    private void StartPrepPhase()
    {
        ActivateMatchMap();
        ScatterHiderSpawns();
        foreach (RoomPlayer hider in GetAllRoomPlayers().Where(p => p.role == PlayerRole.Hider))
        {
            ServerFillHiderItemQueue(hider);
            hider.hiderState = HiderState.Disguised;
            hider.disguiseItemId = PickRandomDisguiseItemId(GameConstants.InvalidItemId);
        }
        SetPhase(GamePhase.Prep, GameConstants.PrepDuration);
    }

    [Server]
    private void BeginPlaying()
    {
        matchStartServerTime = NetworkTime.time;
        // 变身：首拍在 TransformInterval 后；心跳：进入 Playing 即开始计节拍
        nextTransformTime = matchStartServerTime + GameConstants.TransformInterval;
        pendingInvisibleReveal = false;
        invisibleRevealTime = 0;
        nextHeartbeatTime = NetworkTime.time;
        heartbeatBeatIndex = 0;
        SetPhase(GamePhase.Playing, GameConstants.MatchDuration);
    }

    [Server]
    private void EndGame(GameResult gameResult)
    {
        if (phase == GamePhase.Ended) return;

        // Ended 后停止变身 / 心跳计时
        pendingInvisibleReveal = false;
        nextTransformTime = double.MaxValue;
        nextHeartbeatTime = double.MaxValue;

        result = new MatchResult
        {
            result = gameResult,
            survivors = AliveHiders,
            duration = matchStartServerTime > 0
                ? (float)(NetworkTime.time - matchStartServerTime)
                : 0f,
        };
        phase = GamePhase.Ended;
        phaseEndTime = NetworkTime.time;

        RpcPhaseChanged(GamePhase.Ended, 0f);
        RpcGameEnded(result);
    }

    /// <summary>结算后回到小队练习房间：清放置物、重置玩家、切回大厅地图、Waiting。</summary>
    [Server]
    private void ServerReturnToWaiting()
    {
        if (phase != GamePhase.Ended) return;

        pendingInvisibleReveal = false;
        nextTransformTime = double.MaxValue;
        nextHeartbeatTime = double.MaxValue;
        pendingPrepAfterSceneChange = false;
        matchStartServerTime = 0;
        result = default;

        ServerClearPlacedInvestigables();
        ServerResetPlayersForLobby();
        ActivateLobbyMap();

        SetPhase(GamePhase.Waiting, 0f);
        RpcRoleSlotsChanged(ComputeSlots());
        Debug.Log("[NetworkGameState] 已回到 Waiting 练习房间。");
    }

    [Server]
    private void ServerClearPlacedInvestigables()
    {
        InvestigableObject[] objs = FindObjectsOfType<InvestigableObject>();
        for (int i = 0; i < objs.Length; i++)
        {
            InvestigableObject obj = objs[i];
            if (obj == null) continue;
            // 仅销毁运行时 Spawn 的放置物，保留场景预摆
            NetworkIdentity identity = obj.netIdentity;
            if (identity == null || identity.sceneId != 0) continue;
            NetworkServer.Destroy(obj.gameObject);
        }
    }

    [Server]
    private void ServerResetPlayersForLobby()
    {
        CustomNetworkManager nm = NetworkManager.singleton as CustomNetworkManager;
        List<RoomPlayer> all = GetAllRoomPlayers();
        for (int i = 0; i < all.Count; i++)
        {
            RoomPlayer rp = all[i];
            if (rp == null) continue;

            if (!rp.isRoomHost)
                rp.isReady = false;
            else
                rp.isReady = true;

            if (rp.role == PlayerRole.Hider)
            {
                ServerFillHiderItemQueue(rp);
                rp.hiderState = HiderState.Disguised;
                rp.disguiseItemId = PickRandomDisguiseItemId(GameConstants.InvalidItemId);
            }
            else
            {
                rp.itemQueue.Clear();
                rp.hiderState = HiderState.Disguised;
                rp.disguiseItemId = GameConstants.InvalidItemId;
            }

            Vector3 spawn = nm != null
                ? nm.GetLobbySpawnPosition(rp.role)
                : FallbackLobbySpawn(rp.role);
            TeleportHider(rp, spawn);
        }
    }

    static Vector3 FallbackLobbySpawn(PlayerRole role)
    {
        if (role == PlayerRole.Hider) return new Vector3(-3f, -2f, 0);
        if (role == PlayerRole.Seeker) return new Vector3(3f, -2f, 0);
        return new Vector3(0, -2f, 0);
    }

    [Server]
    private void SetPhase(GamePhase newPhase, float duration)
    {
        phase = newPhase;
        phaseEndTime = NetworkTime.time + duration;
        RpcPhaseChanged(newPhase, duration);
    }

    /// <summary>
    /// Prep：启用四房间地图，隐藏小队大厅玩法区。
    /// NetworkGameState 为 DDOL 预制体，场景引用通常为空，故按名称在活动场景根节点查找。
    /// 服务端与客户端都会调用（客户端经 RpcPhaseChanged）。
    /// </summary>
    private void ActivateMatchMap()
    {
        ResolveMapRoots();

        if (matchMapRoot != null)
            matchMapRoot.gameObject.SetActive(true);
        else
            Debug.LogWarning($"[NetworkGameState] 未找到对局地图根节点「{matchMapRootName}」，无法启用四房间地图。");

        if (lobbyPlayArea != null)
            lobbyPlayArea.gameObject.SetActive(false);
    }

    /// <summary>Waiting：启用小队大厅玩法区，隐藏四房间对局地图。</summary>
    private void ActivateLobbyMap()
    {
        ResolveMapRoots();

        if (lobbyPlayArea != null)
            lobbyPlayArea.gameObject.SetActive(true);
        else
            Debug.LogWarning($"[NetworkGameState] 未找到大厅玩法区「{lobbyPlayAreaName}」，无法切回练习房间。");

        if (matchMapRoot != null)
            matchMapRoot.gameObject.SetActive(false);
    }

    private void ResolveMapRoots()
    {
        if (matchMapRoot != null && lobbyPlayArea != null) return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (matchMapRoot == null && root.name == matchMapRootName)
                matchMapRoot = root.transform;
            if (lobbyPlayArea == null && root.name == lobbyPlayAreaName)
                lobbyPlayArea = root.transform;
        }
    }

    [Server]
    private void ScatterHiderSpawns()
    {
        List<RoomPlayer> hiders = GetAllRoomPlayers().Where(p => p.role == PlayerRole.Hider).ToList();
        int count = hiders.Count;
        if (count == 0) return;

        List<Transform> spawns = CollectHiderRoomSpawns();
        if (spawns.Count == 0)
        {
            Debug.LogWarning("[NetworkGameState] 未找到 HiderSpawnPoint / hiderRoomSpawns，回退到圆心分散。");
            ScatterHidersInCircle(hiders);
            return;
        }

        // 打乱后尽量一人一房；人数 > 点数时循环复用
        Shuffle(spawns);
        for (int i = 0; i < count; i++)
        {
            Transform spawn = spawns[i % spawns.Count];
            TeleportHider(hiders[i], spawn.position);
        }
    }

    private List<Transform> CollectHiderRoomSpawns()
    {
        var result = new List<Transform>();
        if (hiderRoomSpawns != null)
        {
            for (int i = 0; i < hiderRoomSpawns.Length; i++)
            {
                if (hiderRoomSpawns[i] != null)
                    result.Add(hiderRoomSpawns[i]);
            }
        }

        if (result.Count > 0) return result;

        HiderSpawnPoint[] points = FindObjectsByType<HiderSpawnPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] != null)
                result.Add(points[i].transform);
        }
        return result;
    }

    [Server]
    private void ScatterHidersInCircle(List<RoomPlayer> hiders)
    {
        int count = hiders.Count;
        Vector3 center = hiderSpawnCenter != null ? hiderSpawnCenter.position : Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * hiderSpawnRadius;
            TeleportHider(hiders[i], center + offset);
        }
    }

    [Server]
    private static void TeleportHider(RoomPlayer hider, Vector3 position)
    {
        if (hider == null) return;

        NetworkTransformBase nt = hider.GetComponent<NetworkTransformBase>();
        if (nt != null)
            nt.ServerTeleport(position, hider.transform.rotation);
        else
            hider.transform.position = position;

        Rigidbody2D rb = hider.GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.velocity = Vector2.zero;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ================================================================
    // Wave 3：变身定时器 / 心跳脉冲（服务端）
    // ================================================================

    /// <summary>
    /// Playing 期间每 TransformInterval 对存活躲藏者随机变身 → Invisible + 无敌窗口；
    /// InvulnerableDuration 后若仍 Invisible 则改回 Disguised（不碰 Captured / 新 Ghost）。
    /// Prep / Ended / Waiting 不跑。
    /// </summary>
    [Server]
    private void TickTransform()
    {
        if (phase != GamePhase.Playing) return;

        // 隐身窗口结束：仅恢复「仍存活且仍 Invisible」
        if (pendingInvisibleReveal && NetworkTime.time >= invisibleRevealTime)
        {
            pendingInvisibleReveal = false;
            foreach (RoomPlayer hider in GetAllRoomPlayers())
            {
                if (hider.role != PlayerRole.Hider) continue;
                if (hider.hiderState != HiderState.Invisible) continue;
                hider.hiderState = HiderState.Disguised;
            }
        }

        // 可能因卡顿追上多拍；每拍独立变身 + 刷新隐身窗口
        while (NetworkTime.time >= nextTransformTime)
        {
            ServerApplyHiderTransformWave();
            nextTransformTime += GameConstants.TransformInterval;
        }
    }

    /// <summary>
    /// 对每个非 Captured 躲藏者：随机新 disguiseItemId → Invisible，
    /// 广播 OnHiderTransformed；Ghost 会被拉回伪装/隐身（契约：鬼魂直到下次随机变化）。
    /// </summary>
    [Server]
    private void ServerApplyHiderTransformWave()
    {
        double invulnerableUntil = NetworkTime.time + GameConstants.InvulnerableDuration;
        bool any = false;

        foreach (RoomPlayer hider in GetAllRoomPlayers())
        {
            if (hider.role != PlayerRole.Hider) continue;
            if (hider.hiderState == HiderState.Captured) continue;

            int newItemId = PickRandomDisguiseItemId(hider.disguiseItemId);

            // 1) 先更新 State
            hider.disguiseItemId = newItemId;
            hider.hiderState = HiderState.Invisible;

            // 2) 再触发 Event（全员）
            RpcHiderTransformed(new TransformInfo
            {
                hiderNetId = hider.netId,
                newItemId = newItemId,
                invulnerableUntil = invulnerableUntil,
            });
            any = true;
        }

        if (any)
        {
            invisibleRevealTime = invulnerableUntil;
            pendingInvisibleReveal = true;
        }
    }

    /// <summary>
    /// 服务端随机伪装 itemId（选角/Prep/练习复活/变身波共用）。
    /// 从 ItemTable 全表抽取；表空则用占位 id 0..FallbackQueueLength-1。尽量避开当前伪装。
    /// </summary>
    [Server]
    private int PickRandomDisguiseItemId(int currentItemId)
    {
        int count = (itemTable != null && itemTable.Count > 0)
            ? itemTable.Count
            : FallbackQueueLength;

        if (count <= 0) return 0;
        if (count == 1) return 0;

        int picked = UnityEngine.Random.Range(0, count);
        // 多于一件时尽量换新外观
        if (picked == currentItemId)
            picked = (picked + 1 + UnityEngine.Random.Range(0, count - 1)) % count;
        return picked;
    }

    /// <summary>
    /// Playing（及可选 Waiting 练习大厅）按 HeartbeatInterval 对每个 Seeker 广播 OnHeartbeatPulse。
    /// 无额外权威判定；Prep / Ended 不发。
    /// </summary>
    [Server]
    private void TickHeartbeat()
    {
        bool inPlaying = phase == GamePhase.Playing;
        bool inPractice = HeartbeatInPracticeLobby && phase == GamePhase.Waiting;
        if (!inPlaying && !inPractice) return;

        // Waiting 首次进入时 nextHeartbeatTime 可能仍为 0（未 BeginPlaying）：从现在起拍
        if (inPractice && nextHeartbeatTime <= 0)
            nextHeartbeatTime = NetworkTime.time;

        while (NetworkTime.time >= nextHeartbeatTime)
        {
            heartbeatBeatIndex++;
            double serverTime = NetworkTime.time;
            foreach (RoomPlayer seeker in GetAllRoomPlayers())
            {
                if (seeker.role != PlayerRole.Seeker) continue;

                RpcHeartbeatPulse(new HeartbeatPulse
                {
                    seekerNetId = seeker.netId,
                    center = seeker.transform.position,
                    // 跳动范围与探测圈一致（HeartbeatRadius 须等于 InvestigateRange）
                    radius = GameConstants.HeartbeatRadius,
                    beatIndex = heartbeatBeatIndex,
                    serverTime = serverTime,
                });
            }

            nextHeartbeatTime += GameConstants.HeartbeatInterval;
        }
    }

    // ================================================================
    // Wave 2：放置 / 调查 / 劈砍工具
    // ================================================================

    [Server]
    private void ServerFillHiderItemQueue(RoomPlayer rp)
    {
        rp.itemQueue.Clear();
        if (itemTable == null || itemTable.Count == 0)
        {
            // ItemTable 为空：合成 itemId，外观一律走占位 prefab
            for (int i = 0; i < FallbackQueueLength; i++)
                rp.itemQueue.Add(i);
            return;
        }

        // 配额：0–1 大 / 2–4 中 / 1–2 小（库存不足时取该桶全部）
        List<int> large = CollectItemIdsBySize(ItemSize.Large);
        List<int> middle = CollectItemIdsBySize(ItemSize.Middle);
        List<int> small = CollectItemIdsBySize(ItemSize.Small);

        List<int> picked = new List<int>(7);
        PickRandomWithoutReplacement(large, UnityEngine.Random.Range(0, 2), picked);
        PickRandomWithoutReplacement(middle, UnityEngine.Random.Range(2, 5), picked);
        PickRandomWithoutReplacement(small, UnityEngine.Random.Range(1, 3), picked);

        ShuffleInPlace(picked);
        for (int i = 0; i < picked.Count; i++)
            rp.itemQueue.Add(picked[i]);
    }

    List<int> CollectItemIdsBySize(ItemSize size)
    {
        List<int> ids = new List<int>();
        for (int i = 0; i < itemTable.Count; i++)
        {
            ItemTable.Entry entry = itemTable.Get(i);
            if (entry != null && entry.size == size)
                ids.Add(i);
        }
        return ids;
    }

    static void PickRandomWithoutReplacement(List<int> pool, int count, List<int> dest)
    {
        if (pool == null || pool.Count == 0 || count <= 0) return;
        int take = Mathf.Min(count, pool.Count);
        // Fisher–Yates 前 take 次交换，再取前 take
        for (int i = 0; i < take; i++)
        {
            int j = UnityEngine.Random.Range(i, pool.Count);
            int tmp = pool[i];
            pool[i] = pool[j];
            pool[j] = tmp;
            dest.Add(pool[i]);
        }
    }

    static void ShuffleInPlace(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    [Server]
    private void ServerPracticeRespawnHider(RoomPlayer hider)
    {
        hider.hiderState = HiderState.Disguised;
        if (hider.itemQueue.Count == 0)
            ServerFillHiderItemQueue(hider);
        hider.disguiseItemId = PickRandomDisguiseItemId(hider.disguiseItemId);

        RespawnInfo info = new RespawnInfo
        {
            hiderNetId = hider.netId,
            position = hider.transform.position,
            itemId = hider.disguiseItemId,
        };
        RpcHiderRespawned(info);
    }

    /// <summary>
    /// 占位空间检测：与已有 InvestigableObject 圆心距离过近 → NoSpace。
    /// 若场景存在 Collider2D，额外用 Physics2D.OverlapCircle 排除固体（忽略发起者自身与 Trigger）。
    /// 无碰撞关卡时 Overlap 常为空，距离检测仍生效。
    /// </summary>
    [Server]
    private bool HasPlaceSpace(Vector2 position, RoomPlayer placer)
    {
        foreach (InvestigableObject obj in FindObjectsOfType<InvestigableObject>())
        {
            if (obj == null) continue;
            if (Vector2.Distance(position, (Vector2)obj.transform.position) < PlaceOccupyRadius * 2f)
                return false;
        }

        Collider2D[] hits = Physics2D.OverlapCircleAll(position, PlaceOccupyRadius);
        foreach (Collider2D col in hits)
        {
            if (col == null || col.isTrigger) continue;
            if (placer != null && col.transform.IsChildOf(placer.transform)) continue;
            if (col.GetComponentInParent<RoomPlayer>() != null) continue;
            // 固体世界碰撞视为占位冲突
            return false;
        }

        return true;
    }

    private GameObject ResolveItemPrefab(int itemId)
    {
        if (itemTable != null && itemTable.IsValid(itemId))
        {
            ItemTable.Entry entry = itemTable.Get(itemId);
            if (entry != null && entry.prefab != null)
                return entry.prefab;
        }
        return investigablePlaceholderPrefab;
    }

    private struct InvestigableTarget
    {
        public uint netId;
        public Vector2 position;
        public RoomPlayer linkedHider; // 非 null 且 Disguised/Invisible 时可能命中
    }

    /// <summary>
    /// 在探测圈内、鼠标判定半径内取最近可调查目标：
    /// 1) 场景/放置的 InvestigableObject（需 NetworkIdentity）
    /// 2) 伪装中的躲藏者本体（Disguised / Invisible；Ghost/Captured 排除）
    /// </summary>
    [Server]
    private bool TryFindInvestigableUnderCursor(
        Vector2 seekerPos,
        Vector2 mousePos,
        float seekerRange,
        float cursorPickRadius,
        out InvestigableTarget best)
    {
        best = default;
        float bestDist = float.MaxValue;
        bool found = false;

        foreach (InvestigableObject obj in FindObjectsOfType<InvestigableObject>())
        {
            if (obj == null || obj.netId == GameConstants.InvalidNetId) continue;
            Vector2 pos = obj.transform.position;
            if (Vector2.Distance(seekerPos, pos) > seekerRange) continue;

            float dMouse = Vector2.Distance(mousePos, pos);
            if (dMouse > cursorPickRadius || dMouse >= bestDist) continue;

            RoomPlayer linked = null;
            if (obj.LinksToHider &&
                NetworkServer.spawned.TryGetValue(obj.HiderNetId, out NetworkIdentity identity))
            {
                linked = identity.GetComponent<RoomPlayer>();
                if (linked != null &&
                    (linked.hiderState == HiderState.Ghost || linked.hiderState == HiderState.Captured))
                {
                    linked = null; // 关联已失效，仍可调查为诱饵
                }
            }

            best = new InvestigableTarget
            {
                netId = obj.netId,
                position = pos,
                linkedHider = linked,
            };
            bestDist = dMouse;
            found = true;
        }

        foreach (RoomPlayer hider in GetAllRoomPlayers())
        {
            if (hider.role != PlayerRole.Hider) continue;
            if (hider.hiderState != HiderState.Disguised && hider.hiderState != HiderState.Invisible)
                continue;

            Vector2 pos = hider.transform.position;
            if (Vector2.Distance(seekerPos, pos) > seekerRange) continue;

            float dMouse = Vector2.Distance(mousePos, pos);
            if (dMouse > cursorPickRadius || dMouse >= bestDist) continue;

            best = new InvestigableTarget
            {
                netId = hider.netId,
                position = pos,
                linkedHider = hider,
            };
            bestDist = dMouse;
            found = true;
        }

        return found;
    }

    [Server]
    private RoomPlayer FindNearestGhostHider(Vector2 origin, float range)
    {
        return FindNearestGhostHiderDual(origin, range, origin, 0f);
    }

    /// <summary>
    /// 两圆并集内找 Ghost：对每个目标取 min(距A, 距B)（仅计入各自半径内），再取全局最小。
    /// </summary>
    [Server]
    private RoomPlayer FindNearestGhostHiderDual(
        Vector2 originA, float rangeA,
        Vector2 originB, float rangeB)
    {
        RoomPlayer best = null;
        float bestDist = float.MaxValue;
        foreach (RoomPlayer hider in GetAllRoomPlayers())
        {
            if (hider.role != PlayerRole.Hider || hider.hiderState != HiderState.Ghost) continue;
            Vector2 pos = hider.transform.position;
            float dA = Vector2.Distance(originA, pos);
            float dB = Vector2.Distance(originB, pos);

            float score = float.MaxValue;
            if (dA <= rangeA) score = Mathf.Min(score, dA);
            if (dB <= rangeB) score = Mathf.Min(score, dB);
            if (score >= bestDist) continue;

            best = hider;
            bestDist = score;
        }
        return best;
    }

    [Server]
    static Vector2 ClampMouseSlashPosition(Vector2 seekerPos, Vector2 effectWorldPosition)
    {
        Vector2 delta = effectWorldPosition - seekerPos;
        float max = GameConstants.MouseSlashMaxDistance;
        if (delta.sqrMagnitude <= max * max)
            return effectWorldPosition;
        return seekerPos + delta.normalized * max;
    }

    private void EnsureItemResources()
    {
        if (itemTable == null)
            itemTable = Resources.Load<ItemTable>("ItemTable");

        if (investigablePlaceholderPrefab == null)
            investigablePlaceholderPrefab = Resources.Load<GameObject>(PlaceholderPrefabPath);

        if (investigablePlaceholderPrefab == null)
        {
            Debug.LogWarning(
                "[NetworkGameState] Resources/InvestigableItemPlaceholder 未找到。" +
                "放置将失败，直到提供 ItemTable prefab 或占位预制体。");
        }
    }

    private void RegisterInvestigablePrefabs()
    {
        if (investigablePlaceholderPrefab != null)
            NetworkClient.RegisterPrefab(investigablePlaceholderPrefab);

        if (itemTable == null) return;
        for (int i = 0; i < itemTable.Count; i++)
        {
            ItemTable.Entry entry = itemTable.Get(i);
            if (entry?.prefab != null)
                NetworkClient.RegisterPrefab(entry.prefab);
        }
    }

    // ================================================================
    // 事件投递（ClientRpc 全员 / TargetRpc 仅发起者）
    // ================================================================

    [ClientRpc]
    private void RpcPhaseChanged(GamePhase newPhase, float duration)
    {
        if (newPhase == GamePhase.Prep)
            ActivateMatchMap();
        else if (newPhase == GamePhase.Waiting)
            ActivateLobbyMap();
        OnPhaseChanged?.Invoke(newPhase, duration);
    }

    [ClientRpc]
    private void RpcRoleSlotsChanged(RoleSlots slots) => OnRoleSlotsChanged?.Invoke(slots);

    [TargetRpc]
    private void TargetRoleSlotsChanged(NetworkConnection target, RoleSlots slots) => OnRoleSlotsChanged?.Invoke(slots);

    [ClientRpc]
    private void RpcGameEnded(MatchResult matchResult) => OnGameEnded?.Invoke(matchResult);

    [ClientRpc]
    private void RpcPlaceResultAll(PlaceItemResult placeResult) => OnPlaceResult?.Invoke(placeResult);

    [TargetRpc]
    private void TargetPlaceResult(NetworkConnection target, PlaceItemResult placeResult) => OnPlaceResult?.Invoke(placeResult);

    [TargetRpc]
    private void TargetCommandRejected(NetworkConnection target, CommandRejected rejected) => OnCommandRejected?.Invoke(rejected);

    [ClientRpc]
    private void RpcInvestigated(InvestigateInfo info) => OnInvestigated?.Invoke(info);

    [ClientRpc]
    private void RpcSlashed(SlashInfo info) => OnSlashed?.Invoke(info);

    [ClientRpc]
    private void RpcCaptured(CaptureInfo info) => OnCaptured?.Invoke(info);

    [ClientRpc]
    private void RpcHiderRespawned(RespawnInfo info) => OnHiderRespawned?.Invoke(info);

    [ClientRpc]
    private void RpcHiderTransformed(TransformInfo info) => OnHiderTransformed?.Invoke(info);

    [ClientRpc]
    private void RpcHeartbeatPulse(HeartbeatPulse pulse) => OnHeartbeatPulse?.Invoke(pulse);

    [Server]
    private void SendPlaceResult(NetworkConnectionToClient sender, PlaceItemResult placeResult)
    {
        if (placeResult.success)
        {
            RpcPlaceResultAll(placeResult);
        }
        else if (sender != null)
        {
            TargetPlaceResult(sender, placeResult);
        }
    }

    [Server]
    private void RejectCommand(NetworkConnectionToClient sender, GameCommandType command, RejectReason reason)
    {
        if (sender == null) return;
        TargetCommandRejected(sender, new CommandRejected { command = command, reason = reason });
    }

    // ================================================================
    // 内部工具
    // ================================================================

    private RoleSlots ComputeSlots()
    {
        List<RoomPlayer> all = GetAllRoomPlayers();
        ComputeRoleMax(all.Count, out int sMax, out int hMax);

        if (isServer)
        {
            seekerMax = sMax;
            hiderMax = hMax;
        }

        return new RoleSlots
        {
            seekerCount = all.Count(p => p.role == PlayerRole.Seeker),
            seekerMax = sMax,
            hiderCount = all.Count(p => p.role == PlayerRole.Hider),
            hiderMax = hMax,
        };
    }

    static void ComputeRoleMax(int totalPlayers, out int seekerMaxOut, out int hiderMaxOut)
    {
        RoleSlots.ComputeRoleMax(totalPlayers, out seekerMaxOut, out hiderMaxOut);
    }

    [Server]
    static void ServerClearPlayerRole(RoomPlayer rp)
    {
        if (rp == null || rp.role == PlayerRole.None) return;

        rp.role = PlayerRole.None;
        rp.isReady = false;
        rp.itemQueue.Clear();
        rp.hiderState = HiderState.Disguised;
        rp.disguiseItemId = GameConstants.InvalidItemId;
    }

    private static RoomPlayer GetRoomPlayer(NetworkConnectionToClient sender)
    {
        if (sender == null || sender.identity == null) return null;
        return sender.identity.GetComponent<RoomPlayer>();
    }

    /// <summary>场景内当前所有 RoomPlayer（服务端=权威列表；客户端=该客户端可观察到的同步副本）。</summary>
    private static List<RoomPlayer> GetAllRoomPlayers()
    {
        return FindObjectsOfType<RoomPlayer>().OrderBy(p => p.netId).ToList();
    }
}
