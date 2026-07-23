using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("左侧 - 加入游戏")]
    public Button joinGameBtn;
    public GameObject functionBar;
    public GameObject roomListScrollView;
    
    [Header("右侧 - 创建游戏")]
    public Button createGameBtn;
    public GameObject createPanel;
    public GameObject rightPanel;  // ← 添加 RightPanel 引用
    
    [Header("状态")]
    public TextMeshProUGUI statusText;
    
    private bool isJoinModeActive = false;
    private bool isCreateModeActive = false;
    
    void Start()
    {
        joinGameBtn.onClick.AddListener(ToggleJoinMode);
        createGameBtn.onClick.AddListener(ToggleCreateMode);
        
        SetAllPanelsActive(false);
        
        // 始终显示两个按钮
        ShowBothButtons();
        
        statusText.text = "选择「加入游戏」或「创建游戏」";
    }
    
    // ==================== 始终显示两个按钮 ====================
    public void ShowBothButtons()
    {
        joinGameBtn.gameObject.SetActive(true);
        createGameBtn.gameObject.SetActive(true);
    }
    
    // ==================== 切换加入模式 ====================
    void ToggleJoinMode()
    {
        if (isJoinModeActive)
        {
            SetJoinModeActive(false);
            isJoinModeActive = false;
            ShowBothButtons();
            statusText.text = "已关闭房间列表";
            return;
        }
        
        isJoinModeActive = true;
        isCreateModeActive = false;
        
        SetJoinModeActive(true);
        SetCreateModeActive(false);
        
        // 两个按钮都显示
        ShowBothButtons();
        
        statusText.text = "📋 选择房间加入，或点击右侧「创建游戏」";
        
        RoomListController roomList = FindObjectOfType<RoomListController>();
        if (roomList != null)
        {
            roomList.RefreshRoomList();
        }
    }
    
    // ==================== 切换创建模式 ====================
    void ToggleCreateMode()
    {
        if (isCreateModeActive)
        {
            SetCreateModeActive(false);
            isCreateModeActive = false;
            ShowBothButtons();
            statusText.text = "已关闭创建面板";
            return;
        }
        
        isCreateModeActive = true;
        isJoinModeActive = false;
        
        SetCreateModeActive(true);
        SetJoinModeActive(false);
        
        // 两个按钮都显示
        ShowBothButtons();
        
        statusText.text = "🏠 填写信息创建新房间";
    }
    
    // ==================== 设置加入模式 ====================
    public void SetJoinModeActive(bool active)
    {
        isJoinModeActive = active;
        functionBar.SetActive(active);
        roomListScrollView.SetActive(active);
        
        // ===== RoomListStatusText 跟随房间列表 =====
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
    
    // ==================== 设置创建模式 ====================
    public void SetCreateModeActive(bool active)
    {
        isCreateModeActive = active;
        createPanel.SetActive(active);
        
        // ===== RightPanel 显示/隐藏 =====
        if (rightPanel != null)
        {
            rightPanel.SetActive(active);
        }
    }
    
    // ==================== 更新状态文字 ====================
    public void UpdateStatusText(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }
    
    // ==================== 全部隐藏 ====================
    void SetAllPanelsActive(bool active)
    {
        functionBar.SetActive(active);
        roomListScrollView.SetActive(active);
        createPanel.SetActive(active);
    }
}