using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class LobbyRoomController : MonoBehaviour
{
    [Header("UI")]
    public GameObject lobbyUI;
    public GameObject gameUI;
    public Transform playerListParent;
    public GameObject playerItemPrefab;
    
    [Header("身份选择")]
    public GameObject roleSelectPanel;
    public Button hiderBtn;
    public Button randomBtn;
    public Button seekerBtn;
    public TextMeshProUGUI hiderStatusText;
    public TextMeshProUGUI seekerStatusText;
    public TextMeshProUGUI statusText;
    [Tooltip("可选：单独展示房间短码；为空则拼进 statusText")]
    public TextMeshProUGUI roomCodeText;
    
    [Header("按钮")]
    public Button readyBtn;
    public Button startGameBtn;
    public Button reselectBtn;
    
    [Header("角色UI")]
    public GameObject hiderUI;
    public GameObject seekerUI;
    
    private List<GameObject> playerItems = new List<GameObject>();
    private RoleSlots roleSlots;
    
    private PlayerRole selectedRole = PlayerRole.None;
    private bool isReady = false;
    private bool gameStarted = false;
    private bool hasSelectedRole = false;
    private bool isLocked = false;
    private bool isHost = false;
    private bool preferredRoleConsumed;
    
    private bool subscribedToRoleSlots;
    
    private string localPlayerName = "";
    private int localConnectionId = -1;
    private CustomNetworkManager networkManager;
    
    void Start()
    {
        networkManager = FindObjectOfType<CustomNetworkManager>();
        
        if (NetworkServer.active)
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                localConnectionId = conn.connectionId;
                localPlayerName = GameConstants.DefaultPlayerName;
                if (conn.identity != null)
                {
                    RoomPlayer hostRp = conn.identity.GetComponent<RoomPlayer>();
                    if (hostRp != null && !string.IsNullOrEmpty(hostRp.playerName))
                        localPlayerName = hostRp.playerName;
                }
                Debug.Log($"✅ 房主连接 ID: {localConnectionId}");
                break;
            }
        }
        else if (NetworkClient.active && NetworkClient.localPlayer != null)
        {
            RoomPlayer rp = NetworkClient.localPlayer.GetComponent<RoomPlayer>();
            if (rp != null)
            {
                localConnectionId = rp.connectionId;
                localPlayerName = string.IsNullOrEmpty(rp.playerName)
                    ? GameConstants.DefaultPlayerName
                    : rp.playerName;
                Debug.Log($"✅ 客户端连接 ID: {localConnectionId}");
            }
        }
        
        if (localConnectionId == -1)
        {
            foreach (var player in GetSceneRoomPlayers())
            {
                if (player != null && player.isLocalPlayer)
                {
                    localConnectionId = player.connectionId;
                    localPlayerName = string.IsNullOrEmpty(player.playerName)
                        ? GameConstants.DefaultPlayerName
                        : player.playerName;
                    Debug.Log($"✅ 通过场景 RoomPlayer 找到连接 ID: {localConnectionId}");
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(localPlayerName))
            localPlayerName = GameConstants.DefaultPlayerName;
        
        isHost = (localConnectionId == 0);
        Debug.Log($"{(isHost ? "👑 你是房主" : "👤 你是普通玩家")}，连接ID: {localConnectionId}");
        
        SubscribeEvents();

        if (GameContract.IsBound)
            roleSlots = GameContract.State.Slots;
        else
            CalculateRoleSlots(Mathf.Max(1, GetSceneRoomPlayers().Length));
        
        if (hiderBtn != null)
            hiderBtn.onClick.AddListener(() => SelectRole(PlayerRole.Hider));
        if (randomBtn != null)
            randomBtn.onClick.AddListener(SelectRandomRole);
        if (seekerBtn != null)
            seekerBtn.onClick.AddListener(() => SelectRole(PlayerRole.Seeker));
        if (readyBtn != null)
            readyBtn.onClick.AddListener(ToggleReady);
        if (startGameBtn != null)
            startGameBtn.onClick.AddListener(HostStartGame);
        
        if (reselectBtn != null)
        {
            reselectBtn.onClick.AddListener(ReselectRole);
            reselectBtn.gameObject.SetActive(false);
        }
        
        ShowLobbyUI();
        
        UpdateRoleButtons();
        UpdatePlayerList();
        RefreshRoomCodeDisplay();
        StartCoroutine(DelayedContractRefresh());
        StartCoroutine(TryApplyPreferredRole());
    }

    IEnumerator TryApplyPreferredRole()
    {
        if (!PlayerPrefs.HasKey(MainMenuController.PrefPreferredRole))
            yield break;

        PlayerRole preferred = (PlayerRole)PlayerPrefs.GetInt(MainMenuController.PrefPreferredRole, (int)PlayerRole.None);
        PlayerPrefs.DeleteKey(MainMenuController.PrefPreferredRole);
        PlayerPrefs.Save();

        if (preferred != PlayerRole.Hider && preferred != PlayerRole.Seeker)
            yield break;

        for (int i = 0; i < 20; i++)
        {
            yield return new WaitForSeconds(0.15f);
            TrySubscribeGameEvents();

            if (GetLocalRoomPlayer() == null)
                continue;

            if (GameContract.IsBound || NetworkServer.active)
            {
                Debug.Log($"[LobbyRoomController] 应用创建时偏好身份：{preferred}");
                SelectRole(preferred);
                yield break;
            }
        }

        Debug.LogWarning("[LobbyRoomController] 未能自动应用偏好身份（本地玩家/契约未就绪）");
    }

    IEnumerator DelayedContractRefresh()
    {
        for (int i = 0; i < 8; i++)
        {
            yield return new WaitForSeconds(0.25f);
            TrySubscribeGameEvents();
            RefreshRoomCodeDisplay();
            SyncRoleFromPreferredOrPlayer();

            if (GameContract.IsBound)
            {
                roleSlots = GameContract.State.Slots;
                UpdateRoleButtons();
                UpdatePlayerList();

                if (GameContract.State.Phase != GamePhase.Waiting)
                    ShowGameUI();
            }
        }
    }

    void TrySubscribeGameEvents()
    {
        if (subscribedToRoleSlots || !GameContract.IsBound) return;

        GameContract.Events.OnRoleSlotsChanged += OnRoleSlotsChanged;
        GameContract.Events.OnPhaseChanged += OnPhaseChanged;
        GameContract.Events.OnCommandRejected += OnCommandRejected;
        subscribedToRoleSlots = true;

        if (GameContract.State.Phase != GamePhase.Waiting)
            ShowGameUI();
    }

    void EnsureRoleUiRefs()
    {
        if (hiderUI == null)
        {
            Transform t = transform.Find("HiderUI");
            if (t == null)
            {
                var go = GameObject.Find("HiderUI");
                if (go != null) hiderUI = go;
            }
            else hiderUI = t.gameObject;
        }

        if (seekerUI == null)
        {
            Transform t = transform.Find("SeekerUI");
            if (t == null)
            {
                var go = GameObject.Find("SeekerUI");
                if (go != null) seekerUI = go;
            }
            else seekerUI = t.gameObject;
        }
    }
    
    void CalculateRoleSlots(int totalPlayers)
    {
        ApplyRoleMaxForPlayerCount(totalPlayers);
        roleSlots.seekerCount = 0;
        roleSlots.hiderCount = 0;
        
        Debug.Log($"📊 名额分配: 躲藏者 {roleSlots.hiderMax} 人, 抓捕者 {roleSlots.seekerMax} 人 (总人数 {totalPlayers})");
    }

    void ApplyRoleMaxForPlayerCount(int totalPlayers)
    {
        if (totalPlayers < 2)
        {
            roleSlots.seekerMax = 1;
            roleSlots.hiderMax = 1;
        }
        else
        {
            int seekerMax = Mathf.Max(1, totalPlayers / 3);
            int hiderMax = totalPlayers - seekerMax;
            if (hiderMax < 1) hiderMax = 1;
            
            roleSlots.seekerMax = seekerMax;
            roleSlots.hiderMax = hiderMax;
        }
    }

    RoomPlayer[] GetSceneRoomPlayers()
    {
        return FindObjectsOfType<RoomPlayer>();
    }

    RoomPlayer GetLocalRoomPlayer()
    {
        if (NetworkClient.active && NetworkClient.localPlayer != null)
        {
            RoomPlayer rp = NetworkClient.localPlayer.GetComponent<RoomPlayer>();
            if (rp != null) return rp;
        }

        foreach (var player in GetSceneRoomPlayers())
        {
            if (player != null && player.isLocalPlayer)
                return player;
        }

        return null;
    }

    bool HasValidRoleComposition()
    {
        RoomPlayer[] players = GetSceneRoomPlayers();
        if (players.Length < 2) return false;

        int hiderCount = 0;
        int seekerCount = 0;

        foreach (var player in players)
        {
            if (player == null) continue;
            if (player.Role == PlayerRole.Hider) hiderCount++;
            else if (player.Role == PlayerRole.Seeker) seekerCount++;
        }

        return hiderCount >= 1 && seekerCount >= 1;
    }

    bool AreAllPlayersReady()
    {
        RoomPlayer[] players = GetSceneRoomPlayers();
        if (players.Length < 2) return false;

        foreach (var player in players)
        {
            if (player == null) return false;
            if (player.Role == PlayerRole.None || !player.isReady)
                return false;
        }

        return true;
    }
    
    bool CanStartGame()
    {
        return HasValidRoleComposition() && AreAllPlayersReady();
    }
    
    void SubscribeEvents()
    {
        try
        {
            if (GameContract.IsRoomBound)
                GameContract.RoomEvents.OnConnectionStateChanged += OnConnectionStateChanged;

            if (GameContract.IsBound)
            {
                GameContract.Events.OnRoleSlotsChanged += OnRoleSlotsChanged;
                GameContract.Events.OnPhaseChanged += OnPhaseChanged;
                GameContract.Events.OnCommandRejected += OnCommandRejected;
                subscribedToRoleSlots = true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LobbyRoomController] 订阅契约事件失败：{e.Message}");
        }
    }
    
    void OnDestroy()
    {
        try
        {
            if (GameContract.IsRoomBound)
                GameContract.RoomEvents.OnConnectionStateChanged -= OnConnectionStateChanged;

            if (GameContract.IsBound)
            {
                GameContract.Events.OnRoleSlotsChanged -= OnRoleSlotsChanged;
                GameContract.Events.OnPhaseChanged -= OnPhaseChanged;
                GameContract.Events.OnCommandRejected -= OnCommandRejected;
            }
        }
        catch { }
    }
    
    void ShowLobbyUI()
    {
        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (gameUI != null) gameUI.SetActive(false);
        gameStarted = false;

        RefreshRoomCodeDisplay();
        SyncRoleFromPreferredOrPlayer();

        if (!hasSelectedRole)
        {
            if (roleSelectPanel != null) roleSelectPanel.SetActive(true);
            if (reselectBtn != null) reselectBtn.gameObject.SetActive(false);
            UpdateRoleUI(PlayerRole.None);
            UpdateStatusText("SELECT YOUR ROLE");
        }

        UpdateButtonVisibility();
        RefreshRoomCodeDisplay();

        // ===== 切换到菜单背景音乐 =====
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.SwitchToMusic(SoundManager.Instance.globalMusic);
        }
    }

    void SyncRoleFromPreferredOrPlayer()
    {
        if (gameStarted || isLocked) return;

        PlayerRole resolved = PlayerRole.None;

        RoomPlayer localPlayer = GetLocalRoomPlayer();
        if (localPlayer != null && localPlayer.Role != PlayerRole.None)
            resolved = localPlayer.Role;
        else if (GameContract.IsBound &&
                 GameContract.State.LocalPlayer != null &&
                 GameContract.State.LocalPlayer.Role != PlayerRole.None)
            resolved = GameContract.State.LocalPlayer.Role;

        PlayerRole preferred = PlayerRole.None;
        if (GameContract.IsRoomBound)
            preferred = GameContract.RoomState.PreferredRole;

        if (resolved == PlayerRole.None && preferred != PlayerRole.None && !preferredRoleConsumed)
        {
            preferredRoleConsumed = true;
            if (GameContract.IsBound)
                GameContract.Commands.SelectRole(preferred);
            resolved = preferred;
        }
        else if (resolved == PlayerRole.None &&
                 preferred != PlayerRole.None &&
                 preferredRoleConsumed &&
                 hasSelectedRole &&
                 selectedRole == preferred &&
                 GameContract.IsBound)
        {
            if (localPlayer != null && localPlayer.Role == PlayerRole.None)
                GameContract.Commands.SelectRole(preferred);
            resolved = preferred;
        }

        if (resolved == PlayerRole.None)
            return;

        bool alreadyUiSelected = hasSelectedRole && selectedRole == resolved;

        selectedRole = resolved;
        hasSelectedRole = true;

        if (roleSelectPanel != null)
            roleSelectPanel.SetActive(false);
        if (reselectBtn != null)
            reselectBtn.gameObject.SetActive(true);

        if (gameStarted)
            UpdateRoleUI(resolved);
        else
            UpdateRoleUI(PlayerRole.None);

        UpdateRoleButtons();
        UpdateButtonVisibility();
        if (!alreadyUiSelected)
            UpdateStatusText($"✅ SELECTED: {GetRoleDisplayName(resolved)}");
        RefreshRoomCodeDisplay();
    }

    string GetCurrentRoomCode()
    {
        if (!GameContract.IsRoomBound) return string.Empty;
        return GameContract.RoomState.CurrentRoomCode ?? string.Empty;
    }

    void RefreshRoomCodeDisplay()
    {
        string code = GetCurrentRoomCode();

        if (roomCodeText != null)
        {
            if (string.IsNullOrEmpty(code))
            {
                roomCodeText.gameObject.SetActive(false);
            }
            else
            {
                roomCodeText.gameObject.SetActive(true);
                roomCodeText.text = $"ROOM CODE: {code}";
            }
        }
    }
    
    void ShowGameUI()
    {
        EnsureRoleUiRefs();

        if (lobbyUI != null) lobbyUI.SetActive(false);
        if (gameUI != null) gameUI.SetActive(true);
        if (roleSelectPanel != null) roleSelectPanel.SetActive(false);
        if (reselectBtn != null) reselectBtn.gameObject.SetActive(false);
        if (readyBtn != null) readyBtn.gameObject.SetActive(false);
        if (startGameBtn != null) startGameBtn.gameObject.SetActive(false);

        PlayerRole role = ResolveLocalRole();
        selectedRole = role;
        hasSelectedRole = role != PlayerRole.None;
        UpdateRoleUI(role);
        gameStarted = true;

        // ===== 切换到游戏背景音乐 =====
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.SwitchToMusic(SoundManager.Instance.gameMusic);
        }
    }

    PlayerRole ResolveLocalRole()
    {
        if (selectedRole != PlayerRole.None)
            return selectedRole;

        RoomPlayer localPlayer = GetLocalRoomPlayer();
        if (localPlayer != null && localPlayer.Role != PlayerRole.None)
            return localPlayer.Role;

        if (GameContract.IsBound && GameContract.State.LocalPlayer != null)
            return GameContract.State.LocalPlayer.Role;

        if (GameContract.IsRoomBound && GameContract.RoomState.PreferredRole != PlayerRole.None)
            return GameContract.RoomState.PreferredRole;

        return PlayerRole.None;
    }
    
    void UpdateButtonVisibility()
    {
        if (readyBtn != null)
            readyBtn.gameObject.SetActive(!isHost);

        if (startGameBtn != null)
            startGameBtn.gameObject.SetActive(isHost);

        UpdateStartGameButtonInteractable();
    }
    
    void UpdateStartGameButtonInteractable()
    {
        if (startGameBtn == null || !isHost) return;
        
        bool canStart = CanStartGame();
        startGameBtn.interactable = canStart;
        
        if (canStart)
        {
            UpdateStatusText("✅ ALL PLAYERS READY! CLICK START");
        }
        else if (!HasValidRoleComposition())
        {
            int total = GetSceneRoomPlayers().Length;
            UpdateStatusText($"⏳ NEED 1 HIDER AND 1 SEEKER (CURRENT: {total})");
        }
        else
        {
            UpdateStatusText(isHost
                ? "⏳ WAITING FOR PLAYERS TO READY..."
                : "⏳ WAITING FOR ALL PLAYERS TO READY...");
        }
    }

    public void NotifyPlayerReadyChanged(RoomPlayer player)
    {
        if (player != null && player.isLocalPlayer)
        {
            isReady = player.isReady;
            isLocked = player.isReady;

            if (readyBtn != null && !isHost)
            {
                TextMeshProUGUI btnText = readyBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = isReady ? "READY ✓" : "READY";
                    btnText.color = isReady ? Color.green : Color.white;
                }
            }

            if (reselectBtn != null)
                reselectBtn.gameObject.SetActive(hasSelectedRole);

            if (isHost)
                UpdateStatusText(isReady ? "✅ AUTO-READY, WAITING FOR OTHERS..." : "CANCELED READY");
            else
                UpdateStatusText(isReady ? "✅ READY! WAITING FOR HOST..." : "CANCELED READY");
        }

        UpdatePlayerList();
        UpdateRoleButtons();
        UpdateStartGameButtonInteractable();
    }
    
    public void UpdatePlayerList()
    {
        foreach (var item in playerItems)
        {
            Destroy(item);
        }
        playerItems.Clear();
        
        RoomPlayer[] players = GetSceneRoomPlayers();
        
        foreach (var player in players)
        {
            if (player == null) continue;
            
            GameObject item = Instantiate(playerItemPrefab, playerListParent);
            TextMeshProUGUI text = item.GetComponent<TextMeshProUGUI>();
            if (text != null)
            {
                bool isLocal = (player.connectionId == localConnectionId);
                string roleName = GetRoleDisplayName(player.Role);
                string readyMark = player.isReady ? " ✅" : "";
                string localMark = isLocal ? " (YOU)" : "";
                
                text.text = $"{player.playerName}{localMark} - {roleName}{readyMark}";
                text.color = isLocal ? Color.yellow : Color.white;
            }
            playerItems.Add(item);
        }
        
        ApplyRoleMaxForPlayerCount(players.Length);
        UpdateRoleButtons();
        UpdateStartGameButtonInteractable();
    }
    
    string GetRoleDisplayName(PlayerRole role)
    {
        switch (role)
        {
            case PlayerRole.Hider: return "HIDER";
            case PlayerRole.Seeker: return "SEEKER";
            default: return "NONE";
        }
    }
    
    void UpdateRoleCounts()
    {
        if (GameContract.IsBound)
        {
            roleSlots = GameContract.State.Slots;
            return;
        }

        int hiderCount = 0;
        int seekerCount = 0;
        
        foreach (var player in GetSceneRoomPlayers())
        {
            if (player == null) continue;
            if (player.Role == PlayerRole.Hider) hiderCount++;
            else if (player.Role == PlayerRole.Seeker) seekerCount++;
        }
        
        roleSlots.hiderCount = hiderCount;
        roleSlots.seekerCount = seekerCount;
    }
    
    void SelectRole(PlayerRole role)
    {
        Debug.Log($"🎯 SELECT ROLE: {role}");
        
        if (hasSelectedRole || isLocked)
        {
            UpdateStatusText(isLocked ? "⚠️ READY, CANNOT CHANGE ROLE" : "⚠️ ROLE ALREADY SELECTED, CLICK RESELECT");
            return;
        }
        
        UpdateRoleCounts();
        
        if (role == PlayerRole.Hider && roleSlots.HiderFull)
        {
            UpdateStatusText("⚠️ HIDER SLOT FULL!");
            return;
        }
        if (role == PlayerRole.Seeker && roleSlots.SeekerFull)
        {
            UpdateStatusText("⚠️ SEEKER SLOT FULL!");
            return;
        }
        
        selectedRole = role;
        hasSelectedRole = true;
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.SelectRole(role);
            UpdateStatusText($"✅ SELECTED: {GetRoleDisplayName(role)} (WAITING FOR SERVER)");
        }
        else
        {
            ApplyRoleOnExistingPlayer(role);
            UpdateStatusText($"✅ SELECTED: {GetRoleDisplayName(role)}");
        }
        
        if (roleSelectPanel != null)
            roleSelectPanel.SetActive(false);
        
        if (reselectBtn != null)
            reselectBtn.gameObject.SetActive(true);
        
        if (gameStarted)
            UpdateRoleUI(role);
        else
            UpdateRoleUI(PlayerRole.None);
        
        UpdatePlayerList();
        UpdateRoleCounts();
        UpdateRoleButtons();
        UpdateStartGameButtonInteractable();
    }
    
    void UpdateRoleUI(PlayerRole role)
    {
        EnsureRoleUiRefs();
        if (hiderUI == null && seekerUI == null)
        {
            Debug.LogWarning("[LobbyRoomController] GameScene 未绑定 HiderUI/SeekerUI");
            return;
        }
        
        if (role == PlayerRole.Hider)
        {
            if (hiderUI != null) hiderUI.SetActive(true);
            if (seekerUI != null) seekerUI.SetActive(false);
            Debug.Log("[LobbyRoomController] GameScene 显示 HiderUI");
        }
        else if (role == PlayerRole.Seeker)
        {
            if (hiderUI != null) hiderUI.SetActive(false);
            if (seekerUI != null) seekerUI.SetActive(true);
            Debug.Log("[LobbyRoomController] GameScene 显示 SeekerUI");
        }
        else
        {
            if (hiderUI != null) hiderUI.SetActive(false);
            if (seekerUI != null) seekerUI.SetActive(false);
        }
    }
    
    void ApplyRoleOnExistingPlayer(PlayerRole role)
    {
        if (networkManager == null)
        {
            Debug.LogError("❌ networkManager 为空！");
            return;
        }

        NetworkConnectionToClient localConn = null;
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn.connectionId == localConnectionId)
            {
                localConn = conn;
                break;
            }
        }

        if (localConn == null)
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                localConn = conn;
                break;
            }
        }

        if (localConn == null)
        {
            Debug.LogError("❌ 找不到任何连接！");
            return;
        }

        networkManager.SpawnPlayerRole(localConn, role);
        UpdateRoleUI(role);
    }

    void ReselectRole()
    {
        if (!hasSelectedRole)
            return;

        Debug.Log("🔄 RESELECTING ROLE");

        preferredRoleConsumed = true;
        selectedRole = PlayerRole.None;
        hasSelectedRole = false;
        isReady = false;
        isLocked = false;
        
        UpdateRoleUI(PlayerRole.None);
        
        if (GameContract.IsBound)
            GameContract.Commands.SelectRole(PlayerRole.None);
        else
        {
            foreach (var player in GetSceneRoomPlayers())
            {
                if (player.connectionId == localConnectionId)
                {
                    player.SetRole(PlayerRole.None);
                    player.isReady = false;
                    break;
                }
            }
        }
        
        if (roleSelectPanel != null)
            roleSelectPanel.SetActive(true);
        
        if (reselectBtn != null)
            reselectBtn.gameObject.SetActive(false);
        
        if (readyBtn != null && !isHost)
        {
            TextMeshProUGUI btnText = readyBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null)
            {
                btnText.text = "READY";
                btnText.color = Color.white;
            }
        }
        
        UpdateStatusText("SELECT YOUR ROLE");
        UpdateRoleButtons();
        UpdatePlayerList();
        RefreshRoomCodeDisplay();
        if (!GameContract.IsBound)
            UpdateRoleCounts();
        
        Debug.Log("✅ 已重置，可以重新选择身份");
    }
    
    void SelectRandomRole()
    {
        Debug.Log("🎲 RANDOM ROLE");
        
        if (hasSelectedRole || isLocked)
        {
            UpdateStatusText(isLocked ? "⚠️ READY, CANNOT CHANGE" : "⚠️ ALREADY SELECTED, CLICK RESELECT");
            return;
        }
        
        UpdateRoleCounts();
        
        List<PlayerRole> available = new List<PlayerRole>();
        if (!roleSlots.HiderFull) available.Add(PlayerRole.Hider);
        if (!roleSlots.SeekerFull) available.Add(PlayerRole.Seeker);
        
        if (available.Count == 0)
        {
            UpdateStatusText("⚠️ ALL ROLES FULL!");
            return;
        }
        
        PlayerRole role = available[Random.Range(0, available.Count)];
        SelectRole(role);
    }
    
    void ToggleReady()
    {
        if (!hasSelectedRole)
        {
            UpdateStatusText("⚠️ PLEASE SELECT A ROLE FIRST!");
            return;
        }

        RoomPlayer localPlayer = GetLocalRoomPlayer();
        if (localPlayer == null)
        {
            UpdateStatusText("⚠️ LOCAL PLAYER NOT FOUND");
            return;
        }

        localPlayer.CmdToggleReady();
    }
    
    void HostStartGame()
    {
        if (!isHost)
        {
            UpdateStatusText("⚠️ ONLY HOST CAN START THE GAME!");
            return;
        }
        
        if (!CanStartGame())
        {
            if (!HasValidRoleComposition())
                UpdateStatusText("⚠️ NEED AT LEAST 1 HIDER AND 1 SEEKER!");
            else
                UpdateStatusText("⚠️ ALL PLAYERS MUST SELECT ROLE AND READY!");
            return;
        }
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.HostStartGame();
            UpdateStatusText("🚀 STARTING GAME...");
            ShowGameUI();
        }
        else
        {
            StartGameLocal();
        }
    }
    
    void StartGameLocal()
    {
        gameStarted = true;
        ShowGameUI();
        
        GameManager gm = FindObjectOfType<GameManager>();
        if (gm != null)
        {
            gm.StartGame();
        }
    }
    
    void OnConnectionStateChanged(RoomConnectionState state)
    {
        if (state == RoomConnectionState.InRoom)
        {
            RefreshRoomCodeDisplay();
            SyncRoleFromPreferredOrPlayer();
            if (!hasSelectedRole)
                UpdateStatusText("✅ JOINED ROOM");
        }
        else if (state == RoomConnectionState.Failed)
        {
            UpdateStatusText("❌ CONNECTION FAILED");
        }
        else if (state == RoomConnectionState.Connecting)
        {
            UpdateStatusText("⏳ CONNECTING...");
        }
        else if (state == RoomConnectionState.Disconnected)
        {
            UpdateStatusText("📋 DISCONNECTED");
        }
    }
    
    void OnRoleSlotsChanged(RoleSlots slots)
    {
        roleSlots = slots;
        SyncRoleFromPreferredOrPlayer();
        UpdateRoleButtons();
        UpdatePlayerList();
        UpdateStartGameButtonInteractable();
        if (!hasSelectedRole)
            UpdateStatusText($"👥 {slots.hiderCount + slots.seekerCount} PLAYERS SELECTED ROLE");
        RefreshRoomCodeDisplay();
    }

    void OnCommandRejected(CommandRejected rejected)
    {
        if (rejected.command != GameCommandType.SelectRole) return;

        if (rejected.reason == RejectReason.RoleFull)
        {
            UpdateStatusText(selectedRole == PlayerRole.Seeker
                ? "⚠️ SEEKER SLOT FULL!"
                : "⚠️ HIDER SLOT FULL!");
            hasSelectedRole = false;
            selectedRole = PlayerRole.None;
            UpdateRoleButtons();
            return;
        }

        UpdateStatusText($"⚠️ ROLE SELECTION FAILED: {rejected.reason}");
    }
    
    void OnPhaseChanged(GamePhase phase, float duration)
    {
        if (phase == GamePhase.Waiting)
            return;

        ShowGameUI();
    }
    
    void UpdateRoleButtons()
    {
        if (roleSlots.hiderMax == 0) roleSlots.hiderMax = 1;
        if (roleSlots.seekerMax == 0) roleSlots.seekerMax = 1;
        
        bool canSelect = !hasSelectedRole && !isLocked;
        
        if (hiderBtn != null)
        {
            hiderBtn.interactable = (canSelect && !roleSlots.HiderFull);
        }
        if (hiderStatusText != null)
        {
            hiderStatusText.text = $"🟢 HIDER ({roleSlots.hiderCount}/{roleSlots.hiderMax})";
            hiderStatusText.color = roleSlots.HiderFull ? Color.gray : Color.green;
            if (selectedRole == PlayerRole.Hider) hiderStatusText.text += " ← YOU";
        }
        
        if (seekerBtn != null)
        {
            seekerBtn.interactable = (canSelect && !roleSlots.SeekerFull);
        }
        if (seekerStatusText != null)
        {
            seekerStatusText.text = $"🔴 SEEKER ({roleSlots.seekerCount}/{roleSlots.seekerMax})";
            seekerStatusText.color = roleSlots.SeekerFull ? Color.gray : Color.red;
            if (selectedRole == PlayerRole.Seeker) seekerStatusText.text += " ← YOU";
        }
        
        if (randomBtn != null)
        {
            randomBtn.interactable = (canSelect && (!roleSlots.HiderFull || !roleSlots.SeekerFull));
        }
        
        if (readyBtn != null && !isHost)
        {
            readyBtn.interactable = hasSelectedRole;
        }
        
        UpdateButtonVisibility();
    }
    
    void UpdateStatusText(string msg)
    {
        if (statusText == null)
        {
            var found = GameObject.Find("StatusText");
            if (found != null)
                statusText = found.GetComponent<TextMeshProUGUI>();
        }

        if (statusText == null)
        {
            Debug.Log($"[LobbyStatus] {msg}");
            RefreshRoomCodeDisplay();
            return;
        }

        string code = GetCurrentRoomCode();
        if (roomCodeText == null && !string.IsNullOrEmpty(code))
            statusText.text = $"ROOM CODE: {code}\n{msg}";
        else
            statusText.text = msg;

        RefreshRoomCodeDisplay();
    }
}