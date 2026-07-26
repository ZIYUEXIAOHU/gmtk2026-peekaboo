// ============================================================
// 契约文件：枚举（两个程序共用，禁止单方修改语义）
// ============================================================

/// <summary>对局阶段。程序 1 权威推进，程序 2 只读。</summary>
public enum GamePhase
{
    Waiting = 0,  // 小队房间（练习大厅），等待房主开始
    Prep = 1,     // 准备阶段：10 秒，躲藏者分散 + 放置物品
    Playing = 2,  // 正式对局：180 秒
    Ended = 3,    // 已结算
}

/// <summary>玩家身份。</summary>
public enum PlayerRole
{
    None = 0,   // 未选择
    Hider = 1,  // 躲藏者
    Seeker = 2, // 抓捕者
}

/// <summary>躲藏者当前形态。程序 1 权威，程序 2 据此做表现。</summary>
public enum HiderState
{
    Disguised = 0,  // 伪装成物品（默认态）
    Invisible = 1,  // 变身后的隐身无敌 3 秒
    Ghost = 2,      // 被调查命中，变回本体（鬼魂），可被劈砍
    Captured = 3,   // 已被捕获（进入队友视角）
}

/// <summary>放置物品失败原因。PlaceItem 的所有失败都走 OnPlaceResult，不走 OnCommandRejected。</summary>
public enum PlaceFailReason
{
    None = 0,        // 成功
    NoSpace = 1,     // 空间不足（PDF：提示无法放置）
    NotPrepPhase = 2,// 不在准备阶段（练习大厅除外）
    NoItemLeft = 3,  // 物品栏已空
    WrongRole = 4,   // 非躲藏者（或未选身份）调用
}

/// <summary>对局结果。</summary>
public enum GameResult
{
    None = 0,
    HidersWin = 1,   // 180 秒结束仍有存活躲藏者
    SeekersWin = 2,  // 躲藏者全部被捕
}

/// <summary>可被拒绝的命令类型（用于 OnCommandRejected）。
/// 注意：PlaceItem 的失败不走这里，走 OnPlaceResult（含更细的 PlaceFailReason）。</summary>
public enum GameCommandType
{
    Unknown = 0,      // 默认值哨兵，避免未赋值的 struct 被误读
    SelectRole = 1,
    HostStartGame = 2,
    Investigate = 3,
    Slash = 4,
    ReturnToWaiting = 5, // 结算后回练习大厅
}

/// <summary>命令被拒绝的原因（程序 1 裁定后回给发起者）。</summary>
public enum RejectReason
{
    None = 0,
    RoleFull = 1,         // 身份已满
    InvalidRole = 2,      // 身份非法（如选 None）
    NotHost = 3,          // 不是房主
    NotEnoughPlayers = 4, // 开局人数不满足（需 ≥1 抓捕者 + ≥1 躲藏者）
    WrongPhase = 5,       // 当前阶段不允许该操作
    WrongRole = 6,        // 当前身份不允许该操作
    InvalidTarget = 7,    // 目标无效
    PlayersNotReady = 8,  // 尚有玩家未选身份或未准备
}

/// <summary>房间连接状态。</summary>
public enum RoomConnectionState
{
    Disconnected = 0, // 未连接（主菜单）
    Connecting = 1,   // 创建/加入中
    InRoom = 2,       // 已在小队房间
    Failed = 3,       // 连接失败（配合 OnRoomError）
}

/// <summary>房间操作类型（用于 OnRoomError）。</summary>
public enum RoomOp
{
    Unknown = 0,  // 默认值哨兵
    Refresh = 1,
    Create = 2,
    Join = 3,
    Find = 4,     // FindRoomByCode / 进房前选身份
}

/// <summary>房间状态（房间列表条目用；原定义在 Data/RoomItemData.cs，为使契约自包含移到此处）。</summary>
public enum RoomStatus
{
    Idle = 0,     // 空闲中 - 可以加入游戏
    Playing = 1,  // 游戏中 - 只能观战
    Settling = 2, // 结算中 - 只能观战
}

/// <summary>房间操作失败原因。提示文案由程序 2 根据枚举决定，RoomError.message 仅作调试。</summary>
public enum RoomErrorReason
{
    Unknown = 0,
    Timeout = 1,          // 超时
    RoomNotFound = 2,     // 房间不存在/已关闭
    RoomFull = 3,         // 房间已满
    ConnectionFailed = 4, // 网络连接失败
    AlreadyInRoom = 5,    // 已在房间中重复操作
    SlotFull = 6,         // 身份名额已满（UI 显示「XX已满」类提示，非字面 slotfull）
    RoleNotSelected = 7,  // 进房前未选择身份
}
