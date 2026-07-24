using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class GameMenuController : MonoBehaviour
{
    [Header("UI")]
    public GameObject escMenu;
    public Button settingsButton;
    public Button resumeBtn;
    public Button settingsBtn;
    public Button quitBtn;
    
    [Header("设置面板")]
    public GameObject settingsPanel;
    public Button settingsBackBtn;
    
    private bool isMenuOpen = false;
    private bool isSettingsOpen = false;
    
    void Start()
    {
        // 绑定按钮
        if (settingsButton != null)
            settingsButton.onClick.AddListener(ToggleMenu);
        
        if (resumeBtn != null)
            resumeBtn.onClick.AddListener(CloseMenu);
        
        if (settingsBtn != null)
            settingsBtn.onClick.AddListener(OpenSettings);
        
        if (quitBtn != null)
            quitBtn.onClick.AddListener(QuitRoom);
        
        if (settingsBackBtn != null)
            settingsBackBtn.onClick.AddListener(CloseSettings);
        
        // 订阅契约事件
        SubscribeEvents();
        
        // 初始状态
        if (escMenu != null)
            escMenu.SetActive(false);
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }
    
    void Update()
    {
        // ESC 键
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 如果设置面板开着，关闭设置面板（回到 ESC 菜单）
            if (settingsPanel != null && settingsPanel.activeSelf)
            {
                CloseSettings();
                return;
            }
            // 否则切换 ESC 菜单
            ToggleMenu();
        }
    }
    
    // ==================== 订阅契约事件 ====================
    void SubscribeEvents()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnConnectionStateChanged += OnConnectionStateChanged;
                Debug.Log("✅ GameMenuController 订阅房间事件成功");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅房间事件失败：{e.Message}");
        }
    }
    
    // ==================== 契约事件回调 ====================
    void OnConnectionStateChanged(RoomConnectionState state)
    {
        Debug.Log($"📋 连接状态变化: {state}");
        
        if (state == RoomConnectionState.Disconnected)
        {
            // ===== 断开连接 → 切换回大厅场景 =====
            ReturnToLobby();
        }
    }
    
    // ==================== 返回大厅场景 ====================
    void ReturnToLobby()
    {
        Debug.Log("🏠 返回大厅场景");
        
        // 关闭所有菜单
        if (escMenu != null)
            escMenu.SetActive(false);
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
        isMenuOpen = false;
        isSettingsOpen = false;
        
        // ===== 切换场景到 LobbyScene =====
        SceneManager.LoadScene("LobbyScene");
    }
    
    void OnDestroy()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnConnectionStateChanged -= OnConnectionStateChanged;
            }
        }
        catch { }
    }
    
    // ==================== UI 控制 ====================
    void ToggleMenu()
    {
        // 如果设置面板开着，先关掉
        if (settingsPanel != null && settingsPanel.activeSelf)
        {
            settingsPanel.SetActive(false);
            isSettingsOpen = false;
        }
        
        isMenuOpen = !isMenuOpen;
        if (escMenu != null)
            escMenu.SetActive(isMenuOpen);
    }
    
    void CloseMenu()
    {
        isMenuOpen = false;
        if (escMenu != null)
            escMenu.SetActive(false);
    }
    
    void OpenSettings()
    {
        Debug.Log("打开设置面板");
        CloseMenu();
        isSettingsOpen = true;
        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }
    
    void CloseSettings()
    {
        Debug.Log("返回 ESC 菜单");
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
        isSettingsOpen = false;
        isMenuOpen = true;
        if (escMenu != null)
            escMenu.SetActive(true);
    }
    
    // ==================== 退出房间 ====================
    void QuitRoom()
    {
        Debug.Log("🚪 退出房间");
        
        CloseMenu();
        
        if (settingsPanel != null && settingsPanel.activeSelf)
            settingsPanel.SetActive(false);
        
        // ===== 契约调用：离开房间 =====
        if (GameContract.IsRoomBound)
        {
            GameContract.RoomCommands.LeaveRoom();
            Debug.Log("✅ 已调用 LeaveRoom()，等待断连事件...");
        }
        else
        {
            // 兼容模式：直接返回大厅
            Debug.Log("⚠️ 契约未绑定，直接返回大厅");
            ReturnToLobby();
        }
    }
}