using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 每个连接对应的玩家对象（房间与对局共用）。
/// 房间发现/连接部分由并行 agent 负责（NetworkRoomService）；本类新增的
/// Role / HiderState / DisguiseItemId / ItemQueue / IsHost 字段服务于契约
/// IPlayerStateReadonly，由 NetworkGameState 统一读取/裁定。
/// </summary>
public class RoomPlayer : NetworkBehaviour, IPlayerStateReadonly
{
    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string playerName = "玩家";

    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReady = false;

    [SyncVar]
    public int connectionId = -1;

    // ==================== 契约：身份与对局状态（程序 1 权威） ====================

    [SyncVar(hook = nameof(OnRoleChanged))]
    public PlayerRole role = PlayerRole.None;

    [SyncVar]
    public HiderState hiderState = HiderState.Disguised;

    [SyncVar]
    public int disguiseItemId = GameConstants.InvalidItemId;

    /// <summary>是否为房主（房主判定：与 NetworkServer.localConnection 是否为同一连接，见 CustomNetworkManager.OnServerAddPlayer）。
    /// 注意：不能叫 isHost —— 与 Mirror NetworkBehaviour.isHost（表示本机是否 host 模式）同名会被隐藏。</summary>
    [SyncVar]
    public bool isRoomHost = false;

    /// <summary>物品栏剩余队列，按顺序放置。
    /// 由 NetworkGameState 在选 Hider / 进入 Prep 时填充，PlaceItem 时弹出队首。</summary>
    public readonly SyncList<int> itemQueue = new SyncList<int>();

    [Header("角色外观（按 Role 显隐，全客户端）")]
    public GameObject visualHider;
    public GameObject visualSeeker;

    // ---- IPlayerStateReadonly ----
    public uint NetId => netId;
    public string PlayerName => playerName;
    public PlayerRole Role => role;
    public HiderState HiderState => hiderState;
    public int DisguiseItemId => disguiseItemId;
    public IReadOnlyList<int> ItemQueue => itemQueue;

    // 用于存储玩家列表更新回调
    private static System.Action<int> onPlayerListUpdated;

    HiderController hiderController;
    SeekerController seekerController;

    public override void OnStartClient()
    {
        base.OnStartClient();
        CacheControllers();
        CacheVisuals();
        ApplyRoleVisuals(role);
        ApplyRoleControllers(role);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        CacheVisuals();
        // 服务器 Spawn 前/后都需隐藏未选身份外观（Visual_Seeker 保持 Active）
        ApplyRoleVisuals(role);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        CacheControllers();
        CacheVisuals();
        ApplyRoleVisuals(role);
        ApplyRoleControllers(role);
    }

    void Awake()
    {
        CacheVisuals();
        // 尽早关掉外观，避免 Visual_Seeker 默认激活时闪一帧；
        // 但必须保持 Visual_Seeker 的 GameObject 激活，否则 NetworkAnimator 不会 Initialize，Spawn 会 OnSerialize NRE。
        ApplyRoleVisuals(role);
    }

    void CacheControllers()
    {
        if (hiderController == null)
            hiderController = GetComponent<HiderController>();
        if (seekerController == null)
            seekerController = GetComponent<SeekerController>();
    }

    void CacheVisuals()
    {
        if (visualHider == null)
        {
            Transform t = transform.Find("Visual_Hider");
            if (t != null) visualHider = t.gameObject;
        }
        if (visualSeeker == null)
        {
            Transform t = transform.Find("Visual_Seeker");
            if (t != null) visualSeeker = t.gameObject;
        }
    }

    void OnRoleChanged(PlayerRole oldRole, PlayerRole newRole)
    {
        Debug.Log($"玩家 {playerName} 身份: {oldRole} -> {newRole}");
        ApplyRoleVisuals(newRole);
        ApplyRoleControllers(newRole);
    }

    /// <summary>
    /// 同一 NetworkIdentity 上按身份开关移动控制器（不换预制体）。
    /// 仅本地玩家启用输入；远端靠 NetworkTransform 同步。
    /// </summary>
    void ApplyRoleControllers(PlayerRole currentRole)
    {
        CacheControllers();

        bool local = isLocalPlayer;
        if (hiderController != null)
            hiderController.enabled = local && currentRole == PlayerRole.Hider;
        if (seekerController != null)
            seekerController.enabled = local && currentRole == PlayerRole.Seeker;
    }

    /// <summary>
    /// 按身份切换 Hider/Seeker 外观（所有客户端）。
    /// Visual_Seeker 挂有 NetworkAnimator：不能 SetActive(false)，否则 Awake/Initialize 不跑，
    /// Spawn 时 OnSerialize 会因 parameters==null 失败并连带 EndOfStreamException。
    /// </summary>
    void ApplyRoleVisuals(PlayerRole currentRole)
    {
        CacheVisuals();

        bool showHider = currentRole == PlayerRole.Hider;
        bool showSeeker = currentRole == PlayerRole.Seeker;

        if (visualHider != null)
            visualHider.SetActive(showHider);

        if (visualSeeker != null)
        {
            if (!visualSeeker.activeSelf)
                visualSeeker.SetActive(true);
            SetChildVisualVisible(visualSeeker, showSeeker);
        }
    }

    static void SetChildVisualVisible(GameObject visual, bool visible)
    {
        SpriteRenderer[] renderers = visual.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = visible;

        Animator[] animators = visual.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].enabled = visible;
    }

    void OnPlayerNameChanged(string oldVal, string newVal)
    {
        Debug.Log($"玩家名称变更: {oldVal} -> {newVal}");
    }

    void OnReadyChanged(bool oldVal, bool newVal)
    {
        Debug.Log($"玩家 {playerName} 准备状态: {newVal}");

        LobbyRoomController lobby = FindObjectOfType<LobbyRoomController>();
        if (lobby != null)
            lobby.NotifyPlayerReadyChanged(this);
    }

    [Command]
    public void CmdToggleReady()
    {
        if (!isReady && role == PlayerRole.None)
            return;

        isReady = !isReady;

        CustomNetworkManager nm = FindObjectOfType<CustomNetworkManager>();
        if (nm != null)
        {
            nm.UpdateReadyStatus(connectionId, isReady);
        }
    }

    // ==================== 客户端接收更新（TargetRpc） ====================
    // UI 须改用 GameContract.RoomCommands / State 读人数；勿再 Find UI 控制器。
    [TargetRpc]
    public void TargetUpdatePlayerList(NetworkConnection target, int playerCount)
    {
        Debug.Log($"[RoomPlayer] 房间人数同步（兼容 Rpc）：{playerCount}；UI 应读 GameContract.State / RoomState。");
    }

    // ==================== 本地/UI 兼容辅助（契约路径应走 GameContract.Commands） ====================
    public void SetRole(PlayerRole newRole)
    {
        role = newRole;
        Debug.Log($"玩家 {playerName} 设置角色: {newRole}");
    }

    public void SetHiderState(HiderState newState)
    {
        hiderState = newState;
        Debug.Log($"玩家 {playerName} 躲藏者状态: {newState}");
    }

    public void SetDisguiseItemId(int itemId)
    {
        disguiseItemId = itemId;
        Debug.Log($"玩家 {playerName} 伪装物品ID: {itemId}");
    }
}
