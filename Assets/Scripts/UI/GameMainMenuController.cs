using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    public Slider volumeSlider;              // ← 只保留音量滑块
    
    [Header("状态")]
    public TextMeshProUGUI statusText;
    
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
        
        // 默认显示主菜单
        ShowMainMenu();
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
        
        RoomListController roomList = FindObjectOfType<RoomListController>();
        if (roomList != null)
        {
            roomList.RefreshRoomList();
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
        // 加载音量
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