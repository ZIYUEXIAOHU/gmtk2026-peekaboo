using Mirror;
using UnityEngine;
using System.Collections.Generic;

public class RoomPlayer : NetworkBehaviour, IPlayerStateReadonly
{
    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string playerName = "玩家";
    
    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReady = false;
    
    [SyncVar]
    public int connectionId = -1;
    
    // ===== 契约需要的字段 =====
    [SyncVar]
    private PlayerRole role = PlayerRole.None;
    
    [SyncVar]
    private HiderState hiderState = HiderState.Disguised;
    
    [SyncVar]
    private int disguiseItemId = GameConstants.InvalidItemId;
    
    [SyncVar]
    private bool isCaptured = false;
    
    // ===== 物品栏队列（暂未实现同步） =====
    private List<int> itemQueue = new List<int>();
    
    // ===== 实现 IPlayerStateReadonly =====
    public uint NetId => netId;
    public string PlayerName => playerName;
    public PlayerRole Role => role;
    public HiderState HiderState => hiderState;
    public int DisguiseItemId => disguiseItemId;
    public IReadOnlyList<int> ItemQueue => itemQueue.AsReadOnly();
    
    // 用于存储玩家列表更新回调
    private static System.Action<int> onPlayerListUpdated;
    
    void OnPlayerNameChanged(string oldVal, string newVal)
    {
        // 更新UI显示玩家名
        Debug.Log($"玩家名称变更: {oldVal} -> {newVal}");
    }
    
    void OnReadyChanged(bool oldVal, bool newVal)
    {
        // 更新UI显示准备状态
        Debug.Log($"玩家 {playerName} 准备状态: {newVal}");
    }
    
    [Command]
    public void CmdToggleReady()
    {
        isReady = !isReady;
        
        CustomNetworkManager nm = FindObjectOfType<CustomNetworkManager>();
        if (nm != null)
        {
            nm.UpdateReadyStatus(connectionId, isReady);
        }
    }
    
    // ==================== 客户端接收更新（TargetRpc） ====================
    [TargetRpc]
    public void TargetUpdatePlayerList(NetworkConnection target, int playerCount)
    {
        // 客户端更新UI
        RoomListController roomList = FindObjectOfType<RoomListController>();
        if (roomList != null)
        {
            Debug.Log($"更新玩家列表，当前人数：{playerCount}");
        }
    }
    
    // ==================== 契约方法 ====================
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
    
    public void SetCaptured(bool captured)
    {
        isCaptured = captured;
        if (captured)
        {
            hiderState = HiderState.Captured;
            Debug.Log($"玩家 {playerName} 已被捕获");
        }
    }
    
    // ==================== 物品栏操作 ====================
    public void AddItemToQueue(int itemId)
    {
        itemQueue.Add(itemId);
    }
    
    public int GetNextItem()
    {
        if (itemQueue.Count == 0) return GameConstants.InvalidItemId;
        int itemId = itemQueue[0];
        itemQueue.RemoveAt(0);
        return itemId;
    }
    
    public void ClearItemQueue()
    {
        itemQueue.Clear();
    }
}