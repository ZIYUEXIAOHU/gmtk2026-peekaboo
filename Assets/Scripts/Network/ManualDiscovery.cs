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
    [Tooltip("Ping 应答端口；0 表示 broadcastPort + 1")]
    public int pingPort = 0;
    public float broadcastInterval = 2f;

    [Header("可选兼容（旧 UI，可留空）")]
    [Tooltip("旧版房间列表 UI。主路径走 NetworkRoomService；仅当场景仍引用时用于兼容。")]
    public RoomListController roomListController;

    const float PingTimeoutSeconds = 1f;
    const float PingRetestInterval = 2f;

    private UdpClient udpClient;
    private UdpClient pingResponder;
    private UdpClient pingClient;
    private bool isBroadcasting = false;
    private bool isListening = false;
    private bool isPingResponding = false;
    private Coroutine broadcastCoroutine;

    private readonly Queue<Action> mainThreadActions = new Queue<Action>();
    private readonly Dictionary<string, RoomItemData> roomCache = new Dictionary<string, RoomItemData>();
    private readonly Dictionary<int, PendingPing> pendingPings = new Dictionary<int, PendingPing>();
    private readonly Dictionary<string, int> pendingPingsByServerId = new Dictionary<string, int>();
    private readonly Dictionary<string, float> lastPingAttemptAt = new Dictionary<string, float>();
    private int nextPingToken;

    struct PendingPing
    {
        public string serverId;
        public long sentAtUtcTicks;
    }

    int EffectivePingPort => pingPort > 0 ? pingPort : broadcastPort + 1;

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
        // 房主只负责广播；释放发现监听端口，允许同机客户端按需监听。
        StopListening();

        if (isBroadcasting) return;

        isBroadcasting = true;
        StartPingResponder();
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
        StopPingResponder();
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
            // 第 7 段：房间短码（旧客户端可忽略多余字段）
            string roomCode = NetworkRoomService.Instance != null
                ? NetworkRoomService.Instance.CurrentRoomCode
                : string.Empty;

            string data = $"{roomName}|{hostName}|{currentPlayers}|{maxPlayers}|{status}|{gameMode}|{roomCode}";
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
            StartPingClient();
            Debug.Log("开始监听局域网广播");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"监听启动失败：{e.Message}");
            isListening = false;
            StopPingClient();
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
        StopPingClient();
    }

    void StartPingResponder()
    {
        if (isPingResponding) return;

        try
        {
            pingResponder = new UdpClient(EffectivePingPort);
            isPingResponding = true;
            pingResponder.BeginReceive(OnPingResponderReceive, null);
            Debug.Log($"开始 Ping 应答监听：{EffectivePingPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Ping 应答启动失败：{e.Message}");
            StopPingResponder();
        }
    }

    void StopPingResponder()
    {
        isPingResponding = false;
        if (pingResponder != null)
        {
            pingResponder.Close();
            pingResponder = null;
        }
    }

    void StartPingClient()
    {
        if (pingClient != null) return;

        try
        {
            pingClient = new UdpClient(0);
            pingClient.BeginReceive(OnPingClientReceive, null);
        }
        catch (Exception e)
        {
            Debug.LogError($"Ping 客户端启动失败：{e.Message}");
            StopPingClient();
        }
    }

    void StopPingClient()
    {
        pendingPings.Clear();
        pendingPingsByServerId.Clear();
        lastPingAttemptAt.Clear();

        if (pingClient != null)
        {
            pingClient.Close();
            pingClient = null;
        }
    }

    void OnPingResponderReceive(IAsyncResult result)
    {
        if (!isPingResponding || pingResponder == null) return;

        try
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] bytes = pingResponder.EndReceive(result, ref remote);
            string data = Encoding.UTF8.GetString(bytes);

            if (data.StartsWith("PING|", StringComparison.Ordinal))
            {
                string token = data.Substring(5);
                byte[] pong = Encoding.UTF8.GetBytes($"PONG|{token}");
                pingResponder.Send(pong, pong.Length, remote);
            }

            pingResponder.BeginReceive(OnPingResponderReceive, null);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Ping 应答跳过：{e.Message}");
            try
            {
                if (isPingResponding && pingResponder != null)
                    pingResponder.BeginReceive(OnPingResponderReceive, null);
            }
            catch
            {
                // 忽略
            }
        }
    }

    void OnPingClientReceive(IAsyncResult result)
    {
        if (pingClient == null) return;

        try
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] bytes = pingClient.EndReceive(result, ref remote);
            string data = Encoding.UTF8.GetString(bytes);

            if (data.StartsWith("PONG|", StringComparison.Ordinal) &&
                int.TryParse(data.Substring(5), out int token) &&
                pendingPings.TryGetValue(token, out PendingPing pending))
            {
                pendingPings.Remove(token);
                pendingPingsByServerId.Remove(pending.serverId);

                double elapsedMs = (DateTime.UtcNow.Ticks - pending.sentAtUtcTicks) / (double)TimeSpan.TicksPerMillisecond;
                float pingMs = Mathf.Round((float)elapsedMs * 10f) / 10f;

                lock (mainThreadActions)
                {
                    mainThreadActions.Enqueue(() => ApplyPingResult(pending.serverId, pingMs));
                }
            }

            pingClient.BeginReceive(OnPingClientReceive, null);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Ping 响应跳过：{e.Message}");
            try
            {
                if (pingClient != null)
                    pingClient.BeginReceive(OnPingClientReceive, null);
            }
            catch
            {
                // 忽略
            }
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
            string roomCode = info.Length > 6 ? info[6] : string.Empty;

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

            Debug.Log($"发现房间：{roomName} @ {ipAddress} ({currentPlayers}/{maxPlayers}人) code={roomCode}");

            float existingPing = -1f;
            if (roomCache.TryGetValue(serverId, out RoomItemData cached) && cached.ping >= 0f)
                existingPing = cached.ping;

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
                ping = existingPing,
                roomCode = roomCode
            };

            roomCache[serverId] = discovered;

            lock (mainThreadActions)
            {
                mainThreadActions.Enqueue(() =>
                {
                    ReportRoom(discovered);
                    TrySendPing(discovered);
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

    void ReportRoom(RoomItemData discovered)
    {
        NetworkRoomService.Instance?.ReportDiscoveredRoom(discovered);

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
                discovered.gameMode,
                discovered.ping);
        }
    }

    void TrySendPing(RoomItemData room)
    {
        if (!isListening || pingClient == null || room == null) return;
        if (string.IsNullOrEmpty(room.ipAddress) || string.IsNullOrEmpty(room.serverId)) return;
        if (pendingPingsByServerId.ContainsKey(room.serverId)) return;

        if (lastPingAttemptAt.TryGetValue(room.serverId, out float lastAttempt) &&
            Time.unscaledTime - lastAttempt < PingRetestInterval)
            return;

        if (!IPAddress.TryParse(room.ipAddress, out IPAddress targetIp))
            return;

        int token = ++nextPingToken;
        lastPingAttemptAt[room.serverId] = Time.unscaledTime;
        pendingPings[token] = new PendingPing
        {
            serverId = room.serverId,
            sentAtUtcTicks = DateTime.UtcNow.Ticks
        };
        pendingPingsByServerId[room.serverId] = token;

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes($"PING|{token}");
            pingClient.Send(bytes, bytes.Length, new IPEndPoint(targetIp, EffectivePingPort));
            StartCoroutine(PingTimeoutCoroutine(token, room.serverId));
        }
        catch (Exception e)
        {
            pendingPings.Remove(token);
            pendingPingsByServerId.Remove(room.serverId);
            Debug.LogWarning($"Ping 发送失败 {room.serverId}：{e.Message}");
        }
    }

    IEnumerator PingTimeoutCoroutine(int token, string serverId)
    {
        yield return new WaitForSecondsRealtime(PingTimeoutSeconds);
        if (pendingPings.Remove(token))
            pendingPingsByServerId.Remove(serverId);
    }

    void ApplyPingResult(string serverId, float pingMs)
    {
        if (!roomCache.TryGetValue(serverId, out RoomItemData room)) return;

        room.ping = pingMs;
        roomCache[serverId] = room;
        ReportRoom(room);
    }

    void OnDestroy()
    {
        StopBroadcasting();
        StopListening();
        roomCache.Clear();
    }

    /// <summary>读取 Mirror Transport 游戏端口（与 JoinRoom / Host 监听同源）。</summary>
    static int GetTransportGamePort()
    {
        if (CustomNetworkManager.TryGetTransportPort(out ushort port))
            return port;
        return 7777;
    }
}
