using System;

// ============================================================
// 注意：RoomStatus 枚举已移至 Assets/Scripts/Contract/GameEnums.cs（契约共享类型）
// 此文件仅保留 RoomItemData 类定义
// 后续可考虑直接使用契约中的 RoomInfo 结构体替代此类
// ============================================================

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
    public RoomStatus status;        // 房间状态（来自契约 GameEnums.cs）
    
    // 额外信息
    public string gameMode;          // 游戏模式
    public float ping;              // 延迟(ms)
    public string roomCode;          // 局域网短码（广播可选字段，旧包无此段则为空）
}