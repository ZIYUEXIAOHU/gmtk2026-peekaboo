using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class MainMenuController : MonoBehaviour
{
    [Header("左侧 - 加入游戏")]
    public Button joinGameBtn;
    public GameObject functionBar;
    public GameObject roomListScrollView;
    
    [Header("右侧 - 创建游戏")]
    public Button createGameBtn;
    public GameObject createPanel;
    public GameObject rightPanel;
    
    [Header("状态")]
    public TextMeshProUGUI statusText;
    
    private bool isJoinModeActive = true;
    private bool isCreateModeActive = false;
    
    // ===== 房间连接状态（来自契约 GameEnums.cs）=====
    private RoomConnectionState currentConnectionState = RoomConnectionState.Disconnected;
    
    void Start()
    {
        joinGameBtn.onClick.AddListener(ToggleJoinMode);
        createGameBtn.onClick.AddListener(ToggleCreateMode);
        
        // ===== 订阅房间事件（契约）=====
        SubscribeRoomEvents();
        
        // ===== 默认显示 LeftPanel =====
        SetJoinModeActive(true);
        SetCreateModeActive(false);
        
        ShowBothButtons();
        
        if (statusText != null)
            statusText.text = "📋 选择房间加入，或点击右侧「创建游戏」";
    }
    
    // ==================== 订阅契约事件 ====================
    void SubscribeRoomEvents()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnConnectionStateChanged += OnConnectionStateChanged;
                GameContract.RoomEvents.OnRoomListUpdated += OnRoomListUpdated;
                GameContract.RoomEvents.OnRoomError += OnRoomError;
                Debug.Log("✅ MainMenuController 订阅契约事件成功");
            }
            else
            {
                Debug.Log("⏳ 等待契约绑定...");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅房间事件失败（等待契约实现）：{e.Message}");
        }
    }
    
    // ==================== 契约事件回调 ====================
    void OnConnectionStateChanged(RoomConnectionState state)
    {
        currentConnectionState = state;
        
        string statusMsg = state switch
        {
            RoomConnectionState.Disconnected => "📋 已断开连接",
            RoomConnectionState.Connecting => "⏳ 正在连接...",
            RoomConnectionState.InRoom => "✅ 已加入房间",
            RoomConnectionState.Failed => "❌ 连接失败，请重试",
            _ => "📋 已断开连接"
        };
        
        if (statusText != null)
            statusText.text = statusMsg;
        
        if (state == RoomConnectionState.Disconnected || state == RoomConnectionState.Failed)
        {
            ShowBothButtons();
        }
        else if (state == RoomConnectionState.InRoom)
        {
            // 隐藏两个主按钮，显示房间界面
            joinGameBtn.gameObject.SetActive(false);
            createGameBtn.gameObject.SetActive(false);
        }
    }
    
    void OnRoomListUpdated(IReadOnlyList<RoomInfo> roomList)
    {
        RoomListController controller = FindObjectOfType<RoomListController>();
        if (controller != null)
        {
            controller.UpdateRoomList(roomList);
        }
    }
    
    void OnRoomError(RoomError error)
    {
        string errorMsg = error.reason switch
        {
            RoomErrorReason.Timeout => "⏰ 操作超时",
            RoomErrorReason.RoomNotFound => "🔍 房间不存在",
            RoomErrorReason.RoomFull => "👥 房间已满",
            RoomErrorReason.ConnectionFailed => "🔌 网络连接失败",
            RoomErrorReason.AlreadyInRoom => "⚠️ 已在房间中",
            _ => $"❌ 操作失败：{error.message}"
        };
        
        if (statusText != null)
            statusText.text = errorMsg;
        
        Debug.LogWarning($"[RoomError] {error.op}: {errorMsg}");
    }
    
    void OnDestroy()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnConnectionStateChanged -= OnConnectionStateChanged;
                GameContract.RoomEvents.OnRoomListUpdated -= OnRoomListUpdated;
                GameContract.RoomEvents.OnRoomError -= OnRoomError;
            }
        }
        catch { }
    }
    
    // ==================== UI 控制 ====================
    public void ShowBothButtons()
    {
        joinGameBtn.gameObject.SetActive(true);
        createGameBtn.gameObject.SetActive(true);
    }
    
    void ToggleJoinMode()
    {
        if (isJoinModeActive)
        {
            return;
        }
        
        isJoinModeActive = true;
        isCreateModeActive = false;
        
        SetJoinModeActive(true);
        SetCreateModeActive(false);
        
        ShowBothButtons();
        
        if (statusText != null)
            statusText.text = "📋 选择房间加入，或点击右侧「创建游戏」";
        
        // ===== 使用契约刷新房间列表 =====
        if (GameContract.IsRoomBound)
        {
            GameContract.RoomCommands.RefreshRoomList();
        }
        else
        {
            // 兼容旧版
            RoomListController roomList = FindObjectOfType<RoomListController>();
            if (roomList != null)
            {
                roomList.RefreshRoomList();
            }
        }
    }
    
    void ToggleCreateMode()
    {
        if (isCreateModeActive)
        {
            return;
        }
        
        isCreateModeActive = true;
        isJoinModeActive = false;
        
        SetCreateModeActive(true);
        SetJoinModeActive(false);
        
        ShowBothButtons();
        
        if (statusText != null)
            statusText.text = "🏠 填写信息创建新房间";
    }
    
    public void SetJoinModeActive(bool active)
    {
        isJoinModeActive = active;
        functionBar.SetActive(active);
        roomListScrollView.SetActive(active);
        
        RoomListController controller = FindObjectOfType<RoomListController>();
        if (controller != null && controller.listStatusText != null)
        {
            controller.listStatusText.gameObject.SetActive(active);
        }
        
        if (active && controller != null)
        {
            controller.ApplyFiltersAndSort();
        }
    }
    
    public void SetCreateModeActive(bool active)
    {
        isCreateModeActive = active;
        createPanel.SetActive(active);
        
        if (rightPanel != null)
        {
            rightPanel.SetActive(active);
        }
    }
    
    public void UpdateStatusText(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }
    
    void SetAllPanelsActive(bool active)
    {
        functionBar.SetActive(active);
        roomListScrollView.SetActive(active);
        createPanel.SetActive(active);
    }
}