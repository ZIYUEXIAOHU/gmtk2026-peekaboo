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
    public float hideDuration = 30f;
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
    
    void Awake()
    {
        // ===== 从 Resources 加载预制体 =====
        LoadPrefabsFromResources();
        
        if (playerPrefab != null)
        {
            NetworkClient.RegisterPrefab(playerPrefab);
            Debug.Log("✅ RoomPlayerPrefab 已注册");
        }
        
        if (hiderPrefab != null)
        {
            NetworkClient.RegisterPrefab(hiderPrefab);
            Debug.Log("✅ HiderPrefab 已注册");
        }
        
        if (seekerPrefab != null)
        {
            NetworkClient.RegisterPrefab(seekerPrefab);
            Debug.Log("✅ SeekerPrefab 已注册");
        }
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
        
        prefabsLoaded = true;
        
        Debug.Log($"hiderPrefab: {(hiderPrefab != null ? hiderPrefab.name : "NULL")}");
        Debug.Log($"seekerPrefab: {(seekerPrefab != null ? seekerPrefab.name : "NULL")}");
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
        
        Debug.Log($"玩家 {conn.connectionId} 已连接，等待选择身份");
        
        UpdatePlayerListUI();
    }
    
    // ==================== 生成角色（支持重新选择） ====================
    public void SpawnPlayerRole(NetworkConnectionToClient conn, PlayerRole role)
    {
        if (conn == null)
        {
            Debug.LogError("❌ conn 为空！");
            return;
        }
        
        Debug.Log($"🎯 生成角色: 连接 {conn.connectionId}, 角色 {role}");
        
        // 确保预制体已加载
        LoadPrefabsFromResources();
        
        // ===== 安全销毁旧的玩家对象 =====
        if (conn.identity != null)
        {
            GameObject oldPlayer = conn.identity.gameObject;
            RoomPlayer oldRp = oldPlayer.GetComponent<RoomPlayer>();
            if (oldRp != null)
            {
                roomPlayers.Remove(oldRp);
            }
            // 先移除连接，再销毁
            NetworkServer.RemovePlayerForConnection(conn, false);
            Destroy(oldPlayer);
            Debug.Log($"🗑️ 销毁旧玩家对象");
        }
        
        // 选择对应的预制体
        GameObject prefab = null;
        if (role == PlayerRole.Hider)
            prefab = hiderPrefab;
        else if (role == PlayerRole.Seeker)
            prefab = seekerPrefab;
        
        if (prefab == null)
        {
            Debug.LogError($"❌ 未找到 {role} 对应的预制体！");
            Debug.Log($"   hiderPrefab: {(hiderPrefab != null ? hiderPrefab.name : "NULL")}");
            Debug.Log($"   seekerPrefab: {(seekerPrefab != null ? seekerPrefab.name : "NULL")}");
            return;
        }
        
        // 生成角色
        GameObject player = Instantiate(prefab);
        player.transform.position = GetSpawnPosition(role);
        
        RoomPlayer rp = player.GetComponent<RoomPlayer>();
        if (rp != null)
        {
            rp.connectionId = conn.connectionId;
            if (playerNames.TryGetValue(conn.connectionId, out string name))
            {
                rp.playerName = name;
            }
            else
            {
                rp.playerName = $"玩家{conn.connectionId + 1}";
            }
            rp.isReady = false;
        }
        
        // ===== 使用 ReplacePlayerForConnection 确保正确替换 =====
        NetworkServer.ReplacePlayerForConnection(conn, player);
        
        if (rp != null)
        {
            roomPlayers.Add(rp);
            UpdatePlayerListUI();
        }
        
        Debug.Log($"✅ 玩家 {conn.connectionId} 生成为 {role} 角色");
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
    }
    
    [Server]
    public void StartGame()
    {
        if (!AllPlayersReady())
        {
            Debug.Log("还有玩家未准备！");
            return;
        }
        
        if (roomPlayers.Count < 2)
        {
            Debug.Log("至少需要2名玩家！");
            return;
        }
        
        Debug.Log("所有玩家已准备，开始游戏...");
        NetworkManager.singleton.ServerChangeScene(gameScene);
    }
    
    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        
        if (sceneName == gameScene)
        {
            GameManager gm = FindObjectOfType<GameManager>();
            if (gm != null)
            {
                gm.StartGame();
            }
        }
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