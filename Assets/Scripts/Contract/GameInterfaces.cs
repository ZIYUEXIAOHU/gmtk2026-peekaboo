// ============================================================
// 契约文件：程序 1 / 程序 2 接口
//   IGameStateReadonly / IPlayerStateReadonly  程序 1 写、程序 2 只读
//   IGameCommands   程序 2 发起 → 程序 1 裁定（对应 Mirror [Command]）
//   IGameEvents     程序 1 裁定后广播 → 程序 2 表现（对应 Mirror Rpc）
//   IRoom*          房间模块（主菜单：刷新/创建/加入/离开）
//
// 状态与事件的关系（两边实现都必须遵守）：
//   1. State 是最终权威快照；Events 只用于一次性表现，不承载状态
//   2. 程序 1 必须先更新 State，再触发对应 Event
//   3. 程序 2 初始化/重新绑定时先读 State 兜底，不依赖错过的事件
//   4. 所有事件在 Unity 主线程触发
//   5. 每个事件的注释标明接收范围（全员 / 仅发起者），Network 实现负责区分
//
// 修改规则：契约改动必须双方确认，并在同一次合并中同步更新
// Network 与 Mock 两个实现；枚举值不得重排或复用，DTO 字段
// 不得单方面改变类型和含义。
// ============================================================

using System;
using System.Collections.Generic;

/// <summary>全局对局状态（程序 1 权威，程序 2 只读，用于 HUD/流程界面）。</summary>
public interface IGameStateReadonly
{
    GamePhase Phase { get; }
    /// <summary>当前阶段剩余秒数（Prep: 40 起倒数；Playing: 250 起倒数）。</summary>
    float PhaseTimeLeft { get; }

    int AliveHiders { get; }
    int TotalHiders { get; }

    /// <summary>本地玩家状态（未进入对局时可为 null）。</summary>
    IPlayerStateReadonly LocalPlayer { get; }
    /// <summary>房间内全部玩家（含本地）。队友视角列表、玩家名单都从这里取。</summary>
    IReadOnlyList<IPlayerStateReadonly> Players { get; }
    /// <summary>本地玩家是否房主（控制「开始游戏」按钮显示）。</summary>
    bool IsLocalPlayerHost { get; }

    /// <summary>身份名额（小队房间界面：灰掉满员按钮、房主开始按钮可用性）。</summary>
    RoleSlots Slots { get; }

    /// <summary>是否处于练习大厅（Waiting 阶段：抓捕者随意劈砍、躲藏者无限复活随意放置）。</summary>
    bool IsPracticeLobby { get; }

    /// <summary>结算结果（Phase == Ended 时有效）。</summary>
    MatchResult Result { get; }
}

/// <summary>单个玩家的只读状态（程序 1 权威）。</summary>
public interface IPlayerStateReadonly
{
    uint NetId { get; }
    string PlayerName { get; }
    PlayerRole Role { get; }

    // ---- 躲藏者专用 ----
    HiderState HiderState { get; }
    /// <summary>当前伪装的物品 ID（Disguised/Invisible 态有效，其余为 GameConstants.InvalidItemId）。</summary>
    int DisguiseItemId { get; }
    /// <summary>物品栏剩余队列（只能按顺序放置，队首为下一个可放的）。</summary>
    IReadOnlyList<int> ItemQueue { get; }
}

/// <summary>程序 2 → 程序 1 的操作请求。程序 1 负责全部合法性校验，程序 2 不做权威判断。
/// 所有命令均为异步：结果通过 Events 回来，被拒绝时发 OnCommandRejected（PlaceItem 例外，走 OnPlaceResult）。
/// 注意：AD 左右移动、W 跳跃、S 跳下均不走本接口，
/// 由角色控制器 + Mirror 位置同步处理；抓捕者的空格键用于劈砍。
/// 队友视角不走本接口：纯本地镜头行为，程序 2 从 State.Players 筛选存活躲藏者即可。</summary>
public interface IGameCommands
{
    // ---- 小队房间 ----
    /// <summary>选择身份。拒绝原因：RoleFull / InvalidRole。</summary>
    void SelectRole(PlayerRole role);
    /// <summary>房主开始游戏。拒绝原因：NotHost / NotEnoughPlayers。</summary>
    void HostStartGame();

    // ---- 躲藏者 ----
    /// <summary>F 放置物品于脚下。程序 1 校验阶段（Prep 或练习大厅）、物品栏顺序、空间是否足够，
    /// 成功与失败都通过 OnPlaceResult 返回（不走 OnCommandRejected）。</summary>
    void PlaceItem();

