using System;

[Serializable]
public class RoomItemData
{
    public string serverId;          // 服务器唯一ID
    public string ipAddress;         // IP地址
    public int port;                 // 端口
    public string roomName;          // 房间名称
    public string hostName;          // 主机名称
    public int currentPlayers;       // 当前人数
    public int maxPlayers;           // 最大人数
    public RoomStatus status;        // 房间状态
    
    // 额外信息
    public string gameMode;          // 游戏模式
    public float ping;              // 延迟(ms)
}

// 房间状态枚举
public enum RoomStatus
{
    Idle,       // 空闲中 - 可以加入游戏
    Playing,    // 游戏中 - 只能观战
    Settling    // 结算中 - 只能观战
}