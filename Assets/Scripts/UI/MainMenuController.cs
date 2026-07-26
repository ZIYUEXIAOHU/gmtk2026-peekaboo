using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using Mirror;

public class MainMenuController : MonoBehaviour
{
    public const string PrefPreferredRole = "PreferredRole";

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
    [Tooltip("短码输入（可复用原 SearchInputField）")]
    public TMP_InputField roomCodeInput;
    [Tooltip("可选：加入面板内躲藏者按钮；为空则用下方 hiderBtn")]
    public Button joinHiderBtn;
    [Tooltip("可选：加入面板内抓捕者按钮；为空则用下方 hunterBtn")]
    public Button joinHunterBtn;
    
    [Header("创建游戏面板")]
    public GameObject createPanel;  // 创建面板
    public Button createBackBtn;    // 返回按钮
    
    [Header("身份选择按钮（创建/加入共用）")]
    public Button hiderBtn;         // 躲藏者按钮
    public Button hunterBtn;        // 抓捕者按钮
    [Tooltip("身份按钮所在面板；为空则用 hiderBtn 的父物体")]
    public GameObject roleSelectPanel;
    [Tooltip("创建房间时的默认最大人数（场景内无人数下拉框时使用）")]
    public int defaultMaxPlayers = 4;
    
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
    private bool _joinPanelOpen;
    private bool _createPanelOpen;
    
    private bool _waitingCreateEnter;
    private bool _roomEventsSubscribed;
    private bool isCreatingRoom;
    
    void Start()
    {
        netManager = FindObjectOfType<CustomNetworkManager>();
        discovery = FindObjectOfType<ManualDiscovery>();

        EnsureJoinUiRefs();
        
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

        if (roomCodeInput != null)
            roomCodeInput.onEndEdit.AddListener(OnRoomCodeEndEdit);
        
        // ===== 创建面板按钮 =====
        if (createBackBtn != null)
            createBackBtn.onClick.AddListener(CloseCreatePanel);
        
        // ===== 身份选择按钮 =====（创建面板分支在 OnSelectRoleClicked 里走 CreateRoomAs）
        WireRoleButton(hiderBtn, PlayerRole.Hider);
        WireRoleButton(hunterBtn, PlayerRole.Seeker);
        WireRoleButton(joinHiderBtn, PlayerRole.Hider);
        WireRoleButton(joinHunterBtn, PlayerRole.Seeker);
        
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
        
        StartCoroutine(EnsureRoomEventsSubscribed());
        ShowMainMenu();
        LoadSettings();
    }

    void EnsureJoinUiRefs()
    {
        if (roomCodeInput == null && joinPanel != null)
        {
            roomCodeInput = joinPanel.GetComponentInChildren<TMP_InputField>(true);
        }

        if (joinHiderBtn == null) joinHiderBtn = hiderBtn;
        if (joinHunterBtn == null) joinHunterBtn = hunterBtn;

        if (roleSelectPanel == null && hiderBtn != null && hiderBtn.transform.parent != null)
            roleSelectPanel = hiderBtn.transform.parent.gameObject;

        SetRoleSelectVisible(false);
    }

    void SetRoleSelectVisible(bool visible)
    {
        if (roleSelectPanel == null) return;
        roleSelectPanel.SetActive(visible);
        if (visible)
        {
            roleSelectPanel.transform.SetAsLastSibling();
            Debug.Log("[MainMenu] 身份选择面板已显示");
        }
    }

