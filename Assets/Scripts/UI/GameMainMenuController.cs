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
    public Slider masterVolumeSlider;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public TextMeshProUGUI masterVolumeText;
    public TextMeshProUGUI musicVolumeText;
    public TextMeshProUGUI sfxVolumeText;
    
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
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        
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
            RoomConnectionState.Disconnected => "📋 Disconnected",
            RoomConnectionState.Connecting => "⏳ Connecting...",
            RoomConnectionState.InRoom => "✅ Joined room",
            RoomConnectionState.Failed => "❌ Connection failed, please retry",
            _ => "📋 Disconnected"
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
            RoomErrorReason.Timeout => "⏰ Operation timed out",
            RoomErrorReason.RoomNotFound => "🔍 Room not found",
            RoomErrorReason.RoomFull => "👥 Room is full",
            RoomErrorReason.ConnectionFailed => "🔌 Network connection failed",
            RoomErrorReason.AlreadyInRoom => "⚠️ Already in a room",
            _ => $"❌ Operation failed: {error.message}"
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
            statusText.text = "🎮 Welcome to Peekaboo multiplayer!";
    }
    
    // ==================== 打开加入游戏面板 ====================
    void OpenJoinPanel()
    {
        mainMenuPanel.SetActive(false);
        joinPanel.SetActive(true);
        settingsPanel.SetActive(false);
        
        if (statusText != null)
            statusText.text = "📋 Select a room to join, or tap Create Game on the right";
        
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
            statusText.text = "⚙️ Game Settings";
    }
    
    // ==================== 返回主菜单 ====================
    void BackToMainMenu()
    {
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
    
    // ==================== 音量控制 ====================
    void LoadSettings()
    {
        float master = PlayerPrefs.GetFloat("MasterVolume", 0.8f);
        float music = PlayerPrefs.GetFloat("MusicVolume", 0.8f);
        float sfx = PlayerPrefs.GetFloat("SFXVolume", 0.8f);
        
        if (masterVolumeSlider != null) masterVolumeSlider.value = master;
        if (musicVolumeSlider != null) musicVolumeSlider.value = music;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = sfx;
        
        AudioListener.volume = master;
    }
    
    void OnMasterVolumeChanged(float value)
    {
        if (masterVolumeText != null)
            masterVolumeText.text = Mathf.RoundToInt(value * 100) + "%";
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);
    }
    
    void OnMusicVolumeChanged(float value)
    {
        if (musicVolumeText != null)
            musicVolumeText.text = Mathf.RoundToInt(value * 100) + "%";
        PlayerPrefs.SetFloat("MusicVolume", value);
    }
    
    void OnSFXVolumeChanged(float value)
    {
        if (sfxVolumeText != null)
            sfxVolumeText.text = Mathf.RoundToInt(value * 100) + "%";
        PlayerPrefs.SetFloat("SFXVolume", value);
    }
}