using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
    
    [Header("按钮")]
    public Button readyBtn;
    public Button startGameBtn;
    public Button reselectBtn;
    
    // 玩家列表数据
    private List<GameObject> playerItems = new List<GameObject>();
    
    // 角色名额（使用契约 RoleSlots）
    private RoleSlots roleSlots;
    
    private PlayerRole selectedRole = PlayerRole.None;
    private bool isReady = false;
    private bool gameStarted = false;
    private bool hasSelectedRole = false;
    private bool isLocked = false;
    private bool isHost = false;
    
    // 本地玩家信息
    private string localPlayerName = "";
    private int localConnectionId = -1;
    private CustomNetworkManager networkManager;
    
    void Start()
    {
        networkManager = FindObjectOfType<CustomNetworkManager>();
        
        // ===== 获取本地玩家连接 ID =====
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
        
        if (localConnectionId == -1 && networkManager != null && networkManager.roomPlayers.Count > 0)
        {
            foreach (var player in networkManager.roomPlayers)
            {
                if (player != null && player.isLocalPlayer)
                {
                    localConnectionId = player.connectionId;
                    localPlayerName = player.playerName;
                    Debug.Log($"✅ 通过 roomPlayers 找到连接 ID: {localConnectionId}");
                    break;
                }
            }
        }
        
        isHost = (localConnectionId == 0);
        Debug.Log($"{(isHost ? "👑 你是房主" : "👤 你是普通玩家")}，连接ID: {localConnectionId}");
        
        // ===== 绑定按钮事件 =====
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
        
        // ===== 订阅契约事件 =====
        SubscribeEvents();
        
        // ===== 显示等待房间 UI =====
        ShowLobbyUI();
        
        UpdateRoleButtons();
        UpdateStatusText("选择你的身份");
        UpdatePlayerList();
    }
    
    void SubscribeEvents()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnConnectionStateChanged += OnConnectionStateChanged;
                GameContract.Events.OnRoleSlotsChanged += OnRoleSlotsChanged;
                GameContract.Events.OnPhaseChanged += OnPhaseChanged;
            }
        }
        catch { }
    }
    
    void OnDestroy()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnConnectionStateChanged -= OnConnectionStateChanged;
                GameContract.Events.OnRoleSlotsChanged -= OnRoleSlotsChanged;
                GameContract.Events.OnPhaseChanged -= OnPhaseChanged;
            }
        }
        catch { }
    }
    
    // ==================== UI 切换 ====================
    void ShowLobbyUI()
    {
        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (gameUI != null) gameUI.SetActive(false);
        if (roleSelectPanel != null) roleSelectPanel.SetActive(true);
        if (reselectBtn != null) reselectBtn.gameObject.SetActive(false);
        UpdateButtonVisibility();
    }
    
    void ShowGameUI()
    {
        if (lobbyUI != null) lobbyUI.SetActive(false);
        if (gameUI != null) gameUI.SetActive(true);
        if (roleSelectPanel != null) roleSelectPanel.SetActive(false);
    }
    
    void UpdateButtonVisibility()
    {
        if (isHost)
        {
            if (readyBtn != null)
                readyBtn.gameObject.SetActive(false);
            if (startGameBtn != null)
                startGameBtn.gameObject.SetActive(true);
        }
        else
        {
            if (readyBtn != null)
                readyBtn.gameObject.SetActive(true);
            if (startGameBtn != null)
                startGameBtn.gameObject.SetActive(false);
        }
        UpdateStartGameButtonInteractable();
    }
    
    void UpdateStartGameButtonInteractable()
    {
        if (startGameBtn == null || !isHost) return;
        
        bool allReady = CheckAllPlayersReady();
        startGameBtn.interactable = allReady;
        
        if (allReady)
        {
            UpdateStatusText("✅ 所有玩家已准备！点击「开始游戏」");
        }
        else
        {
            UpdateStatusText($"⏳ 等待所有玩家准备...");
        }
    }
    
    bool CheckAllPlayersReady()
    {
        if (networkManager == null) return false;
        if (networkManager.roomPlayers.Count < 2) return false;
        
        foreach (var player in networkManager.roomPlayers)
        {
            if (player == null) continue;
            if (!player.isReady) return false;
        }
        return true;
    }
    
    // ==================== 玩家列表更新 ====================
    public void UpdatePlayerList()
    {
        foreach (var item in playerItems)
        {
            Destroy(item);
        }
        playerItems.Clear();
        
        if (networkManager == null) return;
        
        foreach (var player in networkManager.roomPlayers)
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
    
    // ==================== 更新名额统计（使用契约 RoleSlots） ====================
    void UpdateRoleCounts()
    {
        int hiderCount = 0;
        int seekerCount = 0;
        
        if (networkManager == null) return;
        
        foreach (var player in networkManager.roomPlayers)
        {
            if (player == null) continue;
            if (player.Role == PlayerRole.Hider) hiderCount++;
            else if (player.Role == PlayerRole.Seeker) seekerCount++;
        }
        
        roleSlots.hiderCount = hiderCount;
        roleSlots.seekerCount = seekerCount;
    }
    
    // ==================== 选择身份（使用契约） ====================
    void SelectRole(PlayerRole role)
    {
        Debug.Log($"🎯 选择身份: {role}");
        
        if (hasSelectedRole || isLocked)
        {
            UpdateStatusText(isLocked ? "⚠️ 已准备，无法更改身份" : "⚠️ 你已选择身份，点击「重新选择」可更换");
            return;
        }
        
        UpdateRoleCounts();
        
        // 使用契约 RoleSlots 检查满员
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
        
        // ===== 优先使用契约 =====
        if (GameContract.IsBound)
        {
            GameContract.Commands.SelectRole(role);
            UpdateStatusText($"✅ 已选择: {GetRoleDisplayName(role)}（等待服务器确认）");
        }
        else
        {
            // 本地测试模式
            if (networkManager != null)
            {
                foreach (var player in networkManager.roomPlayers)
                {
                    if (player.connectionId == localConnectionId)
                    {
                        player.SetRole(role);
                        break;
                    }
                }
                SpawnLocalPlayer(role);
            }
            
            UpdateStatusText($"✅ 已选择: {GetRoleDisplayName(role)}");
        }
        
        if (roleSelectPanel != null)
            roleSelectPanel.SetActive(false);
        
        if (reselectBtn != null)
            reselectBtn.gameObject.SetActive(true);
        
        UpdatePlayerList();
        UpdateRoleCounts();
        UpdateRoleButtons();
    }
    
    // ==================== 生成本地玩家角色 ====================
    void SpawnLocalPlayer(PlayerRole role)
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
                Debug.Log($"✅ 通过 connectionId 找到连接: {localConnectionId}");
                break;
            }
        }
        
        if (localConn == null)
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                localConn = conn;
                Debug.Log($"✅ 使用第一个连接: {conn.connectionId}");
                break;
            }
        }
        
        if (localConn == null)
        {
            Debug.LogError($"❌ 找不到任何连接！");
            return;
        }
        
        networkManager.SpawnPlayerRole(localConn, role);
        Debug.Log($"✅ 生成 {role} 角色！");
    }
    
    // ==================== 重新选择 ====================
    void ReselectRole()
    {
        if (isLocked)
        {
            UpdateStatusText("⚠️ 已准备，无法重新选择");
            return;
        }
        
        Debug.Log("🔄 重新选择身份");
        
        DestroyCurrentPlayer();
        
        selectedRole = PlayerRole.None;
        hasSelectedRole = false;
        isReady = false;
        isLocked = false;
        
        if (networkManager != null)
        {
            foreach (var player in networkManager.roomPlayers)
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
        
        TextMeshProUGUI btnText = readyBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            btnText.text = "准备";
            btnText.color = Color.white;
        }
        
        UpdateStatusText("选择你的身份");
        UpdateRoleButtons();
        UpdatePlayerList();
        UpdateRoleCounts();
        
        Debug.Log("✅ 已重置，可以重新选择身份");
    }
    
    // ==================== 销毁当前玩家预制体 ====================
    void DestroyCurrentPlayer()
    {
        if (networkManager == null) return;
        
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn.identity != null)
            {
                RoomPlayer rp = conn.identity.GetComponent<RoomPlayer>();
                if (rp != null && rp.connectionId == localConnectionId)
                {
                    GameObject playerObj = conn.identity.gameObject;
                    if (playerObj.name.Contains("HiderPrefab") || 
                        playerObj.name.Contains("SeekerPrefab") ||
                        playerObj.GetComponent<HiderController>() != null ||
                        playerObj.GetComponent<SeekerController>() != null)
                    {
                        networkManager.roomPlayers.Remove(rp);
                        NetworkServer.Destroy(playerObj);
                        Debug.Log("🗑️ 销毁当前玩家预制体");
                    }
                    break;
                }
            }
        }
    }
    
    // ==================== 随机选择 ====================
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
    
    // ==================== 准备按钮 ====================
    void ToggleReady()
    {
        if (!hasSelectedRole)
        {
            UpdateStatusText("⚠️ 请先选择身份！");
            return;
        }
        
        isReady = !isReady;
        isLocked = isReady;
        
        TextMeshProUGUI btnText = readyBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            btnText.text = isReady ? "✅ 已准备" : "准备";
            btnText.color = isReady ? Color.green : Color.white;
        }
        
        if (isReady)
        {
            if (reselectBtn != null)
                reselectBtn.gameObject.SetActive(false);
            UpdateStatusText("✅ 已准备！等待房主开始...");
        }
        else
        {
            if (reselectBtn != null)
                reselectBtn.gameObject.SetActive(true);
            UpdateStatusText("已取消准备");
        }
        
        if (networkManager != null)
        {
            foreach (var player in networkManager.roomPlayers)
            {
                if (player.connectionId == localConnectionId)
                {
                    player.isReady = isReady;
                    break;
                }
            }
        }
        
        UpdatePlayerList();
        UpdateRoleButtons();
        UpdateStartGameButtonInteractable();
    }
    
    // ==================== 房主开始游戏 ====================
    void HostStartGame()
    {
        if (!isHost)
        {
            UpdateStatusText("⚠️ 只有房主可以开始游戏！");
            return;
        }
        
        if (!CheckAllPlayersReady())
        {
            UpdateStatusText("⚠️ 等待所有玩家准备...");
            return;
        }
        
        // ===== 优先使用契约 =====
        if (GameContract.IsBound)
        {
            GameContract.Commands.HostStartGame();
            UpdateStatusText("🚀 正在开始游戏...");
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
    
    // ==================== 事件回调 ====================
    void OnConnectionStateChanged(RoomConnectionState state)
    {
        if (state == RoomConnectionState.InRoom)
        {
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
        
        UpdateRoleButtons();
        UpdateStatusText($"👥 {slots.hiderCount + slots.seekerCount} 人已选择身份");
        UpdatePlayerList();
        
        if (slots.CanStart)
        {
            UpdateStatusText("✅ 所有玩家已选择身份！可以开始了");
        }
    }
    
    void OnPhaseChanged(GamePhase phase, float duration)
    {
        if (phase != GamePhase.Waiting && !gameStarted)
        {
            gameStarted = true;
            ShowGameUI();
        }
    }
    
    // ==================== UI 更新 ====================
    void UpdateRoleButtons()
    {
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
        
        if (readyBtn != null)
        {
            readyBtn.interactable = hasSelectedRole && !isLocked;
        }
        
        UpdateButtonVisibility();
    }
    
    void UpdateStatusText(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }
}