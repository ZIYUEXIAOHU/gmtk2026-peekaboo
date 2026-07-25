using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class CreateRoomController : MonoBehaviour
{
    [Header("创建面板UI")]
    public TMP_InputField roomNameInput;
    public TMP_Dropdown maxPlayerDropdown;
    public Button createConfirmBtn;
    public Button backBtn;  // 返回按钮
    public TextMeshProUGUI createStatusText;
    
    [Header("主控制器")]
    public MainMenuController mainMenuController;
    
    private CustomNetworkManager netManager;
    private ManualDiscovery discovery;
    
    // ===== 房间错误回调（契约）=====
    private void OnRoomError(RoomError error)
    {
        if (error.op == RoomOp.Create)
        {
            string errorMsg = error.reason switch
            {
                RoomErrorReason.Timeout => "⏰ 创建房间超时",
                RoomErrorReason.RoomFull => "👥 房间已满",
                RoomErrorReason.ConnectionFailed => "🔌 网络连接失败",
                RoomErrorReason.AlreadyInRoom => "⚠️ 已在房间中",
                _ => $"❌ 创建失败：{error.message}"
            };
            
            createStatusText.text = errorMsg;
            createStatusText.color = Color.red;
            createStatusText.gameObject.SetActive(true);
        }
    }
    
    void Start()
    {
        netManager = FindObjectOfType<CustomNetworkManager>();
        discovery = FindObjectOfType<ManualDiscovery>();
        
        createConfirmBtn.onClick.AddListener(CreateRoom);
        roomNameInput.onEndEdit.AddListener(OnRoomNameEndEdit);
        
        if (backBtn != null)
            backBtn.onClick.AddListener(OnBackClicked);
        
        roomNameInput.text = GetDefaultRoomName();
        maxPlayerDropdown.value = 2;
        
        SubscribeEvents();
    }
    
    void SubscribeEvents()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnRoomError += OnRoomError;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅房间事件失败（等待契约实现）：{e.Message}");
        }
    }
    
    void OnDestroy()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnRoomError -= OnRoomError;
            }
        }
        catch { }
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
        
        int maxPlayers = GetMaxPlayers();
        
        createStatusText.text = $"⏳ 正在创建房间 \"{roomName}\"...";
        createStatusText.color = Color.yellow;
        createStatusText.gameObject.SetActive(true);
        
        if (GameContract.IsRoomBound)
        {
            Debug.Log($"[契约] 创建房间：{roomName}，最大人数：{maxPlayers}");
            GameContract.RoomCommands.CreateRoom(roomName, maxPlayers);
        }
        else
        {
            if (netManager == null)
            {
                createStatusText.text = "❌ 错误：找不到网络管理器！";
                createStatusText.color = Color.red;
                createStatusText.gameObject.SetActive(true);
                return;
            }
            
            PlayerPrefs.SetString("RoomName", roomName);
            netManager.maxConnections = maxPlayers;
            netManager.StartHost();
            
            if (discovery != null)
            {
                discovery.StartBroadcasting();
            }
        }
        
        createStatusText.text = $"✅ 房间 \"{roomName}\" 创建成功！正在进入选角场景...";
        createStatusText.color = Color.green;
        
        Debug.Log($"房间创建成功：{roomName}，最大人数：{maxPlayers}，即将进入选角场景");

        StartCoroutine(DelayedEnterGameScene());
    }
    
    IEnumerator DelayedEnterGameScene()
    {
        yield return new WaitForSeconds(0.8f);
        
        if (mainMenuController != null)
        {
            mainMenuController.ShowMainMenu();
        }
        
        createStatusText.gameObject.SetActive(false);
        
        if (netManager != null && !string.IsNullOrEmpty(netManager.gameScene))
        {
            Debug.Log("进入选角场景");
            netManager.ServerChangeScene(netManager.gameScene);
        }
        else
        {
            Debug.LogWarning("netManager 或 gameScene 为空，无法跳转");
        }
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
    
    void OnBackClicked()
    {
        Debug.Log("← 返回主菜单");
        
        if (mainMenuController != null)
        {
            mainMenuController.ShowMainMenu();
        }
        
        gameObject.SetActive(false);
    }
    
    public void ResetCreatePanel()
    {
        roomNameInput.text = GetDefaultRoomName();
        maxPlayerDropdown.value = 2;
        createStatusText.text = "填写信息创建房间";
        createStatusText.color = Color.white;
        createStatusText.gameObject.SetActive(false);
    }
}