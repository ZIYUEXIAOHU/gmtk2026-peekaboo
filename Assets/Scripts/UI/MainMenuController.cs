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
    public GameObject rightPanel;
    
    [Header("状态")]
    public TextMeshProUGUI statusText;
    
    private bool isJoinModeActive = true;   // ← 默认 true
    private bool isCreateModeActive = false;
    
    void Start()
    {
        joinGameBtn.onClick.AddListener(ToggleJoinMode);
        createGameBtn.onClick.AddListener(ToggleCreateMode);
        
        // ===== 默认显示 LeftPanel =====
        SetJoinModeActive(true);
        SetCreateModeActive(false);
        
        ShowBothButtons();
        
        statusText.text = "📋 选择房间加入，或点击右侧「创建游戏」";
    }
    
    public void ShowBothButtons()
    {
        joinGameBtn.gameObject.SetActive(true);
        createGameBtn.gameObject.SetActive(true);
    }
    
    void ToggleJoinMode()
    {
        if (isJoinModeActive)
        {
            // 如果已显示，不关闭（保持显示）
            return;
        }
        
        isJoinModeActive = true;
        isCreateModeActive = false;
        
        SetJoinModeActive(true);
        SetCreateModeActive(false);
        
        ShowBothButtons();
        
        statusText.text = "📋 选择房间加入，或点击右侧「创建游戏」";
        
        RoomListController roomList = FindObjectOfType<RoomListController>();
        if (roomList != null)
        {
            roomList.RefreshRoomList();
        }
    }
    
    void ToggleCreateMode()
    {
        if (isCreateModeActive)
        {
            // 如果已显示，不关闭（保持显示）
            return;
        }
        
        isCreateModeActive = true;
        isJoinModeActive = false;
        
        SetCreateModeActive(true);
        SetJoinModeActive(false);
        
        ShowBothButtons();
        
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