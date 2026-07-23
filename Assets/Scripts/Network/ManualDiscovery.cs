using Mirror;
using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections;
using System.Collections.Generic;

public class ManualDiscovery : MonoBehaviour
{
    [Header("设置")]
    public int broadcastPort = 47777;
    public float broadcastInterval = 2f;
    
    public RoomListController roomListController;
    
    private UdpClient udpClient;
    private bool isBroadcasting = false;
    private bool isListening = false;
    private Coroutine broadcastCoroutine;
    
    // 线程安全的队列
    private Queue<Action> mainThreadActions = new Queue<Action>();
    
    void Start()
    {
        StartListening();
    }
    
    void Update()
    {
        // 在主线程执行队列中的操作
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
            {
                mainThreadActions.Dequeue()?.Invoke();
            }
        }
    }
    
    // ==================== 开始广播 ====================
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
            int status = (int)RoomStatus.Idle;
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
    
    // ==================== 开始监听 ====================
    public void StartListening()
    {
        if (isListening) return;
        
        isListening = true;
        try
        {
            udpClient = new UdpClient(broadcastPort);
            udpClient.BeginReceive(OnReceive, null);
            Debug.Log("开始监听局域网广播");
        }
        catch (Exception e)
        {
            Debug.LogError($"监听启动失败：{e.Message}");
            isListening = false;
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
            int currentPlayers = int.Parse(info[2]);
            int maxPlayers = int.Parse(info[3]);
            RoomStatus status = (RoomStatus)int.Parse(info[4]);
            string gameMode = info[5];
            
            string ipAddress = endPoint.Address.ToString();
            int port = 7777;
            string serverId = $"{ipAddress}:{port}";
            
            Debug.Log($"发现房间：{roomName} @ {ipAddress} ({currentPlayers}/{maxPlayers}人)");
            
            // ===== 修复：通过主线程队列更新UI =====
            lock (mainThreadActions)
            {
                mainThreadActions.Enqueue(() => {
                    if (roomListController != null)
                    {
                        roomListController.AddRoom(serverId, ipAddress, port, roomName, 
                                                   hostName, currentPlayers, maxPlayers, 
                                                   status, gameMode);
                    }
                });
            }
            
            // 继续监听
            udpClient.BeginReceive(OnReceive, null);
        }
        catch (Exception e)
        {
            // ===== 修复：错误信息也通过主线程队列输出 =====
            string errorMsg = e.Message;
            lock (mainThreadActions)
            {
                mainThreadActions.Enqueue(() => {
                    Debug.LogError($"接收数据失败：{errorMsg}");
                });
            }
            
            // 继续监听
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
}