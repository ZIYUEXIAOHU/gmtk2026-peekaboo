using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CreateRoomController : MonoBehaviour
{
    [Header("创建面板UI")]
    public TMP_InputField roomNameInput;
    public TMP_Dropdown maxPlayerDropdown;
    public Button createConfirmBtn;
    public TextMeshProUGUI createStatusText;
    
    [Header("主控制器")]
    public MainMenuController mainMenuController;
    
    private CustomNetworkManager netManager;
    private ManualDiscovery discovery;
    
    void Start()
    {
        netManager = FindObjectOfType<CustomNetworkManager>();
        discovery = FindObjectOfType<ManualDiscovery>();
        
        createConfirmBtn.onClick.AddListener(CreateRoom);
        roomNameInput.onEndEdit.AddListener(OnRoomNameEndEdit);
        
        roomNameInput.text = GetDefaultRoomName();
        maxPlayerDropdown.value = 2;
    }
    
    string GetDefaultRoomName()
    {
        string playerName = System.Environment.MachineName;
        return $"{playerName}的房间";
    }
    
    void CreateRoom()
    {
        string roomName = roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(roomName))
        {
            createStatusText.text = "❌ 请输入房间名称！";
            createStatusText.color = Color.red;
            createStatusText.gameObject.SetActive(true);
            return;
        }
        
        if (netManager == null)
        {
            createStatusText.text = "❌ 错误：找不到网络管理器！";
            createStatusText.color = Color.red;
            createStatusText.gameObject.SetActive(true);
            return;
        }
        
        int maxPlayers = GetMaxPlayers();
        
        createStatusText.text = $"⏳ 正在创建房间 \"{roomName}\"...";
        createStatusText.color = Color.yellow;
        createStatusText.gameObject.SetActive(true);
        
        PlayerPrefs.SetString("RoomName", roomName);
        netManager.maxConnections = maxPlayers;
        
        // ===== 启动主机 =====
        netManager.StartHost();
        
        if (discovery != null)
        {
            discovery.StartBroadcasting();
        }
        
        createStatusText.text = $"✅ 房间 \"{roomName}\" 创建成功！({maxPlayers}人)";
        createStatusText.color = Color.green;
        
        Debug.Log($"房间创建成功：{roomName}，最大人数：{maxPlayers}");
        
        // ===== 房主直接进入游戏场景 =====
        EnterGameScene();
    }
    
    int GetMaxPlayers()
    {
        string selected = maxPlayerDropdown.options[maxPlayerDropdown.value].text;
        return int.Parse(selected);
    }
    
    void OnRoomNameEndEdit(string text)
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            CreateRoom();
        }
    }
    
    public void ResetCreatePanel()
    {
        roomNameInput.text = GetDefaultRoomName();
        maxPlayerDropdown.value = 2;
        createStatusText.text = "填写信息创建房间";
        createStatusText.color = Color.white;
        createStatusText.gameObject.SetActive(false);
    }
    
    // ==================== 房主进入游戏场景 ====================
    void EnterGameScene()
    {
        // 延迟执行，确保服务器完全启动
        Invoke(nameof(DoEnterGameScene), 0.8f);
    }
    
    void DoEnterGameScene()
    {
        if (netManager != null && NetworkServer.active)
        {
            Debug.Log("🚀 房主进入游戏场景...");
            
            // 隐藏创建面板
            if (mainMenuController != null)
            {
                mainMenuController.SetCreateModeActive(false);
                mainMenuController.UpdateStatusText("🎮 进入游戏...");
            }
            
            // 隐藏状态文字
            createStatusText.gameObject.SetActive(false);
            
            // 切换到游戏场景
            netManager.ServerChangeScene(netManager.gameScene);
        }
        else
        {
            Debug.LogWarning("⚠️ 服务器未启动，无法进入游戏");
            createStatusText.text = "⚠️ 服务器启动失败，请重试";
            createStatusText.color = Color.red;
            createStatusText.gameObject.SetActive(true);
        }
    }
}