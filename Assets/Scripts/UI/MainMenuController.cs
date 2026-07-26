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

    [Header("玩家名称")]
    public TMP_InputField playerNameInput;  // 玩家名称输入框（放在主界面）
    [Tooltip("写名字面板；为空则从 playerNameInput 向上查找 NamePanel")]
    public GameObject namePanel;
    
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
    [Tooltip("创建房间时的默认最大人数")]
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
    
    [Header("提示")]
    public Image slotFullImage;  // SlotFull 提示图片
    
    private RoomConnectionState currentConnectionState = RoomConnectionState.Disconnected;
    private CustomNetworkManager netManager;
    private ManualDiscovery discovery;
    private bool _joinPanelOpen;
    private bool _createPanelOpen;
    
    private bool _waitingCreateEnter;
    private bool _roomEventsSubscribed;
    private bool isCreatingRoom;
    private Coroutine hideSlotFullCoroutine;
    private string currentPlayerName = "";
    
    void Start()
    {
        netManager = FindObjectOfType<CustomNetworkManager>();
        discovery = FindObjectOfType<ManualDiscovery>();

        // 初始隐藏 SlotFull 提示
        if (slotFullImage != null)
            slotFullImage.gameObject.SetActive(false);

        // ===== 加载玩家名称 =====
        LoadPlayerName();

        EnsureJoinUiRefs();
        
        // ===== 主菜单按钮（添加音效） =====
        if (joinGameBtn != null)
            joinGameBtn.onClick.AddListener(() => {
                PlayClick();
                OpenJoinPanel();
            });
        
        if (createGameBtn != null)
            createGameBtn.onClick.AddListener(() => {
                PlayClick();
                OpenCreatePanel();
            });
        
        if (settingsBtn != null)
            settingsBtn.onClick.AddListener(() => {
                PlayClick();
                OpenSettingsPanel();
            });
        
        if (quitGameBtn != null)
            quitGameBtn.onClick.AddListener(() => {
                PlayClick();
                QuitGame();
            });
        
        // ===== 加入面板按钮（添加音效） =====
        if (joinBtn != null)
            joinBtn.onClick.AddListener(() => {
                PlayClick();
                OnJoinClicked();
            });
        
        if (joinBackBtn != null)
            joinBackBtn.onClick.AddListener(() => {
                PlayClick();
                CloseJoinPanel();
            });

        if (roomCodeInput != null)
            roomCodeInput.onEndEdit.AddListener(OnRoomCodeEndEdit);
        
        // ===== 创建面板按钮（添加音效） =====
        if (createBackBtn != null)
            createBackBtn.onClick.AddListener(() => {
                PlayClick();
                CloseCreatePanel();
            });
        
        // ===== 身份选择按钮 =====
        WireRoleButton(hiderBtn, PlayerRole.Hider);
        WireRoleButton(hunterBtn, PlayerRole.Seeker);
        WireRoleButton(joinHiderBtn, PlayerRole.Hider);
        WireRoleButton(joinHunterBtn, PlayerRole.Seeker);
        
        // ===== 设置面板按钮（添加音效） =====
        if (settingsBackBtn != null)
            settingsBackBtn.onClick.AddListener(() => {
                PlayClick();
                CloseSettingsPanel();
            });
        
        // ===== 音量绑定 =====
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        
        // ===== 玩家名称输入监听 =====
        if (playerNameInput != null)
        {
            playerNameInput.onEndEdit.AddListener(OnPlayerNameChanged);
            playerNameInput.text = currentPlayerName;
        }
        
        StartCoroutine(EnsureRoomEventsSubscribed());
        ShowMainMenu();
        LoadSettings();
    }

    // ==================== 玩家名称管理 ====================
    
    void LoadPlayerName()
    {
        PlayerProfile.Load();
        currentPlayerName = PlayerProfile.PlayerName;

        if (playerNameInput != null)
            playerNameInput.text = currentPlayerName;

        Debug.Log($"📛 玩家名称已加载: {currentPlayerName}");
    }

    void OnPlayerNameChanged(string newName)
    {
        PlayerProfile.SetPlayerName(newName);
        currentPlayerName = PlayerProfile.PlayerName;
        if (playerNameInput != null)
            playerNameInput.text = currentPlayerName;

        Debug.Log($"📛 玩家名称已更新: {currentPlayerName}");
    }

    /// <summary>
    /// 获取当前玩家名称（供其他脚本使用）
    /// </summary>
    public string GetPlayerName()
    {
        return currentPlayerName;
    }

    // ==================== 音效播放（通过契约） ====================

    private void PlayClick()
    {
        if (GameContract.IsAudioBound)
        {
            GameContract.Audio.PlayClick();
        }
    }

    private void PlayHover()
    {
        if (GameContract.IsAudioBound)
        {
            GameContract.Audio.PlayHover();
        }
    }

    // ==================== SlotFull 提示 ====================
    private void ShowSlotFullMessage(bool show)
    {
        if (slotFullImage == null) return;
        
        slotFullImage.gameObject.SetActive(show);
        
        if (show)
        {
            if (hideSlotFullCoroutine != null)
                StopCoroutine(hideSlotFullCoroutine);
            hideSlotFullCoroutine = StartCoroutine(HideSlotFullImageDelayed());
        }
    }

    private IEnumerator HideSlotFullImageDelayed()
    {
        yield return new WaitForSeconds(3f);
        if (slotFullImage != null)
        {
            slotFullImage.gameObject.SetActive(false);
        }
        hideSlotFullCoroutine = null;
    }

    public void OnRoleButtonHover(PlayerRole role)
    {
        PlayHover();  // 悬停音效

        if (!GameContract.IsRoomBound || GameContract.RoomState == null) return;
        
        RoomInfo? foundRoom = GameContract.RoomState.FoundRoom;
        if (!foundRoom.HasValue) return;
        
        RoomInfo info = foundRoom.Value;
        RoleSlots projected = RoleSlots.ProjectForJoiner(
            info.currentPlayers, info.seekerCount, info.hiderCount);
        
        bool isFull = (role == PlayerRole.Hider && projected.HiderFull) ||
                      (role == PlayerRole.Seeker && projected.SeekerFull);
        
        ShowSlotFullMessage(isFull);
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

        EnsureNamePanelRef();
        SetRoleSelectVisible(false);
        SetNamePanelVisible(true);
    }

    void EnsureNamePanelRef()
    {
        if (namePanel != null) return;
        if (playerNameInput == null) return;

        Transform t = playerNameInput.transform;
        while (t != null)
        {
            if (t.name == "NamePanel")
            {
                namePanel = t.gameObject;
                return;
            }
            t = t.parent;
        }
    }

    void SetNamePanelVisible(bool visible)
    {
        EnsureNamePanelRef();
        if (namePanel != null)
            namePanel.SetActive(visible);
    }

    void SetRoleSelectVisible(bool visible)
    {
        if (roleSelectPanel == null) return;
        roleSelectPanel.SetActive(visible);
        // Join 流程选角时隐藏 Join/Back，避免与 HIDER/HUNTER 按钮重叠
        if (_joinPanelOpen)
            SetJoinActionButtonsVisible(!visible);
        if (visible)
        {
            // 选角时藏起写名字，并用独立 Canvas 强制盖在最上层
            SetNamePanelVisible(false);
            EnsureRoleSelectSortsOnTop();
            roleSelectPanel.transform.SetAsLastSibling();
            Debug.Log("[MainMenu] 身份选择面板已显示");
        }
    }

    void SetJoinActionButtonsVisible(bool visible)
    {
        if (joinBtn != null)
            joinBtn.gameObject.SetActive(visible);
        if (joinBackBtn != null)
            joinBackBtn.gameObject.SetActive(visible);
    }

    void EnsureRoleSelectSortsOnTop()
    {
        if (roleSelectPanel == null) return;

        Canvas canvas = roleSelectPanel.GetComponent<Canvas>();
        if (canvas == null)
            canvas = roleSelectPanel.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;

        if (roleSelectPanel.GetComponent<GraphicRaycaster>() == null)
            roleSelectPanel.AddComponent<GraphicRaycaster>();
    }

    void WireRoleButton(Button btn, PlayerRole role)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => OnSelectRoleClicked(role));
        
        var trigger = btn.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>();
        if (trigger == null)
            trigger = btn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        
        trigger.triggers.Clear();
        
        var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
        enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
        enterEntry.callback.AddListener((data) => OnRoleButtonHover(role));
        trigger.triggers.Add(enterEntry);
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
            Debug.LogWarning("[MainMenu] 房间契约仍未绑定");
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
            RoomConnectionState.Disconnected => "📋 Disconnected",
            RoomConnectionState.Connecting => "⏳ Connecting...",
            RoomConnectionState.InRoom => "✅ Joined room",
            RoomConnectionState.Failed => "❌ Connection failed, please retry",
            _ => "📋 Disconnected"
        };
        
        if (statusText != null)
            statusText.text = statusMsg;

        if (state == RoomConnectionState.InRoom && GameContract.IsRoomBound)
        {
            string code = GameContract.RoomState.CurrentRoomCode;
            if (!string.IsNullOrEmpty(code) && statusText != null)
                statusText.text = $"✅ In room, code: {code}";

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
                $"✅ Found room \"{info.roomName}\"  code {info.roomCode}\n" +
                $"Tap HIDER / HUNTER (hiders {projected.hiderCount}/{projected.hiderMax}, " +
                $"hunters {projected.seekerCount}/{projected.seekerMax})";
        }
    }
    
    void OnRoomError(RoomError error)
    {
        if (error.op == RoomOp.Create)
            isCreatingRoom = false;

        string errorMsg = error.reason switch
        {
            RoomErrorReason.Timeout => "⏰ Operation timed out",
            RoomErrorReason.RoomNotFound => "🔍 Room not found",
            RoomErrorReason.RoomFull => "👥 Room is full",
            RoomErrorReason.ConnectionFailed => "🔌 Network connection failed",
            RoomErrorReason.AlreadyInRoom => "⚠️ Already in a room",
            RoomErrorReason.SlotFull => error.message == "Seeker"
                ? "⚠️ SEEKER SLOT FULL!"
                : "⚠️ HIDER SLOT FULL!",
            RoomErrorReason.RoleNotSelected => "⚠️ Please select a role first",
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
            statusText.text = "🎮 Welcome to Peekaboo!";

        SetRoleSelectVisible(false);
        SetNamePanelVisible(true);
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

        SetNamePanelVisible(false);
        SetJoinActionButtonsVisible(true);
        
        if (statusText != null)
            statusText.text = "📋 Enter room code, tap JOIN, then select a role";

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
            if (statusText != null) statusText.text = "❌ Room service not ready";
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
            if (statusText != null) statusText.text = "⚠️ Please tap HIDER or HUNTER first";
            return;
        }

        TryJoinFoundRoomNow();
    }

    void TryJoinFoundRoomNow()
    {
        if (!GameContract.IsRoomBound) return;
        if (!GameContract.RoomState.FoundRoom.HasValue) return;
        if (GameContract.RoomState.PreferredRole == PlayerRole.None) return;

        if (statusText != null) statusText.text = "⏳ Joining room...";
        GameContract.RoomCommands.JoinFoundRoom();
    }

    void TryFindRoomByCodeInput()
    {
        if (!GameContract.IsRoomBound)
        {
            if (statusText != null) statusText.text = "❌ Room service not ready";
            return;
        }

        string code = roomCodeInput != null ? roomCodeInput.text : string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            if (statusText != null) statusText.text = "⚠️ Please enter a room code";
            return;
        }

        if (statusText != null) statusText.text = $"⏳ Looking up code {code.Trim().ToUpperInvariant()}...";
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
        if (_createPanelOpen)
        {
            if (GameContract.IsRoomBound)
                GameContract.RoomCommands.TrySelectRoleBeforeEnter(role);
            CreateRoomAs(role);
            return;
        }

        if (!GameContract.IsRoomBound)
        {
            if (statusText != null) statusText.text = "❌ Room service not ready";
            return;
        }

        if (_joinPanelOpen && GameContract.RoomState.FoundRoom.HasValue)
        {
            RoomInfo found = GameContract.RoomState.FoundRoom.Value;
            RoleSlots projected = RoleSlots.ProjectForJoiner(
                found.currentPlayers, found.seekerCount, found.hiderCount);
            if (role == PlayerRole.Hider && projected.HiderFull)
            {
                ShowSlotFullMessage(true);
                return;
            }
            if (role == PlayerRole.Seeker && projected.SeekerFull)
            {
                ShowSlotFullMessage(true);
                return;
            }
        }

        if (!GameContract.RoomCommands.TrySelectRoleBeforeEnter(role))
            return;

        ShowSlotFullMessage(false);

        string name = role == PlayerRole.Hider ? "Hider" : "Hunter";
        if (statusText != null)
            statusText.text = $"✅ Selected: {name}";

        if (_createPanelOpen)
            BeginCreateRoomAfterRoleSelected();
        else if (_joinPanelOpen && GameContract.RoomState.FoundRoom.HasValue)
            TryJoinFoundRoomNow();
    }

    void BeginCreateRoomAfterRoleSelected()
    {
        if (!GameContract.IsRoomBound) return;

        // ===== 使用自定义玩家名称作为房间名 =====
        string roomName = $"{currentPlayerName}'s Room";
        if (statusText != null)
            statusText.text = $"⏳ Creating room \"{roomName}\"...";

        _waitingCreateEnter = true;
        GameContract.RoomCommands.CreateRoom(roomName, 4);
    }

    IEnumerator DelayedEnterGameScene()
    {
        _waitingCreateEnter = false;
        string code = GameContract.IsRoomBound ? GameContract.RoomState.CurrentRoomCode : string.Empty;
        if (statusText != null && !string.IsNullOrEmpty(code))
            statusText.text = $"✅ Created successfully! Code: {code}, entering...";

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

        SetNamePanelVisible(false);
        
        if (statusText != null)
            statusText.text = "🏠 Select a role to create a room";

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
            SetStatus("❌ Network manager not found", Color.red);
            return;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            SetStatus("⚠️ Already in a room, please leave first", Color.yellow);
            return;
        }

        isCreatingRoom = true;
        string roleLabel = preferredRole == PlayerRole.Hider ? "Hider" : "Hunter";
        SetStatus($"⏳ Creating room as {roleLabel}...", Color.yellow);

        PlayerPrefs.SetInt(PrefPreferredRole, (int)preferredRole);
        PlayerPrefs.Save();

        // ===== 使用自定义玩家名称作为房间名 =====
        string roomName = $"{currentPlayerName}'s Room";
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
            SetStatus($"❌ Create failed: {e.Message}", Color.red);
            Debug.LogError($"[MainMenu] 创建房间异常：{e}");
            return;
        }

        if (!NetworkServer.active)
        {
            isCreatingRoom = false;
            SetStatus("❌ Create failed: server not started", Color.red);
            return;
        }

        SetStatus($"✅ Created, entering role select...", Color.green);
        StartCoroutine(EnterGameSceneAfterCreate());
    }

    IEnumerator EnterGameSceneAfterCreate()
    {
        yield return null;
        yield return new WaitForSeconds(0.35f);

        if (netManager == null || string.IsNullOrEmpty(netManager.gameScene))
        {
            isCreatingRoom = false;
            SetStatus("❌ Failed to enter game scene", Color.red);
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

        SetNamePanelVisible(false);
        SetRoleSelectVisible(false);
        
        if (statusText != null)
            statusText.text = "⚙️ Game Settings";
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
        float master = PlayerProfile.MasterVolume;
        float music = PlayerProfile.MusicVolume;
        float sfx = PlayerProfile.SFXVolume;

        if (masterVolumeSlider != null) masterVolumeSlider.value = master;
        if (musicVolumeSlider != null) musicVolumeSlider.value = music;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = sfx;

        // ===== 通过契约同步音量 =====
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
}