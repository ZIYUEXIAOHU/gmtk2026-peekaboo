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

    [SyncVar]
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

    // ---- IPlayerStateReadonly ----
    public uint NetId => netId;
    public string PlayerName => playerName;
    public PlayerRole Role => role;
    public HiderState HiderState => hiderState;
    public int DisguiseItemId => disguiseItemId;
    public IReadOnlyList<int> ItemQueue => itemQueue;

    // 用于存储玩家列表更新回调
    private static System.Action<int> onPlayerListUpdated;

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
