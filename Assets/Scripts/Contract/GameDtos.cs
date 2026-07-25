// ============================================================
// 契约文件：事件数据结构（程序 1 裁定后广播给程序 2 做表现）
// 全部为纯数据 struct，字段类型限定 Mirror 可序列化类型
// ============================================================

using UnityEngine;

/// <summary>身份名额（小队房间用：满员灰掉、开局条件）。</summary>
public struct RoleSlots
{
    public int seekerCount;
    public int seekerMax;
    public int hiderCount;
    public int hiderMax;

    public bool SeekerFull => seekerCount >= seekerMax;
    public bool HiderFull => hiderCount >= hiderMax;
    /// <summary>房主开局条件：至少 1 抓捕者 + 1 躲藏者。</summary>
    public bool CanStart => seekerCount >= 1 && hiderCount >= 1;

    /// <summary>与 NetworkGameState 相同的名额上限算法（按房内总人数）。</summary>
    public static void ComputeRoleMax(int totalPlayers, out int seekerMaxOut, out int hiderMaxOut)
    {
        if (totalPlayers < 2)
        {
            seekerMaxOut = 1;
            hiderMaxOut = 1;
            return;
        }

        seekerMaxOut = Mathf.Max(1, totalPlayers / 3);
        hiderMaxOut = totalPlayers - seekerMaxOut;
        if (hiderMaxOut < 1) hiderMaxOut = 1;
    }

    /// <summary>
    /// 加入方进房前预览：按「当前人数 + 1」投影 max，count 用房内已选身份数。
    /// </summary>
    public static RoleSlots ProjectForJoiner(int currentPlayers, int seekerCount, int hiderCount)
    {
        int projectedTotal = Mathf.Max(1, currentPlayers + 1);
        ComputeRoleMax(projectedTotal, out int sMax, out int hMax);
        return new RoleSlots
        {
            seekerCount = Mathf.Max(0, seekerCount),
            seekerMax = sMax,
            hiderCount = Mathf.Max(0, hiderCount),
            hiderMax = hMax,
        };
    }
}

/// <summary>放置物品的结果（成功则全员可见，失败只回给发起者）。</summary>
public struct PlaceItemResult
{
    public uint hiderNetId;
    public bool success;
    public PlaceFailReason failReason;
    public int itemId;          // 成功时：放下的物品 ID
    public Vector2 position;    // 成功时：放置位置
}

/// <summary>调查结果（F）。噪音对所有躲藏者可见。</summary>
public struct InvestigateInfo
{
    public uint seekerNetId;
    public uint targetNetId;     // 被调查的物体/玩家
    public bool hitHider;        // true = 命中躲藏者，其变为鬼魂
    public Vector2 noisePosition;// 噪音位置（程序 2 做躲藏者可见的反馈）
}

/// <summary>躲藏者随机变化（每 50 秒）。</summary>
public struct TransformInfo
{
    public uint hiderNetId;
    public int newItemId;            // 变成的新物品
    /// <summary>无敌结束时刻（服务端 NetworkTime.time）。程序 2 用
    /// invulnerableUntil - NetworkTime.time 计算剩余时长，避免网络延迟把无敌表现拖长。</summary>
    public double invulnerableUntil;
}

/// <summary>劈砍结果（空格）。</summary>
public struct SlashInfo
{
    public uint seekerNetId;
    public bool hitGhost;      // true = 砍中鬼魂 → 触发 CaptureInfo
    public uint targetNetId;   // 砍中的目标（未命中为 0）
    public Vector2 position;   // 劈砍位置（程序 2 做特效）
}

/// <summary>捕获成功（劈砍命中鬼魂后由程序 1 发出）。</summary>
public struct CaptureInfo
{
    public uint hiderNetId;
    public uint seekerNetId;
    public int aliveHiders;    // 剩余存活躲藏者（0 = 触发结算）
}

/// <summary>心跳脉冲（固定节奏，围绕每个抓捕者）。</summary>
public struct HeartbeatPulse
{
    public uint seekerNetId;
    public Vector2 center;
    public float radius;
    public int beatIndex;      // 节拍序号，程序 2 对齐动画
    public double serverTime;  // 发出时的 NetworkTime.time，两端对齐节拍、抵抗网络抖动
}

/// <summary>命令被拒绝（仅发给发起者）。PlaceItem 的失败走 PlaceItemResult。</summary>
public struct CommandRejected
{
    public GameCommandType command;
    public RejectReason reason;
}

/// <summary>练习大厅复活信息。</summary>
public struct RespawnInfo
{
    public uint hiderNetId;
    public Vector2 position;
    public int itemId;         // 复活后的伪装物品 ID
}

/// <summary>房间操作失败（本地事件）。提示文案由程序 2 按 reason 决定。</summary>
public struct RoomError
{
    public RoomOp op;
    public RoomErrorReason reason;
    public string message;     // 仅调试用，不作为用户提示文案
}

/// <summary>房间列表条目的只读快照（值类型，程序 2 无法改动源数据）。
/// 由程序 1 从内部的 RoomItemData 转换而来；连接用 serverId 调 JoinRoom。</summary>
public struct RoomInfo
{
    public string serverId;
    public string roomName;
    public string hostName;
    public int currentPlayers;
    public int maxPlayers;
    public RoomStatus status;  // 枚举定义在 GameEnums.cs
    public float ping;         // 延迟 ms
    public string roomCode;    // 局域网短码（可空）
    public int seekerCount;    // 已选抓捕者人数（广播）
    public int hiderCount;     // 已选躲藏者人数（广播）

    /// <summary>加入方用的投影名额（currentPlayers+1）；创建/列表展示也可用。</summary>
    public RoleSlots ProjectedSlotsForJoiner =>
        RoleSlots.ProjectForJoiner(currentPlayers, seekerCount, hiderCount);
}

/// <summary>对局结算。</summary>
public struct MatchResult
{
    public GameResult result;
    public int survivors;      // 存活躲藏者数
    public float duration;     // 对局用时
}
