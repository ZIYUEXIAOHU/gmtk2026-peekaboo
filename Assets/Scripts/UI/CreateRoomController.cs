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
            RoomErrorReason.Timeout => "⏰ Create room timed out",
            RoomErrorReason.RoomFull => "👥 Room is full",
            RoomErrorReason.ConnectionFailed => "🔌 Network connection failed",
            RoomErrorReason.AlreadyInRoom => "⚠️ Already in a room",
            RoomErrorReason.SlotFull => error.message == "Seeker"
                ? "⚠️ SEEKER SLOT FULL!"
                : "⚠️ HIDER SLOT FULL!",
            RoomErrorReason.RoleNotSelected => "⚠️ Please select a role first",
            _ => $"❌ Create failed: {error.message}"
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
                ShowStatus($"✅ Created successfully! Code: {code}", Color.green);
                if (roomCodeText != null)
                {
                    roomCodeText.gameObject.SetActive(true);
                    roomCodeText.text = code;
                }
            }
            else
            {
                ShowStatus("✅ Room created successfully! Entering...", Color.green);
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
        return $"{PlayerProfile.PlayerName}'s Room";
    }
    
    void CreateRoom()
    {
        string roomName = roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(roomName))
        {
            ShowStatus("❌ Please enter a room name!", Color.red);
            return;
        }

        if (GameContract.IsRoomBound &&
            GameContract.RoomState.PreferredRole == PlayerRole.None)
        {
            ShowStatus("⚠️ Please select a role first", Color.red);
            return;
        }
        
        int maxPlayers = GetMaxPlayers();
        ShowStatus($"⏳ Creating room \"{roomName}\"...", Color.yellow);
        _waitingEnterScene = true;
        
        if (GameContract.IsRoomBound)
        {
            Debug.Log($"[契约] 创建房间：{roomName}，最大人数：{maxPlayers}");
            GameContract.RoomCommands.CreateRoom(roomName, maxPlayers);
            return;
        }
        
        if (netManager == null)
        {
            ShowStatus("❌ Error: network manager not found!", Color.red);
            _waitingEnterScene = false;
            return;
        }
        
        PlayerPrefs.SetString("RoomName", roomName);
        netManager.maxConnections = maxPlayers;
        netManager.StartHost();
        
        if (discovery != null)
            discovery.StartBroadcasting();

        ShowStatus($"✅ Room \"{roomName}\" created successfully! Entering...", Color.green);
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
        createStatusText.text = "Fill in details to create a room";
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
