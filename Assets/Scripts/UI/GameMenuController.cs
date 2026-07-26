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
    
    [Header("ESC 菜单信息")]
    public TextMeshProUGUI timeText;   // 剩余时间文本
    public TextMeshProUGUI aliveText;  // 存活数文本
    
    [Header("设置面板")]
    public GameObject settingsPanel;
    public Button settingsBackBtn;
    
    [Header("音量滑块")]
    public Slider masterVolumeSlider;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public TextMeshProUGUI masterVolumeText;
    public TextMeshProUGUI musicVolumeText;
    public TextMeshProUGUI sfxVolumeText;
    
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
        
        // 绑定音量滑块
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        
        // 订阅契约事件
        SubscribeEvents();
        
        // 初始状态
        if (escMenu != null)
            escMenu.SetActive(false);
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
        
        // 加载音量
        LoadVolumes();
    }
    
    void Update()
    {
        // ESC 键
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (settingsPanel != null && settingsPanel.activeSelf)
            {
                CloseSettings();
                return;
            }
            ToggleMenu();
        }
        
        // ===== ESC 菜单打开时，更新信息 =====
        if (escMenu != null && escMenu.activeSelf)
        {
            UpdateInfoTexts();
        }
    }
    
    // ==================== 更新信息文本（遵循契约） ====================
    void UpdateInfoTexts()
    {
        // ===== 更新剩余时间 =====
        if (timeText != null)
        {
            if (GameContract.IsBound && GameContract.State != null)
            {
                float timeLeft = GameContract.State.PhaseTimeLeft;
                timeText.text = $"{Mathf.CeilToInt(timeLeft)}s";
            }
            else
            {
                timeText.text = "--s";
            }
        }
        
        // ===== 更新存活数 =====
        if (aliveText != null)
        {
            if (GameContract.IsBound && GameContract.State != null)
            {
                int alive = GameContract.State.AliveHiders;
                int total = GameContract.State.TotalHiders;
                aliveText.text = $"{alive}/{total}";
            }
            else
            {
                aliveText.text = "-/-";
            }
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
            ReturnToLobby();
        }
    }
    
    // ==================== 返回大厅场景 ====================
    void ReturnToLobby()
    {
        Debug.Log("🏠 返回大厅场景");
        
        if (escMenu != null)
            escMenu.SetActive(false);
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
        isMenuOpen = false;
        isSettingsOpen = false;
        
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
    
    // ==================== 音量控制（通过契约） ====================
    
    void OnMasterVolumeChanged(float value)
    {
        if (masterVolumeText != null)
            masterVolumeText.text = Mathf.RoundToInt(value * 100) + "%";

        if (GameContract.IsAudioBound)
            GameContract.Audio.SetMasterVolume(value);
        else
        {
            AudioListener.volume = value;
            PlayerProfile.SetMasterVolume(value);
        }
    }

    void OnMusicVolumeChanged(float value)
    {
        if (musicVolumeText != null)
            musicVolumeText.text = Mathf.RoundToInt(value * 100) + "%";

        if (GameContract.IsAudioBound)
            GameContract.Audio.SetMusicVolume(value);
        else
            PlayerProfile.SetMusicVolume(value);
    }

    void OnSFXVolumeChanged(float value)
    {
        if (sfxVolumeText != null)
            sfxVolumeText.text = Mathf.RoundToInt(value * 100) + "%";

        if (GameContract.IsAudioBound)
            GameContract.Audio.SetSFXVolume(value);
        else
            PlayerProfile.SetSFXVolume(value);
    }

    void LoadVolumes()
    {
        float master = PlayerProfile.MasterVolume;
        float music = PlayerProfile.MusicVolume;
        float sfx = PlayerProfile.SFXVolume;

        if (masterVolumeSlider != null) masterVolumeSlider.value = master;
        if (musicVolumeSlider != null) musicVolumeSlider.value = music;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = sfx;

        if (GameContract.IsAudioBound)
        {
            GameContract.Audio.SetMasterVolume(master);
            GameContract.Audio.SetMusicVolume(music);
            GameContract.Audio.SetSFXVolume(sfx);
        }
        else
        {
            AudioListener.volume = master;
        }
    }
    
    // ==================== 退出房间（遵循契约） ====================
    void QuitRoom()
    {
        Debug.Log("🚪 退出房间");
        
        CloseMenu();
        
        if (settingsPanel != null && settingsPanel.activeSelf)
            settingsPanel.SetActive(false);
        
        if (GameContract.IsRoomBound)
        {
            GameContract.RoomCommands.LeaveRoom();
            Debug.Log("✅ 已调用 LeaveRoom()，等待断连事件...");
        }
        else
        {
            Debug.Log("⚠️ 契约未绑定，直接返回大厅");
            ReturnToLobby();
        }
    }
}