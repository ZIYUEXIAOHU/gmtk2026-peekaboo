using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class CustomNetworkManager : NetworkManager
{
    [Header("场景设置")]
    public string lobbyScene = "LobbyScene";
    public string gameScene = "GameScene";
    
    [Header("游戏设置")]
    public int maxPlayers = 6;

    /// <summary>已废弃：阶段时长以 GameConstants（PrepDuration / MatchDuration）为准，勿再读取。</summary>
    [System.Obsolete("阶段时长请用 GameConstants.PrepDuration / MatchDuration；本字段不再被逻辑读取。")]
    [HideInInspector]
    public float hideDuration = 30f;

    /// <summary>已废弃：阶段时长以 GameConstants 为准，勿再读取。</summary>
    [System.Obsolete("阶段时长请用 GameConstants.PrepDuration / MatchDuration；本字段不再被逻辑读取。")]
    [HideInInspector]
    public float seekDuration = 60f;
    
    [Header("玩家预制体")]
    public GameObject hiderPrefab;
    public GameObject seekerPrefab;
    
    [Header("等待房间出生点")]
    public Transform hiderLobbySpawnPoint;
    public Transform seekerLobbySpawnPoint;
    
    public readonly List<RoomPlayer> roomPlayers = new List<RoomPlayer>();
    public readonly Dictionary<int, string> playerNames = new Dictionary<int, string>();
    
    // ===== 是否已加载预制体 =====
    private bool prefabsLoaded = false;
    
    public override void Awake()
    {
        base.Awake();
        LoadPrefabsFromResources();

        if (playerPrefab != null)
        {
            NetworkClient.RegisterPrefab(playerPrefab);
            if (playerPrefab.GetComponent<RoomPlayer>() == null)
            {
                Debug.LogError(
                    "[CustomNetworkManager] playerPrefab 缺少 RoomPlayer！" +
                    "请改为 RoomPlayerPrefab，否则 SelectRole/Place/Investigate/Slash 全部失效。");
            }
            else
            {
                Debug.Log($"✅ playerPrefab 已注册：{playerPrefab.name}");
            }
        }

        // Hider/Seeker 预制体仅作程序 2 外观参考，禁止再当作换角用的玩家 NetworkIdentity。
        if (hiderPrefab != null)
            NetworkClient.RegisterPrefab(hiderPrefab);
        if (seekerPrefab != null)
            NetworkClient.RegisterPrefab(seekerPrefab);
    }
    
    // ==================== 从 Resources 加载预制体 ====================
    void LoadPrefabsFromResources()
    {
        if (prefabsLoaded) return;
        
        Debug.Log("📂 从 Resources 加载预制体...");
        
        if (hiderPrefab == null)
        {
            hiderPrefab = Resources.Load<GameObject>("Prefabs/HiderPrefab");
            if (hiderPrefab != null)
            {
                Debug.Log($"✅ 加载 HiderPrefab: {hiderPrefab.name}");
            }
            else
            {
                Debug.LogError("❌ 无法从 Resources/Prefabs/HiderPrefab 加载 HiderPrefab！");
            }
        }
        
        if (seekerPrefab == null)
        {
            seekerPrefab = Resources.Load<GameObject>("Prefabs/SeekerPrefab");
            if (seekerPrefab != null)
            {
                Debug.Log($"✅ 加载 SeekerPrefab: {seekerPrefab.name}");
            }
            else
            {
                Debug.LogError("❌ 无法从 Resources/Prefabs/SeekerPrefab 加载 SeekerPrefab！");
            }
        }

        // 对局权威状态预制体（Resources/NetworkGameState），客户端需提前注册才能接收 Spawn
        GameObject gameStatePrefab = Resources.Load<GameObject>("NetworkGameState");
        if (gameStatePrefab != null)
        {
            NetworkClient.RegisterPrefab(gameStatePrefab);
        }
        else
        {
            Debug.LogError("❌ Resources/NetworkGameState 预制体未找到！");
        }

        // Wave 2：可调查放置物占位预制体（ItemTable 条目 prefab 缺失时使用）
        GameObject investigablePrefab = Resources.Load<GameObject>("InvestigableItemPlaceholder");
        if (investigablePrefab != null)
        {
            NetworkClient.RegisterPrefab(investigablePrefab);
        }
        else
        {
            Debug.LogWarning("⚠ Resources/InvestigableItemPlaceholder 未找到，放置物 Spawn 可能失败。");
        }
        
        prefabsLoaded = true;
        
        Debug.Log($"hiderPrefab: {(hiderPrefab != null ? hiderPrefab.name : "NULL")}");
        Debug.Log($"seekerPrefab: {(seekerPrefab != null ? seekerPrefab.name : "NULL")}");
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // 身份/阶段权威核心：Host 启动时生成，跨场景 DontDestroyOnLoad
        NetworkGameState.ServerEnsureSpawned();
    }
    
    public bool AllPlayersReady()
    {
        if (roomPlayers.Count == 0) return false;
        foreach (var p in roomPlayers)
        {
            if (!p.isReady) return false;
        }
        return true;
    }
    
    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (conn.identity != null && conn.identity.gameObject != null)
        {
            Debug.Log($"玩家 {conn.connectionId} 已存在，跳过生成");
            return;
        }

        string playerName = $"玩家{conn.connectionId + 1}";
        playerNames[conn.connectionId] = playerName;

        if (playerPrefab == null)
        {
            Debug.LogError("[CustomNetworkManager] playerPrefab 未设置，无法为连接生成玩家。");
            return;
        }

        GameObject player = Instantiate(playerPrefab);

        RoomPlayer rp = player.GetComponent<RoomPlayer>();
        if (rp == null)
        {
            Debug.LogError(
                $"[CustomNetworkManager] playerPrefab「{playerPrefab.name}」缺少 RoomPlayer，已销毁实例。" +
                "请把 NetworkManager.playerPrefab 设为 RoomPlayerPrefab。");
            Destroy(player);
            return;
        }

        rp.connectionId = conn.connectionId;
        rp.playerName = playerName;
        rp.isRoomHost = (conn == NetworkServer.localConnection);
        rp.role = PlayerRole.None;
        rp.hiderState = HiderState.Disguised;
        rp.disguiseItemId = GameConstants.InvalidItemId;

        NetworkServer.AddPlayerForConnection(conn, player);

        roomPlayers.Add(rp);
        UpdatePlayerListUI();
        NetworkGameState.Instance?.ServerNotifyRoleSlotsChanged();

        Debug.Log($"🎮 玩家 {conn.connectionId} 加入，当前人数：{roomPlayers.Count}");
    }

    /// <summary>
    /// 选身份：只改现有玩家物体上的 Role，绝不销毁/换成 HiderPrefab、SeekerPrefab。
    /// 契约权威路径是 GameContract.Commands.SelectRole；本方法仅兼容旧 UI 本地调用。
    /// 外观/控制器由程序 2 在同一 NetworkIdentity 上按 Role 开关。
    /// </summary>
    public void SpawnPlayerRole(NetworkConnectionToClient conn, PlayerRole role)
    {
        if (conn == null)
        {
            Debug.LogError("[CustomNetworkManager] SpawnPlayerRole：conn 为空");
            return;
        }

        if (conn.identity == null)
        {
            Debug.LogError(
                $"[CustomNetworkManager] 连接 {conn.connectionId} 尚无玩家对象。" +
                "应先由 OnServerAddPlayer 生成带 RoomPlayer 的 playerPrefab。");
            return;
        }

        RoomPlayer rp = conn.identity.GetComponent<RoomPlayer>();
        if (rp == null)
        {
            Debug.LogError(
                $"[CustomNetworkManager] 玩家「{conn.identity.name}」缺少 RoomPlayer，无法设身份。" +
                "不要用 HiderPrefab/SeekerPrefab 当 playerPrefab。");
            return;
        }

        // 只改 State，不换 NetworkIdentity
        rp.role = role;
        if (role == PlayerRole.Hider)
        {
            rp.hiderState = HiderState.Disguised;
        }
        else
        {
            rp.hiderState = HiderState.Disguised;
            rp.disguiseItemId = GameConstants.InvalidItemId;
        }

        rp.transform.position = GetSpawnPosition(role);

        if (!roomPlayers.Contains(rp))
            roomPlayers.Add(rp);

        UpdatePlayerListUI();
        NetworkGameState.Instance?.ServerNotifyRoleSlotsChanged();

        Debug.Log($"[CustomNetworkManager] 连接 {conn.connectionId} 身份设为 {role}（保留同一玩家物体）");
    }
    
    // ==================== 出生点 ====================
    Vector3 GetSpawnPosition(PlayerRole role)
    {
        if (role == PlayerRole.Hider)
        {
            if (hiderLobbySpawnPoint != null)
                return hiderLobbySpawnPoint.position;
            else
                return new Vector3(-3f, -2f, 0);
        }
        else if (role == PlayerRole.Seeker)
        {
            if (seekerLobbySpawnPoint != null)
                return seekerLobbySpawnPoint.position;
            else
                return new Vector3(3f, -2f, 0);
        }
        return new Vector3(0, -2f, 0);
    }
    
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        RoomPlayer rp = roomPlayers.Find(p => p.connectionId == conn.connectionId);
        if (rp != null)
        {
            roomPlayers.Remove(rp);
            UpdatePlayerListUI();
        }
        base.OnServerDisconnect(conn);

        if (NetworkGameState.Instance != null)
        {
            NetworkGameState.Instance.ServerNotifyRoleSlotsChanged();
        }
    }

    // ===== 房间模块（程序 1 契约）：客户端连接结果转发给 NetworkRoomService =====
    public override void OnClientConnect()
    {
        base.OnClientConnect();
        NetworkRoomService.Instance?.NotifyClientConnected();
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        NetworkRoomService.Instance?.NotifyClientDisconnected();
    }

    public override void OnClientError(TransportError error, string reason)
    {
        base.OnClientError(error, reason);
        NetworkRoomService.Instance?.NotifyClientError($"{error}: {reason}");
    }

    public override void OnStopHost()
    {
        base.OnStopHost();
        roomPlayers.Clear();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        roomPlayers.Clear();
    }
    
    /// <summary>
    /// 已废弃：开局权威路径为 GameContract.Commands.HostStartGame()（NetworkGameState）。
    /// 勿再以 AllPlayersReady 作为开局门槛联调误用本方法。
    /// UI 须改用 GameContract.RoomCommands / Commands，勿直连 Mirror 本方法。
    /// </summary>
    [System.Obsolete("开局请用 GameContract.Commands.HostStartGame()（NetworkGameState）。")]
    [Server]
    public void StartGame()
    {
        Debug.LogError(
            "[CustomNetworkManager] StartGame() 已废弃，不会切场景/开局。" +
            "请走 GameContract.Commands.HostStartGame()（校验 → 切 gameScene → Prep）。" +
            "UI 须改用契约命令，勿直连本方法。");
    }

    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);

        if (sceneName == gameScene)
        {
            Debug.Log("[CustomNetworkManager] 已进入对局场景；若无挂起 Prep 则仍须 HostStartGame()。");
            NetworkGameState.Instance?.ServerTryStartPendingPrepAfterSceneChange();
        }
    }

    /// <summary>读取当前 Mirror Transport 游戏端口（KCP/Telepathy/SimpleWeb 等 PortTransport）。</summary>
    public static bool TryGetTransportPort(out ushort port)
    {
        port = 7777;
        Transport t = Transport.active;
        if (t == null && singleton != null)
            t = singleton.transport;
        if (t is PortTransport pt)
        {
            port = pt.Port;
            return true;
        }
        return false;
    }

    /// <summary>写入当前 Transport 端口（JoinRoom 用）。成功返回 true。</summary>
    public static bool TrySetTransportPort(ushort port)
    {
        Transport t = Transport.active;
        if (t == null && singleton != null)
            t = singleton.transport;
        if (t is PortTransport pt)
        {
            pt.Port = port;
            return true;
        }
        return false;
    }
    
    public void UpdatePlayerListUI()
    {
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn.identity != null)
            {
                RoomPlayer rp = conn.identity.GetComponent<RoomPlayer>();
                if (rp != null)
                {
                    rp.TargetUpdatePlayerList(conn, roomPlayers.Count);
                }
            }
        }
    }
    
    public void UpdateReadyStatus(int connectionId, bool isReady)
    {
        RoomPlayer rp = roomPlayers.Find(p => p.connectionId == connectionId);
        if (rp != null)
        {
            rp.isReady = isReady;
            UpdatePlayerListUI();
        }
    }
}