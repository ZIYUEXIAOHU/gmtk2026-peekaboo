using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomItemUI : MonoBehaviour
{
    [Header("UI组件")]
    public TextMeshProUGUI roomNameText;     // TMP 文本
    public TextMeshProUGUI hostNameText;     // TMP 文本
    public TextMeshProUGUI playerCountText;  // TMP 文本
    public TextMeshProUGUI gameStatusText;   // TMP 文本
    public Button joinBtn;
    public TextMeshProUGUI joinBtnText;      // TMP 文本
    public Image statusIcon;
    
    // ===== 支持 RoomInfo（契约）和 RoomItemData（旧版）=====
    private RoomInfo? roomInfo;              // 契约中的 RoomInfo
    private RoomItemData roomData;           // 旧版数据
    private RoomListController parentController;
    
    private Color idleColor = new Color(0, 0.8f, 0.2f);
    private Color playingColor = new Color(1, 0.8f, 0);
    private Color settlingColor = new Color(1, 0.3f, 0.3f);
    
    void Start()
    {
        if (joinBtn != null)
        {
            joinBtn.onClick.AddListener(OnJoinClicked);
        }
    }
    
    // ===== 使用 RoomInfo（契约）=====
    public void SetRoomData(RoomInfo data, RoomListController controller)
    {
        roomInfo = data;
        roomData = null;
        parentController = controller;
        
        UpdateUI(data.roomName, data.hostName, data.currentPlayers, data.maxPlayers, data.status);
    }
    
    // ===== 使用 RoomItemData（旧版兼容）=====
    public void SetRoomData(RoomItemData data, RoomListController controller)
    {
        roomData = data;
        roomInfo = null;
        parentController = controller;
        
        UpdateUI(data.roomName, data.hostName, data.currentPlayers, data.maxPlayers, data.status);
    }
    
    private void UpdateUI(string roomName, string hostName, int currentPlayers, int maxPlayers, RoomStatus status)
    {
        if (roomNameText != null)
            roomNameText.text = roomName;
        
        if (hostNameText != null)
            hostNameText.text = $"主机: {hostName}";
        
        if (playerCountText != null)
            playerCountText.text = $"{currentPlayers}/{maxPlayers}人";
        
        UpdateStatus(status);
    }
    
    public void UpdateStatus(RoomStatus status)
    {
        // 更新数据
        if (roomData != null)
            roomData.status = status;
        if (roomInfo.HasValue)
        {
            RoomInfo updated = roomInfo.Value;
            updated.status = status;
            roomInfo = updated;
        }
        
        string statusText = "";
        Color statusColor = Color.white;
        
        switch (status)
        {
            case RoomStatus.Idle:
                statusText = "🟢 空闲中";
                statusColor = idleColor;
                if (joinBtnText != null)
                    joinBtnText.text = "加入";
                if (joinBtn != null)
                    joinBtn.interactable = true;
                break;
                
            case RoomStatus.Playing:
                statusText = "🟡 游戏中";
                statusColor = playingColor;
                if (joinBtnText != null)
                    joinBtnText.text = "观战";
                if (joinBtn != null)
                    joinBtn.interactable = true;
                break;
                
            case RoomStatus.Settling:
                statusText = "🔴 结算中";
                statusColor = settlingColor;
                if (joinBtnText != null)
                    joinBtnText.text = "观战";
                if (joinBtn != null)
                    joinBtn.interactable = true;
                break;
        }
        
        if (gameStatusText != null)
        {
            gameStatusText.text = statusText;
            gameStatusText.color = statusColor;
        }
        
        if (statusIcon != null)
        {
            statusIcon.color = statusColor;
        }
    }
    
    void OnJoinClicked()
    {
        if (parentController == null) return;
        
        // 优先使用 RoomInfo
        if (roomInfo.HasValue)
        {
            // 将 RoomInfo 转换为 RoomItemData 调用现有方法
            RoomItemData data = new RoomItemData
            {
                serverId = roomInfo.Value.serverId,
                roomName = roomInfo.Value.roomName,
                hostName = roomInfo.Value.hostName,
                currentPlayers = roomInfo.Value.currentPlayers,
                maxPlayers = roomInfo.Value.maxPlayers,
                status = roomInfo.Value.status,
                ping = roomInfo.Value.ping
            };
            parentController.JoinRoom(data);
        }
        else if (roomData != null)
        {
            parentController.JoinRoom(roomData);
        }
    }
}