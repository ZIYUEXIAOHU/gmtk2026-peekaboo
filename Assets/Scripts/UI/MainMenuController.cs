using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mirror;

public class MainMenuController : MonoBehaviour
{
    [Header("主菜单")]
    public GameObject mainMenuPanel;  // GameMenuPanel

    [Header("主菜单按钮")]
    public Button createGameBtn;    // 创建游戏
    public Button joinGameBtn;      // 加入游戏
    public Button settingsBtn;      // 设置
    public Button quitGameBtn;      // 退出游戏
    
    [Header("加入游戏面板")]
    public GameObject joinPanel;    // 加入面板
    public Button joinBtn;          // 加入确认按钮
    public Button joinBackBtn;      // 返回按钮
    
    [Header("创建游戏面板")]
    public GameObject createPanel;  // 创建面板
    public Button createBackBtn;    // 返回按钮
    
    // ===== 身份选择按钮（创建面板内） =====
    [Header("身份选择按钮（创建面板内）")]
    public Button hiderBtn;         // 躲藏者按钮
    public Button hunterBtn;        // 抓捕者按钮
    
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
    private CustomNetworkManager netManager;
    private ManualDiscovery discovery;
    
    void Start()
    {
        netManager = FindObjectOfType<CustomNetworkManager>();
        discovery = FindObjectOfType<ManualDiscovery>();
        
        // ===== 主菜单按钮 =====
        if (joinGameBtn != null)
            joinGameBtn.onClick.AddListener(OpenJoinPanel);
        
        if (createGameBtn != null)
            createGameBtn.onClick.AddListener(OpenCreatePanel);
        
        if (settingsBtn != null)
            settingsBtn.onClick.AddListener(OpenSettingsPanel);
        
        if (quitGameBtn != null)
            quitGameBtn.onClick.AddListener(QuitGame);
        
        // ===== 加入面板按钮 =====
        if (joinBtn != null)
            joinBtn.onClick.AddListener(OnJoinClicked);
        
        if (joinBackBtn != null)
            joinBackBtn.onClick.AddListener(CloseJoinPanel);
        
        // ===== 创建面板按钮 =====
        if (createBackBtn != null)
            createBackBtn.onClick.AddListener(CloseCreatePanel);
        
        // ===== 身份选择按钮 =====
        if (hiderBtn != null)
            hiderBtn.onClick.AddListener(() => Debug.Log("选择躲藏者"));
        if (hunterBtn != null)
            hunterBtn.onClick.AddListener(() => Debug.Log("选择抓捕者"));
        
        // ===== 设置面板按钮 =====
        if (settingsBackBtn != null)
            settingsBackBtn.onClick.AddListener(CloseSettingsPanel);
        
        // ===== 音量绑定 =====
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        
        SubscribeRoomEvents();
        ShowMainMenu();
        LoadSettings();
    }
    
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
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅契约事件失败：{e.Message}");
        }
    }
    
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
            controller.UpdateRoomList(roomList);
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
    
    public void ShowMainMenu()
    {
        if (joinPanel != null) joinPanel.SetActive(false);
        if (createPanel != null) createPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
        
        if (statusText != null)
            statusText.text = "🎮 欢迎来到躲猫猫！";
    }
    
    // ==================== 加入游戏 ====================
    void OpenJoinPanel()
    {
        if (joinPanel != null) joinPanel.SetActive(true);
        if (createPanel != null) createPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        
        if (statusText != null)
            statusText.text = "📋 选择房间加入";
        
        if (GameContract.IsRoomBound)
            GameContract.RoomCommands.RefreshRoomList();
        else
        {
            RoomListController roomList = FindObjectOfType<RoomListController>();
            if (roomList != null)
                roomList.RefreshRoomList();
        }
    }
    
    void CloseJoinPanel()
    {
        if (joinPanel != null) joinPanel.SetActive(false);
        ShowMainMenu();
    }
    
    void OnJoinClicked()
    {
        Debug.Log("加入房间");
    }
    
    // ==================== 创建游戏 ====================
    void OpenCreatePanel()
    {
        if (createPanel != null) createPanel.SetActive(true);
        if (joinPanel != null) joinPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        
        if (statusText != null)
            statusText.text = "🏠 创建新房间";
    }
    
    void CloseCreatePanel()
    {
        if (createPanel != null) createPanel.SetActive(false);
        ShowMainMenu();
    }
    
    // ==================== 设置 ====================
    void OpenSettingsPanel()
    {
        if (settingsPanel != null) settingsPanel.SetActive(true);
        if (joinPanel != null) joinPanel.SetActive(false);
        if (createPanel != null) createPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        
        if (statusText != null)
            statusText.text = "⚙️ 游戏设置";
    }
    
    void CloseSettingsPanel()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        ShowMainMenu();
    }
    
    void QuitGame()
    {
        Debug.Log("退出游戏");
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
    
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