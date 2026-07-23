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
    
    public readonly List<RoomPlayer> roomPlayers = new List<RoomPlayer>();
    
    // ===== 在 Awake 中注册预制体 =====
    void Awake()
    {
        // 注册 PlayerPrefab
        if (playerPrefab != null)
        {
            // 确保 NetworkIdentity 有正确的 assetId
            NetworkIdentity identity = playerPrefab.GetComponent<NetworkIdentity>();
            if (identity != null)
            {
                // 通过 RegisterPrefab 注册
                NetworkClient.RegisterPrefab(playerPrefab);
                Debug.Log("✅ RoomPlayerPrefab 已注册");
            }
            else
            {
                Debug.LogError("❌ PlayerPrefab 缺少 NetworkIdentity 组件！");
            }
        }
        else
        {
            Debug.LogError("❌ PlayerPrefab 未设置！");
        }
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
        // 检查是否已有玩家
        if (conn.identity != null && conn.identity.gameObject != null)
        {
            Debug.Log($"玩家 {conn.connectionId} 已存在，跳过生成");
            return;
        }
        
        bool isObserver = PlayerPrefs.GetInt("IsObserver", 0) == 1;
        
        // 生成玩家
        GameObject player = Instantiate(playerPrefab);
        
        // 设置 NetworkIdentity 的 assetId
        NetworkIdentity identity = player.GetComponent<NetworkIdentity>();
        if (identity != null)
        {
            // 通过 RegisterPrefab 注册
            NetworkServer.Spawn(player);
        }
        
        RoomPlayer rp = player.GetComponent<RoomPlayer>();
        if (rp != null)
        {
            rp.connectionId = conn.connectionId;
            rp.playerName = $"玩家{conn.connectionId + 1}";
        }
        
        NetworkServer.AddPlayerForConnection(conn, player);
        
        if (rp != null)
        {
            roomPlayers.Add(rp);
            UpdatePlayerListUI();
        }
        
        Debug.Log($"玩家 {conn.connectionId} 加入，当前人数：{roomPlayers.Count}");
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
        
        Debug.Log("所有玩家已准备，开始切换场景...");
        NetworkManager.singleton.ServerChangeScene(gameScene);
    }
    
    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        
        if (sceneName == gameScene)
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn.identity != null)
                {
                    Destroy(conn.identity.gameObject);
                    
                    if (spawnPrefabs.Count > 0)
                    {
                        GameObject gamePlayer = Instantiate(spawnPrefabs[0]);
                        NetworkServer.ReplacePlayerForConnection(conn, gamePlayer);
                    }
                }
            }
            
            GameManager gm = FindObjectOfType<GameManager>();
            if (gm != null)
            {
                gm.StartGame();
            }
            else
            {
                Debug.LogWarning("GameManager 未找到，请确保 GameScene 中有 GameManager 物体");
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