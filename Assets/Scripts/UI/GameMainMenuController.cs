using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class GameMainMenuController : MonoBehaviour
{
    [Header("主菜单")]
    public GameObject mainMenuPanel;
    public Button startGameBtn;
    public Button settingsBtn;
    public Button quitGameBtn;
    
    [Header("加入游戏面板")]
    public GameObject joinPanel;
    public Button backToMainBtn;
    
    [Header("设置面板")]
    public GameObject settingsPanel;
    public Button settingsBackBtn;
    public Slider volumeSlider;
    
    [Header("状态")]
    public TextMeshProUGUI statusText;
    
    private RoomConnectionState currentConnectionState = RoomConnectionState.Disconnected;
    
    private void Start()
    {
        // ===== 主菜单按钮 =====
        startGameBtn.onClick.AddListener(OpenJoinPanel);
        settingsBtn.onClick.AddListener(OpenSettingsPanel);
        quitGameBtn.onClick.AddListener(QuitGame);
        
        // ===== 返回按钮 =====
        if (backToMainBtn != null)
            backToMainBtn.onClick.AddListener(BackToMainMenu);
        
        if (settingsBackBtn != null)
            settingsBackBtn.onClick.AddListener(BackToMainMenu);
        
        // ===== 音量绑定 =====
        if (volumeSlider != null)
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        
        // 加载保存的设置
        LoadSettings();
        
        // ===== 订阅契约事件 =====
        SubscribeRoomEvents();
        
        // 默认显示主菜单
        ShowMainMenu();
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
                Debug.Log("✅ GameMainMenuController 订阅契约事件成功");
            }
            else
            {
                Debug.Log("⏳ 等待契约绑定...");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅契约事件失败（等待契约实现）：{e.Message}");
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
    
    // ==================== 显示主菜单 ====================
    void ShowMainMenu()
    {
        mainMenuPanel.SetActive(true);
        joinPanel.SetActive(false);
        settingsPanel.SetActive(false);
        
        if (statusText != null)
            statusText.text = "🎮 欢迎来到躲猫猫联机游戏！";
    }
    
    // ==================== 打开加入游戏面板 ====================
    void OpenJoinPanel()
    {
        mainMenuPanel.SetActive(false);
        joinPanel.SetActive(true);
        settingsPanel.SetActive(false);
        
        if (statusText != null)
            statusText.text = "📋 选择房间加入，或点击右侧「创建游戏」";
        
        // ===== 优先使用契约刷新房间列表 =====
        if (GameContract.IsRoomBound)
        {
            GameContract.RoomCommands.RefreshRoomList();
        }
        else
        {
            RoomListController roomList = FindObjectOfType<RoomListController>();
            if (roomList != null)
            {
                roomList.RefreshRoomList();
            }
        }
    }
    
    // ==================== 打开设置面板 ====================
    void OpenSettingsPanel()
    {
        mainMenuPanel.SetActive(false);
        joinPanel.SetActive(false);
        settingsPanel.SetActive(true);
        
        if (statusText != null)
            statusText.text = "⚙️ 游戏设置";
    }
    
    // ==================== 返回主菜单 ====================
    void BackToMainMenu()
    {
        // ===== 如果已连接，离开房间 =====
        if (GameContract.IsRoomBound && currentConnectionState == RoomConnectionState.InRoom)
        {
            GameContract.RoomCommands.LeaveRoom();
            Debug.Log("🚪 离开房间，返回主菜单");
        }
        
        ShowMainMenu();
    }
    
    // ==================== 退出游戏 ====================
    void QuitGame()
    {
        Debug.Log("退出游戏");
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
    
    // ==================== 设置功能 ====================
    void LoadSettings()
    {
        float volume = PlayerPrefs.GetFloat("Volume", 1f);
        if (volumeSlider != null)
        {
            volumeSlider.value = volume;
            AudioListener.volume = volume;
        }
    }
    
    void OnVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("Volume", value);
    }
}