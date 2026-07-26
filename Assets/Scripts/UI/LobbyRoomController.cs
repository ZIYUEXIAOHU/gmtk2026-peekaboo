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
    /// <summary>进房前偏好已消费（或用户点了重新选择），不再自动 SelectRole。</summary>
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
                localPlayerName = $"玩家{localConnectionId + 1}";
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
                localPlayerName = rp.playerName;
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
                    localPlayerName = player.playerName;
                    Debug.Log($"✅ 通过场景 RoomPlayer 找到连接 ID: {localConnectionId}");
                    break;
                }
            }
        }
        
        isHost = (localConnectionId == 0);
        Debug.Log($"{(isHost ? "👑 你是房主" : "👤 你是普通玩家")}，连接ID: {localConnectionId}");
        
        SubscribeEvents();

        if (GameContract.IsBound)
            roleSlots = GameContract.State.Slots;
        else
            CalculateRoleSlots(Mathf.Max(1, GetSceneRoomPlayers().Length));
        
        hiderBtn.onClick.AddListener(() => SelectRole(PlayerRole.Hider));
        randomBtn.onClick.AddListener(SelectRandomRole);
        seekerBtn.onClick.AddListener(() => SelectRole(PlayerRole.Seeker));
        readyBtn.onClick.AddListener(ToggleReady);
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

    /// <summary>
    /// 主菜单创建房间时点了 HIDER/HUNTER，会写入 PreferredRole；进 GameScene 后自动选角。
    /// </summary>
    IEnumerator TryApplyPreferredRole()
    {
        if (!PlayerPrefs.HasKey(MainMenuController.PrefPreferredRole))
            yield break;

        PlayerRole preferred = (PlayerRole)PlayerPrefs.GetInt(MainMenuController.PrefPreferredRole, (int)PlayerRole.None);
        PlayerPrefs.DeleteKey(MainMenuController.PrefPreferredRole);
        PlayerPrefs.Save();

        if (preferred != PlayerRole.Hider && preferred != PlayerRole.Seeker)
            yield break;

        // 等本地玩家与契约就绪
        for (int i = 0; i < 20; i++)
        {
            yield return new WaitForSeconds(0.15f);
            TrySubscribeGameEvents();

            if (GetLocalRoomPlayer() == null)
                continue;

            // 契约路径：需 IsBound；无契约时走本地 SpawnPlayerRole
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

                // 可能已错过 OnPhaseChanged：进 GameScene 后若已开局则补显示身份 UI
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
            UpdateStatusText("选择你的身份");
        }

        UpdateButtonVisibility();
        RefreshRoomCodeDisplay();
    }

    /// <summary>用进房前偏好 / 已同步身份跳过二次强制选角。</summary>
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

        // 进房前已选：首次同步时发给服务器并跳过选角 UI
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
            // UI 已按偏好显示，但服务端尚未确认（契约晚绑定）
            if (localPlayer != null && localPlayer.Role == PlayerRole.None)
                GameContract.Commands.SelectRole(preferred);
            resolved = preferred;
        }

        if (resolved == PlayerRole.None)
            return;

        // 已与 UI 一致则不必反复刷状态文案
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
            UpdateStatusText($"✅ 已选择: {GetRoleDisplayName(resolved)}");
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
                roomCodeText.text = $"房间码：{code}";
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
            UpdateStatusText("✅ 全员已准备，可以开始游戏！");
        }
        else if (!HasValidRoleComposition())
        {
            int total = GetSceneRoomPlayers().Length;
            UpdateStatusText($"⏳ 需要 1 名躲藏者和 1 名抓捕者 (当前 {total} 人)");
        }
        else
        {
            UpdateStatusText(isHost
                ? "⏳ 等待其他玩家选择身份并准备..."
                : "⏳ 等待全员选择身份并准备...");
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
                    btnText.text = isReady ? "✅ 已准备" : "准备";
                    btnText.color = isReady ? Color.green : Color.white;
                }
            }

            if (reselectBtn != null)
                reselectBtn.gameObject.SetActive(hasSelectedRole);

            if (isHost)
                UpdateStatusText(isReady ? "✅ 已自动准备，等待其他玩家..." : "已取消准备");
            else
                UpdateStatusText(isReady ? "✅ 已准备！等待房主开始..." : "已取消准备");
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
                string localMark = isLocal ? " (你)" : "";
                
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
            case PlayerRole.Hider: return "🟢 躲藏者";
            case PlayerRole.Seeker: return "🔴 抓捕者";
            default: return "❓ 未选择";
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
        Debug.Log($"🎯 选择身份: {role}");
        
        if (hasSelectedRole || isLocked)
        {
            UpdateStatusText(isLocked ? "⚠️ 已准备，无法更改身份" : "⚠️ 你已选择身份，点击「重新选择」可更换");
            return;
        }
        
        UpdateRoleCounts();
        
        if (role == PlayerRole.Hider && roleSlots.HiderFull)
        {
            UpdateStatusText("⚠️ 躲藏者已满！");
            return;
        }
        if (role == PlayerRole.Seeker && roleSlots.SeekerFull)
        {
            UpdateStatusText("⚠️ 抓捕者已满！");
            return;
        }
        
        selectedRole = role;
        hasSelectedRole = true;
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.SelectRole(role);
            UpdateStatusText($"✅ 已选择: {GetRoleDisplayName(role)}（等待服务器确认）");
        }
        else
        {
            ApplyRoleOnExistingPlayer(role);
            UpdateStatusText($"✅ 已选择: {GetRoleDisplayName(role)}");
        }
        
        if (roleSelectPanel != null)
            roleSelectPanel.SetActive(false);
        
        if (reselectBtn != null)
            reselectBtn.gameObject.SetActive(true);
        
        // 对局 UI（HiderUI/SeekerUI）等开局 ShowGameUI 再显示；选角阶段只记身份
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

        Debug.Log("🔄 重新选择身份");

        preferredRoleConsumed = true; // 不要再自动套用进房前偏好
        selectedRole = PlayerRole.None;
        hasSelectedRole = false;
        isReady = false;
        isLocked = false;
        
        UpdateRoleUI(PlayerRole.None);
        
        // 服务端清身份并清准备（ServerClearPlayerRole）；房主重选后再选角会再次自动准备
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
                btnText.text = "准备";
                btnText.color = Color.white;
            }
        }
        
        UpdateStatusText("选择你的身份");
        UpdateRoleButtons();
        UpdatePlayerList();
        RefreshRoomCodeDisplay();
        if (!GameContract.IsBound)
            UpdateRoleCounts();
        
        Debug.Log("✅ 已重置，可以重新选择身份");
    }
    
    void SelectRandomRole()
    {
        Debug.Log("🎲 选择随机身份");
        
        if (hasSelectedRole || isLocked)
        {
            UpdateStatusText(isLocked ? "⚠️ 已准备，无法更改身份" : "⚠️ 你已选择身份，点击「重新选择」可更换");
            return;
        }
        
        UpdateRoleCounts();
        
        List<PlayerRole> available = new List<PlayerRole>();
        if (!roleSlots.HiderFull) available.Add(PlayerRole.Hider);
        if (!roleSlots.SeekerFull) available.Add(PlayerRole.Seeker);
        
        if (available.Count == 0)
        {
            UpdateStatusText("⚠️ 所有身份已满");
            return;
        }
        
        PlayerRole role = available[Random.Range(0, available.Count)];
        SelectRole(role);
    }
    
    void ToggleReady()
    {
        if (!hasSelectedRole)
        {
            UpdateStatusText("⚠️ 请先选择身份！");
            return;
        }

        RoomPlayer localPlayer = GetLocalRoomPlayer();
        if (localPlayer == null)
        {
            UpdateStatusText("⚠️ 找不到本地玩家，无法准备");
            return;
        }

        localPlayer.CmdToggleReady();
    }
    
    void HostStartGame()
    {
        if (!isHost)
        {
            UpdateStatusText("⚠️ 只有房主可以开始游戏！");
            return;
        }
        
        if (!CanStartGame())
        {
            if (!HasValidRoleComposition())
                UpdateStatusText("⚠️ 需要至少 1 名躲藏者和 1 名抓捕者！");
            else
                UpdateStatusText("⚠️ 需要全员选择身份并准备后才能开始！");
            return;
        }
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.HostStartGame();
            UpdateStatusText("🚀 正在开始游戏...");
            // GameScene 已在选角；进入 Prep 时 Rpc 会再调一次，这里先切对局 UI
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
                UpdateStatusText("✅ 已加入房间");
        }
        else if (state == RoomConnectionState.Failed)
        {
            UpdateStatusText("❌ 连接失败，请重试");
        }
        else if (state == RoomConnectionState.Connecting)
        {
            UpdateStatusText("⏳ 正在连接...");
        }
        else if (state == RoomConnectionState.Disconnected)
        {
            UpdateStatusText("📋 已断开连接");
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
            UpdateStatusText($"👥 {slots.hiderCount + slots.seekerCount} 人已选择身份");
        RefreshRoomCodeDisplay();
    }

    void OnCommandRejected(CommandRejected rejected)
    {
        if (rejected.command != GameCommandType.SelectRole) return;

        if (rejected.reason == RejectReason.RoleFull)
        {
            UpdateStatusText(selectedRole == PlayerRole.Seeker
                ? "⚠️ 抓捕者已满！"
                : "⚠️ 躲藏者已满！");
            hasSelectedRole = false;
            selectedRole = PlayerRole.None;
            UpdateRoleButtons();
            return;
        }

        UpdateStatusText($"⚠️ 选择身份失败：{rejected.reason}");
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
            hiderStatusText.text = $"🟢 躲藏者 ({roleSlots.hiderCount}/{roleSlots.hiderMax})";
            hiderStatusText.color = roleSlots.HiderFull ? Color.gray : Color.green;
            if (selectedRole == PlayerRole.Hider) hiderStatusText.text += " ← 你";
        }
        
        if (seekerBtn != null)
        {
            seekerBtn.interactable = (canSelect && !roleSlots.SeekerFull);
        }
        if (seekerStatusText != null)
        {
            seekerStatusText.text = $"🔴 抓捕者 ({roleSlots.seekerCount}/{roleSlots.seekerMax})";
            seekerStatusText.color = roleSlots.SeekerFull ? Color.gray : Color.red;
            if (selectedRole == PlayerRole.Seeker) seekerStatusText.text += " ← 你";
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
            // GameScene 里 statusText 可能未绑；尝试按名字找回，避免选角无文字反馈
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
        // 无独立短码控件时，把房间码拼进状态栏，避免被后续状态覆盖后看不到
        if (roomCodeText == null && !string.IsNullOrEmpty(code))
            statusText.text = $"房间码：{code}\n{msg}";
        else
            statusText.text = msg;

        RefreshRoomCodeDisplay();
    }
}