    void WireRoleButton(Button btn, PlayerRole role)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => OnSelectRoleClicked(role));
    }

    IEnumerator EnsureRoomEventsSubscribed()
    {
        float waited = 0f;
        while (!GameContract.IsRoomBound && waited < 5f)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
        }

        SubscribeRoomEvents();
        if (!GameContract.IsRoomBound)
            Debug.LogWarning("[MainMenu] 房间契约仍未绑定，加入/找房 UI 可能无响应");
    }
    
    void SubscribeRoomEvents()
    {
        if (_roomEventsSubscribed || !GameContract.IsRoomBound) return;

        try
        {
            GameContract.RoomEvents.OnConnectionStateChanged += OnConnectionStateChanged;
            GameContract.RoomEvents.OnRoomListUpdated += OnRoomListUpdated;
            GameContract.RoomEvents.OnFoundRoomChanged += OnFoundRoomChanged;
            GameContract.RoomEvents.OnRoomError += OnRoomError;
            _roomEventsSubscribed = true;
            Debug.Log("✅ MainMenuController 订阅契约事件成功");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅房间事件失败：{e.Message}");
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

        if (state == RoomConnectionState.InRoom && GameContract.IsRoomBound)
        {
            string code = GameContract.RoomState.CurrentRoomCode;
            if (!string.IsNullOrEmpty(code) && statusText != null)
                statusText.text = $"✅ 已在房间，短码：{code}";

            if (_waitingCreateEnter)
                StartCoroutine(DelayedEnterGameScene());
        }
        else if (state == RoomConnectionState.Failed)
        {
            _waitingCreateEnter = false;
        }
    }
    
    void OnRoomListUpdated(IReadOnlyList<RoomInfo> roomList)
    {
        RoomListController controller = FindObjectOfType<RoomListController>();
        if (controller != null)
            controller.UpdateRoomList(roomList);
    }

    void OnFoundRoomChanged(RoomInfo? room)
    {
        if (!_joinPanelOpen) return;
        RefreshJoinRoleUiFromState(room);
    }

    void RefreshJoinRoleUiFromState(RoomInfo? room = null)
    {
        if (!_joinPanelOpen) return;

        if (!room.HasValue && GameContract.IsRoomBound)
            room = GameContract.RoomState.FoundRoom;

        if (!room.HasValue)
        {
            SetRoleSelectVisible(false);
            RefreshJoinRoleButtons(default);
            return;
        }

        ShowJoinRoleSelect();

        RoomInfo info = room.Value;
        RoleSlots projected = RoleSlots.ProjectForJoiner(
            info.currentPlayers, info.seekerCount, info.hiderCount);
        RefreshJoinRoleButtons(projected);

        Debug.Log(
            $"[MainMenu] JOIN 后显示身份选择 code={info.roomCode} " +
            $"H{projected.hiderCount}/{projected.hiderMax} S{projected.seekerCount}/{projected.seekerMax}");

        if (GameContract.RoomState.PreferredRole != PlayerRole.None)
        {
            TryJoinFoundRoomNow();
            return;
        }

        if (statusText != null)
        {
            statusText.text =
                $"✅ 找到房间「{info.roomName}」 短码 {info.roomCode}\n" +
                $"请点 HIDER / HUNTER（躲藏 {projected.hiderCount}/{projected.hiderMax}，" +
                $"抓捕 {projected.seekerCount}/{projected.seekerMax}）";
        }
    }
    
    void OnRoomError(RoomError error)
    {
        if (error.op == RoomOp.Create)
            isCreatingRoom = false;

        string errorMsg = error.reason switch
        {
            RoomErrorReason.Timeout => "⏰ 操作超时",
            RoomErrorReason.RoomNotFound => "🔍 房间不存在",
            RoomErrorReason.RoomFull => "👥 房间已满",
            RoomErrorReason.ConnectionFailed => "🔌 网络连接失败",
            RoomErrorReason.AlreadyInRoom => "⚠️ 已在房间中",
            RoomErrorReason.SlotFull => error.message == "Seeker"
                ? "⚠️ 抓捕者已满！"
                : "⚠️ 躲藏者已满！",
            RoomErrorReason.RoleNotSelected => "⚠️ 请先选择身份",
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
            if (_roomEventsSubscribed && GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnConnectionStateChanged -= OnConnectionStateChanged;
                GameContract.RoomEvents.OnRoomListUpdated -= OnRoomListUpdated;
                GameContract.RoomEvents.OnFoundRoomChanged -= OnFoundRoomChanged;
                GameContract.RoomEvents.OnRoomError -= OnRoomError;
                _roomEventsSubscribed = false;
            }
        }
        catch { }
    }
    
    public void ShowMainMenu()
    {
        _joinPanelOpen = false;
        _createPanelOpen = false;

        if (joinPanel != null) joinPanel.SetActive(false);
        if (createPanel != null) createPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
        
        if (statusText != null)
            statusText.text = "🎮 欢迎来到躲猫猫！";

        SetRoleSelectVisible(false);
    }
    
    // ==================== 加入游戏 ====================
    void OpenJoinPanel()
    {
        _joinPanelOpen = true;
        _createPanelOpen = false;

        if (joinPanel != null) joinPanel.SetActive(true);
        if (createPanel != null) createPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        
        if (statusText != null)
            statusText.text = "📋 输入房间短码，点 JOIN 后再选身份";

        SetRoleSelectVisible(false);
        RefreshJoinRoleButtons(default);
    }
    
    void CloseJoinPanel()
    {
        _joinPanelOpen = false;
        if (joinPanel != null) joinPanel.SetActive(false);
        ShowMainMenu();
    }

    void OnRoomCodeEndEdit(string _)
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            TryFindRoomByCodeInput();
    }
    
    void OnJoinClicked()
    {
        if (!GameContract.IsRoomBound)
        {
            if (statusText != null) statusText.text = "❌ 房间服务未就绪";
            return;
        }

        if (!GameContract.RoomState.FoundRoom.HasValue)
        {
            TryFindRoomByCodeInput();
            return;
        }

        if (GameContract.RoomState.PreferredRole == PlayerRole.None)
        {
            ShowJoinRoleSelect();
            if (statusText != null) statusText.text = "⚠️ 请先点 HIDER 或 HUNTER";
            return;
        }

        TryJoinFoundRoomNow();
    }

    void TryJoinFoundRoomNow()
    {
        if (!GameContract.IsRoomBound) return;
        if (!GameContract.RoomState.FoundRoom.HasValue) return;
        if (GameContract.RoomState.PreferredRole == PlayerRole.None) return;

        if (statusText != null) statusText.text = "⏳ 正在加入房间...";
        GameContract.RoomCommands.JoinFoundRoom();
    }

    void TryFindRoomByCodeInput()
    {
        if (!GameContract.IsRoomBound)
        {
            if (statusText != null) statusText.text = "❌ 房间服务未就绪";
            return;
        }

        string code = roomCodeInput != null ? roomCodeInput.text : string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            if (statusText != null) statusText.text = "⚠️ 请输入房间短码";
            return;
        }

        if (statusText != null) statusText.text = $"⏳ 正在查找短码 {code.Trim().ToUpperInvariant()}...";
        GameContract.RoomCommands.FindRoomByCode(code);
        RefreshJoinRoleUiFromState();
        StartCoroutine(WatchFoundRoomAfterSearch());
    }

    IEnumerator WatchFoundRoomAfterSearch()
    {
        float waited = 0f;
        while (_joinPanelOpen && waited < 8f)
        {
            if (GameContract.IsRoomBound && GameContract.RoomState.FoundRoom.HasValue)
            {
                RefreshJoinRoleUiFromState();
                yield break;
            }
            yield return null;
            waited += Time.unscaledDeltaTime;
        }
    }

    void ShowJoinRoleSelect()
    {
        SetRoleSelectVisible(true);
    }

    void OnSelectRoleClicked(PlayerRole role)
    {
        // 创建面板：走 CreateRoomAs（记偏好身份 + 契约/直连兜底 + 防重复点击）
        if (_createPanelOpen)
        {
            if (GameContract.IsRoomBound)
                GameContract.RoomCommands.TrySelectRoleBeforeEnter(role);
            CreateRoomAs(role);
            return;
        }

        if (!GameContract.IsRoomBound)
        {
            if (statusText != null) statusText.text = "❌ 房间服务未就绪";
            return;
        }

        if (_joinPanelOpen && GameContract.RoomState.FoundRoom.HasValue)
        {
            RoomInfo found = GameContract.RoomState.FoundRoom.Value;
            RoleSlots projected = RoleSlots.ProjectForJoiner(
                found.currentPlayers, found.seekerCount, found.hiderCount);
            if (role == PlayerRole.Hider && projected.HiderFull)
            {
                if (statusText != null) statusText.text = "⚠️ 躲藏者已满！";
                return;
            }
            if (role == PlayerRole.Seeker && projected.SeekerFull)
            {
                if (statusText != null) statusText.text = "⚠️ 抓捕者已满！";
                return;
            }
        }

        if (!GameContract.RoomCommands.TrySelectRoleBeforeEnter(role))
            return;

        string name = role == PlayerRole.Hider ? "躲藏者" : "抓捕者";
        if (statusText != null)
            statusText.text = $"✅ 已选择：{name}";

        if (_createPanelOpen)
            BeginCreateRoomAfterRoleSelected();
        else if (_joinPanelOpen && GameContract.RoomState.FoundRoom.HasValue)
            TryJoinFoundRoomNow();
    }

    void BeginCreateRoomAfterRoleSelected()
    {
        if (!GameContract.IsRoomBound) return;

        string roomName = $"{System.Environment.MachineName}的房间";
        if (statusText != null)
            statusText.text = $"⏳ 正在创建房间「{roomName}」...";

        _waitingCreateEnter = true;
        GameContract.RoomCommands.CreateRoom(roomName, 4);
    }

    IEnumerator DelayedEnterGameScene()
    {
        _waitingCreateEnter = false;
        string code = GameContract.IsRoomBound ? GameContract.RoomState.CurrentRoomCode : string.Empty;
        if (statusText != null && !string.IsNullOrEmpty(code))
            statusText.text = $"✅ 创建成功！短码：{code}，正在进入...";

        yield return new WaitForSeconds(0.8f);

        ShowMainMenu();

        if (netManager == null)
            netManager = FindObjectOfType<CustomNetworkManager>();

        if (netManager != null && NetworkServer.active && !string.IsNullOrEmpty(netManager.gameScene))
        {
            Debug.Log($"进入游戏场景，短码={code}");
            netManager.ServerChangeScene(netManager.gameScene);
        }
        else
        {
            Debug.LogWarning("无法切到 gameScene：NetworkManager 未就绪");
        }
    }

    void RefreshJoinRoleButtons(RoleSlots projected)
    {
        bool found = GameContract.IsRoomBound && GameContract.RoomState.FoundRoom.HasValue;
        bool hiderOk = true;
        bool seekerOk = true;

        if (found)
        {
            RoomInfo info = GameContract.RoomState.FoundRoom.Value;
            projected = RoleSlots.ProjectForJoiner(
                info.currentPlayers, info.seekerCount, info.hiderCount);
            hiderOk = !projected.HiderFull;
            seekerOk = !projected.SeekerFull;
        }

        if (_joinPanelOpen)
        {
            SetRoleButtonInteractable(EffectiveJoinHiderBtn(), hiderOk);
            SetRoleButtonInteractable(EffectiveJoinHunterBtn(), seekerOk);
        }

        if (_createPanelOpen)
        {
            SetRoleButtonInteractable(hiderBtn, true);
            SetRoleButtonInteractable(hunterBtn, true);
        }
    }

    Button EffectiveJoinHiderBtn() => joinHiderBtn != null ? joinHiderBtn : hiderBtn;
    Button EffectiveJoinHunterBtn() => joinHunterBtn != null ? joinHunterBtn : hunterBtn;

    static void SetRoleButtonInteractable(Button btn, bool interactable)
    {
        if (btn != null) btn.interactable = interactable;
    }
    
    // ==================== 创建游戏 ====================
    void OpenCreatePanel()
    {
        _createPanelOpen = true;
        _joinPanelOpen = false;

        if (createPanel != null) createPanel.SetActive(true);
        if (joinPanel != null) joinPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        
        if (statusText != null)
            statusText.text = "🏠 选择身份以创建房间";

        SetRoleSelectVisible(true);
        SetRoleButtonInteractable(hiderBtn, true);
        SetRoleButtonInteractable(hunterBtn, true);
    }
    
    void CloseCreatePanel()
    {
        _createPanelOpen = false;
        if (createPanel != null) createPanel.SetActive(false);
        ShowMainMenu();
    }

    /// <summary>
    /// 创建面板 HIDER/HUNTER：创建房间 → 记住偏好身份 → 进入 GameScene 选角/练习。
    /// （此前两按钮只 Debug.Log，导致「点躲藏者没反应」。）
    /// </summary>
    void CreateRoomAs(PlayerRole preferredRole)
    {
        if (isCreatingRoom) return;

        if (preferredRole != PlayerRole.Hider && preferredRole != PlayerRole.Seeker)
        {
            Debug.LogWarning("[MainMenu] CreateRoomAs 收到无效身份");
            return;
        }

        if (netManager == null)
            netManager = FindObjectOfType<CustomNetworkManager>();
        if (discovery == null)
            discovery = FindObjectOfType<ManualDiscovery>();

        if (netManager == null)
        {
            SetStatus("❌ 找不到网络管理器", Color.red);
            return;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            SetStatus("⚠️ 已在房间中，请先离开", Color.yellow);
            return;
        }

        isCreatingRoom = true;
        string roleLabel = preferredRole == PlayerRole.Hider ? "躲藏者" : "抓捕者";
        SetStatus($"⏳ 正在以{roleLabel}创建房间...", Color.yellow);

        PlayerPrefs.SetInt(PrefPreferredRole, (int)preferredRole);
        PlayerPrefs.Save();

        string roomName = $"{System.Environment.MachineName}的房间";
        int maxPlayers = Mathf.Max(2, defaultMaxPlayers);

        try
        {
            if (GameContract.IsRoomBound)
            {
                Debug.Log($"[MainMenu] 契约创建房间：{roomName}，偏好身份={preferredRole}");
                GameContract.RoomCommands.CreateRoom(roomName, maxPlayers);
            }
            else
            {
                Debug.Log($"[MainMenu] 直连创建房间：{roomName}，偏好身份={preferredRole}");
                PlayerPrefs.SetString("RoomName", roomName);
                netManager.maxConnections = maxPlayers;
                netManager.StartHost();
                discovery?.StartBroadcasting();
            }
        }
        catch (System.Exception e)
        {
            isCreatingRoom = false;
            SetStatus($"❌ 创建失败：{e.Message}", Color.red);
            Debug.LogError($"[MainMenu] 创建房间异常：{e}");
            return;
        }

        if (!NetworkServer.active)
        {
            isCreatingRoom = false;
            SetStatus("❌ 创建失败：服务器未启动", Color.red);
            return;
        }

        SetStatus($"✅ 已创建，正在进入选角...", Color.green);
        StartCoroutine(EnterGameSceneAfterCreate());
    }

    IEnumerator EnterGameSceneAfterCreate()
    {
        // 等一帧，让 Host 本地玩家 / NetworkGameState 完成生成
        yield return null;
        yield return new WaitForSeconds(0.35f);

        if (netManager == null || string.IsNullOrEmpty(netManager.gameScene))
        {
            isCreatingRoom = false;
            SetStatus("❌ 无法进入游戏场景", Color.red);
            yield break;
        }

        Debug.Log($"[MainMenu] 进入场景：{netManager.gameScene}");
        netManager.ServerChangeScene(netManager.gameScene);
        isCreatingRoom = false;
    }

    void SetStatus(string msg, Color color)
    {
        if (statusText == null) return;
        statusText.text = msg;
        statusText.color = color;
    }
    
    // ==================== 设置 ====================
    void OpenSettingsPanel()
    {
        _joinPanelOpen = false;
        _createPanelOpen = false;

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