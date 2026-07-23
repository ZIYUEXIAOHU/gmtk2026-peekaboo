using Mirror;
using UnityEngine;

public class RoomPlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string playerName = "玩家";
    
    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReady = false;
    
    [SyncVar]
    public int connectionId = -1;
    
    // 用于存储玩家列表更新回调
    private static System.Action<int> onPlayerListUpdated;
    
    void OnPlayerNameChanged(string oldVal, string newVal)
    {
        // 更新UI显示玩家名
    }
    
    void OnReadyChanged(bool oldVal, bool newVal)
    {
        // 更新UI显示准备状态
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
            // 更新玩家列表显示
            Debug.Log($"更新玩家列表，当前人数：{playerCount}");
        }
    }
}