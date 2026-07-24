using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 局域网 UDP 房间发现（程序 1 内部）。
/// 权威上报路径：NetworkRoomService.ReportDiscoveredRoom。
/// 可选兼容：若 Inspector 仍挂了旧 RoomListController，则同步喂一份给旧 UI（主路径不依赖 UI）。
/// </summary>
public class ManualDiscovery : MonoBehaviour
{
    [Header("设置")]
    public int broadcastPort = 47777;
    public float broadcastInterval = 2f;

    [Header("可选兼容（旧 UI，可留空）")]
    [Tooltip("旧版房间列表 UI。主路径走 NetworkRoomService；仅当场景仍引用时用于兼容。")]
    public RoomListController roomListController;

    private UdpClient udpClient;
    private bool isBroadcasting = false;
    private bool isListening = false;
    private Coroutine broadcastCoroutine;

    private Queue<Action> mainThreadActions = new Queue<Action>();

    void Start()
    {
        StartListening();
    }

    void Update()
    {
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
            {
                try
                {
                    mainThreadActions.Dequeue()?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"执行队列操作失败：{e.Message}");
                }
            }
        }
    }

    public void StartBroadcasting()
    {
        if (isBroadcasting) return;

        isBroadcasting = true;
        broadcastCoroutine = StartCoroutine(BroadcastCoroutine());
        Debug.Log("开始局域网广播");
    }

    public void StopBroadcasting()
    {
        isBroadcasting = false;
        if (broadcastCoroutine != null)
        {
            StopCoroutine(broadcastCoroutine);
            broadcastCoroutine = null;
        }
        Debug.Log("停止局域网广播");
    }

    IEnumerator BroadcastCoroutine()
    {
        while (isBroadcasting)
        {
            BroadcastData();
            yield return new WaitForSeconds(broadcastInterval);
        }
    }

    void BroadcastData()
    {
        try
        {
            CustomNetworkManager nm = FindObjectOfType<CustomNetworkManager>();
            if (nm == null) return;

            string roomName = PlayerPrefs.GetString("RoomName", "躲猫猫房间");
            string hostName = System.Environment.MachineName;
            int currentPlayers = nm.roomPlayers?.Count ?? 0;
            int maxPlayers = nm.maxConnections;
            int status = (int)ResolveBroadcastRoomStatus();
            string gameMode = PlayerPrefs.GetString("GameMode", "经典模式");

            string data = $"{roomName}|{hostName}|{currentPlayers}|{maxPlayers}|{status}|{gameMode}";
            byte[] bytes = Encoding.UTF8.GetBytes(data);

            using (UdpClient client = new UdpClient())
            {
                client.EnableBroadcast = true;
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, broadcastPort);
                client.Send(bytes, bytes.Length, endPoint);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"广播失败：{e.Message}");
        }
    }

    /// <summary>
    /// Host 广播用：从服务端权威 NetworkGameState.Phase 映射房间列表 RoomStatus。
    /// 无 GameState 时保持 Idle（可加入）。
    /// Waiting→Idle；Prep/Playing→Playing（只能观战）；Ended→Settling。
    /// </summary>
    static RoomStatus ResolveBroadcastRoomStatus()
    {
        NetworkGameState gs = NetworkGameState.Instance;
        if (gs == null) return RoomStatus.Idle;

        switch (gs.Phase)
        {
            case GamePhase.Waiting:
                return RoomStatus.Idle;
            case GamePhase.Prep:
            case GamePhase.Playing:
                return RoomStatus.Playing;
            case GamePhase.Ended:
                return RoomStatus.Settling;
            default:
                return RoomStatus.Idle;
        }
    }

    /// <summary>开始监听局域网广播。返回是否启动成功（供 NetworkRoomService.RefreshRoomList 判断是否需要上报 OnRoomError）。</summary>
    public bool StartListening()
    {
        if (isListening) return true;

        isListening = true;
        try
        {
            udpClient = new UdpClient(broadcastPort);
            udpClient.BeginReceive(OnReceive, null);
            Debug.Log("开始监听局域网广播");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"监听启动失败：{e.Message}");
            isListening = false;
            return false;
        }
    }

    public void StopListening()
    {
        isListening = false;
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
    }

    void OnReceive(IAsyncResult result)
    {
        if (!isListening) return;

        try
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, broadcastPort);
            byte[] bytes = udpClient.EndReceive(result, ref endPoint);
            string data = Encoding.UTF8.GetString(bytes);

            string[] info = data.Split('|');
            if (info.Length < 6) return;

            string roomName = info[0];
            string hostName = info[1];
            int currentPlayers;
            int maxPlayers;
            RoomStatus status;
            string gameMode = info[5];

            if (!int.TryParse(info[2], out currentPlayers))
                currentPlayers = 0;
            if (!int.TryParse(info[3], out maxPlayers))
                maxPlayers = 6;
            if (!Enum.TryParse(info[4], out status))
                status = RoomStatus.Idle;

            string ipAddress = endPoint.Address.ToString();
            // 游戏端口与 NetworkManager Transport 同源（勿硬编码 7777）
            int port = GetTransportGamePort();
            string serverId = $"{ipAddress}:{port}";

            Debug.Log($"发现房间：{roomName} @ {ipAddress} ({currentPlayers}/{maxPlayers}人)");

            RoomItemData discovered = new RoomItemData
            {
                serverId = serverId,
                ipAddress = ipAddress,
                port = port,
                roomName = roomName,
                hostName = hostName,
                currentPlayers = currentPlayers,
                maxPlayers = maxPlayers,
                status = status,
                gameMode = gameMode,
                ping = UnityEngine.Random.Range(5f, 50f)
            };

            lock (mainThreadActions)
            {
                mainThreadActions.Enqueue(() =>
                {
                    // 权威路径：契约房间服务
                    NetworkRoomService.Instance?.ReportDiscoveredRoom(discovered);

                    // 可选兼容：旧 UI（LobbyScene 仍可能挂着引用）
                    if (roomListController != null)
                    {
                        roomListController.AddRoom(
                            discovered.serverId,
                            discovered.ipAddress,
                            discovered.port,
                            discovered.roomName,
                            discovered.hostName,
                            discovered.currentPlayers,
                            discovered.maxPlayers,
                            discovered.status,
                            discovered.gameMode);
                    }
                });
            }

            udpClient.BeginReceive(OnReceive, null);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"接收数据跳过：{e.Message}");

            try
            {
                udpClient.BeginReceive(OnReceive, null);
            }
            catch
            {
                // 忽略
            }
        }
    }

    void OnDestroy()
    {
        StopBroadcasting();
        StopListening();
    }

    /// <summary>读取 Mirror Transport 游戏端口（与 JoinRoom / Host 监听同源）。</summary>
    static int GetTransportGamePort()
    {
        if (CustomNetworkManager.TryGetTransportPort(out ushort port))
            return port;
        return 7777;
    }
}