    // ---- 抓捕者 ----
    /// <summary>F 调查。程序 1 选取范围内最近的可调查物体裁定，结果通过 OnInvestigated 广播。
    /// 拒绝原因：WrongPhase / WrongRole。</summary>
    void Investigate();
    /// <summary>空格劈砍。程序 1 裁定命中，结果通过 OnSlashed / OnCaptured 广播。
    /// 拒绝原因：WrongPhase / WrongRole。</summary>
    void Slash();
}

/// <summary>程序 1 → 程序 2 的事件广播。程序 2 只做表现（VFX/音效/UI），不改游戏状态。</summary>
public interface IGameEvents
{
    // ---- 流程 ----
    /// <summary>[全员] 阶段切换（含进入 Prep / Playing / Ended）。参数：新阶段、该阶段总时长。</summary>
    event Action<GamePhase, float> OnPhaseChanged;
    /// <summary>[全员] 身份名额变化（小队房间界面刷新）。</summary>
    event Action<RoleSlots> OnRoleSlotsChanged;
    /// <summary>[全员] 对局结算（展示胜负界面）。</summary>
    event Action<MatchResult> OnGameEnded;
    /// <summary>[仅发起者] 命令被拒绝（SelectRole / HostStartGame / Investigate / Slash）。
    /// 程序 2 据此弹提示；PlaceItem 的失败不走这里。</summary>
    event Action<CommandRejected> OnCommandRejected;

    // ---- 躲藏者 ----
    /// <summary>[成功→全员，失败→仅发起者] 放置结果。失败时程序 2 弹「无法放置」提示。</summary>
    event Action<PlaceItemResult> OnPlaceResult;
    /// <summary>[全员] 随机变化触发：换物品外观 + 播隐身无敌表现。</summary>
    event Action<TransformInfo> OnHiderTransformed;
    /// <summary>[全员] 练习大厅中躲藏者复活。</summary>
    event Action<RespawnInfo> OnHiderRespawned;

    // ---- 抓捕 ----
    /// <summary>[全员] 调查发生：播噪音反馈（对躲藏者可见）；命中则目标变鬼魂表现。</summary>
    event Action<InvestigateInfo> OnInvestigated;
    /// <summary>[全员] 劈砍发生：播劈砍特效。</summary>
    event Action<SlashInfo> OnSlashed;
    /// <summary>[全员] 捕获成功：目标进入 Captured，HUD 刷新存活数。</summary>
    event Action<CaptureInfo> OnCaptured;

    // ---- 心跳 ----
    /// <summary>[全员] 心跳脉冲：程序 2 让范围内物体跳动；躲藏者据此跟节奏跳跃。</summary>
    event Action<HeartbeatPulse> OnHeartbeatPulse;
}

// ============================================================
// 房间模块（主菜单 ↔ 小队房间之间的连接层）
// 程序 1 实现（基于 CustomNetworkManager / ManualDiscovery），程序 2 的主菜单 UI 调用
// ============================================================

/// <summary>房间连接状态（程序 1 权威，程序 2 只读）。</summary>
public interface IRoomStateReadonly
{
    RoomConnectionState ConnectionState { get; }
    /// <summary>最近一次刷新得到的房间列表（RoomInfo 为值类型快照，元素不可回写）。</summary>
    IReadOnlyList<RoomInfo> RoomList { get; }
}

/// <summary>程序 2（主菜单/局内菜单）→ 程序 1 的房间操作。</summary>
public interface IRoomCommands
{
    /// <summary>刷新房间列表，结果通过 OnRoomListUpdated 返回。</summary>
    void RefreshRoomList();
    /// <summary>创建房间并成为房主。maxPlayers 来自创建房间界面的最大人数选项。</summary>
    void CreateRoom(string roomName, int maxPlayers);
    /// <summary>加入指定房间（serverId 来自 RoomInfo.serverId）。</summary>
    void JoinRoom(string serverId);
    /// <summary>离开房间/断开连接，回主菜单（局内 ESC「返回主菜单」也走这里）。</summary>
    void LeaveRoom();
}

/// <summary>房间事件（均为本地：只发给发起操作的这台客户端）。</summary>
public interface IRoomEvents
{
    /// <summary>连接状态变化（主菜单/加载提示据此刷新）。</summary>
    event Action<RoomConnectionState> OnConnectionStateChanged;
    /// <summary>房间列表刷新完成。</summary>
    event Action<IReadOnlyList<RoomInfo>> OnRoomListUpdated;
    /// <summary>房间操作失败（刷新/创建/加入），程序 2 按 RoomError.reason 弹提示。</summary>
    event Action<RoomError> OnRoomError;
}
