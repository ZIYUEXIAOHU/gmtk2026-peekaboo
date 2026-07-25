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
    [Tooltip("可选：展示房间短码")]
    public TextMeshProUGUI roomCodeText;
    
    [Header("主控制器")]
    public MainMenuController mainMenuController;
    
    private CustomNetworkManager netManager;
    private ManualDiscovery discovery;
    private bool _waitingEnterScene;
    
    private void OnRoomError(RoomError error)
    {
        if (error.op != RoomOp.Create && error.op != RoomOp.Find) return;

        string errorMsg = error.reason switch
        {
            RoomErrorReason.Timeout => "⏰ 创建房间超时",
            RoomErrorReason.RoomFull => "👥 房间已满",
            RoomErrorReason.ConnectionFailed => "🔌 网络连接失败",
            RoomErrorReason.AlreadyInRoom => "⚠️ 已在房间中",
            RoomErrorReason.SlotFull => error.message == "Seeker"
                ? "⚠️ 抓捕者已满！"
                : "⚠️ 躲藏者已满！",
            RoomErrorReason.RoleNotSelected => "⚠️ 请先选择身份",
            _ => $"❌ 创建失败：{error.message}"
        };

        ShowStatus(errorMsg, Color.red);
        _waitingEnterScene = false;
    }

    void OnConnectionStateChanged(RoomConnectionState state)
    {
        if (!_waitingEnterScene) return;

        if (state == RoomConnectionState.InRoom)
        {
            string code = GameContract.IsRoomBound
                ? GameContract.RoomState.CurrentRoomCode
                : string.Empty;

            if (!string.IsNullOrEmpty(code))
            {
                ShowStatus($"✅ 创建成功！短码：{code}", Color.green);
                if (roomCodeText != null)
                {
                    roomCodeText.gameObject.SetActive(true);
                    roomCodeText.text = code;
                }
            }
            else
            {
                ShowStatus("✅ 房间创建成功！正在进入...", Color.green);
            }

            StartCoroutine(DelayedEnterGameScene());
        }
        else if (state == RoomConnectionState.Failed)
        {
            _waitingEnterScene = false;
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

        if (roomCodeText != null)
            roomCodeText.gameObject.SetActive(false);
        
        SubscribeEvents();
    }
    
    void SubscribeEvents()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnRoomError += OnRoomError;
                GameContract.RoomEvents.OnConnectionStateChanged += OnConnectionStateChanged;
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
                GameContract.RoomEvents.OnConnectionStateChanged -= OnConnectionStateChanged;
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
            ShowStatus("❌ 请输入房间名称！", Color.red);
            return;
        }

        if (GameContract.IsRoomBound &&
            GameContract.RoomState.PreferredRole == PlayerRole.None)
        {
            ShowStatus("⚠️ 请先选择身份", Color.red);
            return;
        }
        
        int maxPlayers = GetMaxPlayers();
        ShowStatus($"⏳ 正在创建房间 \"{roomName}\"...", Color.yellow);
        _waitingEnterScene = true;
        
        if (GameContract.IsRoomBound)
        {
            Debug.Log($"[契约] 创建房间：{roomName}，最大人数：{maxPlayers}");
            GameContract.RoomCommands.CreateRoom(roomName, maxPlayers);
            return;
        }
        
        if (netManager == null)
        {
            ShowStatus("❌ 错误：找不到网络管理器！", Color.red);
            _waitingEnterScene = false;
            return;
        }
        
        PlayerPrefs.SetString("RoomName", roomName);
        netManager.maxConnections = maxPlayers;
        netManager.StartHost();
        
        if (discovery != null)
            discovery.StartBroadcasting();

        ShowStatus($"✅ 房间 \"{roomName}\" 创建成功！正在进入...", Color.green);
        StartCoroutine(DelayedEnterGameScene());
    }

    void ShowStatus(string msg, Color color)
    {
        if (createStatusText == null) return;
        createStatusText.text = msg;
        createStatusText.color = color;
        createStatusText.gameObject.SetActive(true);
    }
    
    IEnumerator DelayedEnterGameScene()
    {
        yield return new WaitForSeconds(0.8f);
        _waitingEnterScene = false;
        
        if (mainMenuController != null)
            mainMenuController.ShowMainMenu();
        
        if (createStatusText != null)
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
            CreateRoom();
    }
    
    void OnBackClicked()
    {
        Debug.Log("← 返回主菜单");
        _waitingEnterScene = false;
        
        if (mainMenuController != null)
            mainMenuController.ShowMainMenu();
        
        gameObject.SetActive(false);
    }
    
    public void ResetCreatePanel()
    {
        roomNameInput.text = GetDefaultRoomName();
        maxPlayerDropdown.value = 2;
        createStatusText.text = "填写信息创建房间";
        createStatusText.color = Color.white;
        createStatusText.gameObject.SetActive(false);
        if (roomCodeText != null)
        {
            roomCodeText.text = string.Empty;
            roomCodeText.gameObject.SetActive(false);
        }
        _waitingEnterScene = false;
    }
}
