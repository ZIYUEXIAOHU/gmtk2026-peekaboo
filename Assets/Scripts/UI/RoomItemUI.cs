using UnityEngine;
using UnityEngine.UI;
using TMPro;  // ← 添加 TextMeshPro 命名空间

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
    
    private RoomItemData roomData;
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
    
    public void SetRoomData(RoomItemData data, RoomListController controller)
    {
        roomData = data;
        parentController = controller;
        
        if (roomNameText != null)
            roomNameText.text = data.roomName;
        
        if (hostNameText != null)
            hostNameText.text = $"主机: {data.hostName}";
        
        if (playerCountText != null)
            playerCountText.text = $"{data.currentPlayers}/{data.maxPlayers}人";
        
        UpdateStatus(data.status);
    }
    
    public void UpdateStatus(RoomStatus status)
    {
        if (roomData != null)
            roomData.status = status;
        
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
        if (parentController != null && roomData != null)
        {
            parentController.JoinRoom(roomData);
        }
    }
}