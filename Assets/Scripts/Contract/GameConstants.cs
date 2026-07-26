// ============================================================
// 契约文件：数值常量（唯一定义处，两个程序都从这里引用）
// 修改规则：改动需双方确认
// ============================================================

public static class GameConstants
{
    // ---- 移动 ----
    public const float HiderMoveSpeed = 0.3f;
    public const float SeekerMoveSpeed = 0.5f;

    // ---- 阶段时长（秒）----
    public const float PrepDuration = 40f;    // 躲藏者准备阶段
    public const float MatchDuration = 250f;  // 正式对局

    // ---- 躲藏者随机变化 ----
    public const float TransformInterval = 50f;             // 每 50 秒变身一次
    public const float InvulnerableDuration = 3f;           // 变身后隐身无敌 3 秒

    // ---- 抓捕者心跳（数值可调，但只在此处改）----
    public const float HeartbeatInterval = 1.0f;  // 节奏间隔
    /// <summary>心跳跳动半径。须与 InvestigateRange 一致：探测圈内物品/伪装躲藏者一起跳。</summary>
    public const float HeartbeatRadius = 5.0f;

    // ---- 交互范围（数值可调，但只在此处改）----
    /// <summary>探测圈 / F 调查范围（世界单位）。角色根节点约 3.5×4，过小会导致「贴脸也调查不到」。</summary>
    public const float InvestigateRange = 5.0f;
    /// <summary>F 调查：鼠标判定半径（物品约 3×3.4，取半幅量级）。</summary>
    public const float InvestigateCursorPickRadius = 2.0f;
    public const float SlashRange = 2.0f;         // 空格/身位劈砍范围
    /// <summary>鼠标攻击特效圆心半径（与身位圈并集裁定鬼魂）。</summary>
    public const float MouseSlashRange = 2.0f;
    /// <summary>鼠标特效点相对 Seeker 的最大距离（防恶意远程）。</summary>
    public const float MouseSlashMaxDistance = 40f;

    // ---- 鬼魂 ----
    // 鬼魂恢复时机 = 下一次随机变化，无独立时长；此处不定义恢复时间

    // ---- 物品显示尺度（略小于 RoomPlayer 根节点 3.5×4，伪装/放置物共用）----
    public const float ItemScaleX = 3.0f;
    public const float ItemScaleY = 3.4f;
    public const float ItemScaleZ = 3.0f;

    // ---- 展示名（仅 UI，不参与程序身份区分）----
    public const string DefaultPlayerName = "Shui";
    public const int MaxPlayerNameLength = 16;
    public const string PlayerNamePrefsKey = "PlayerName";

    // ---- ID 约定（详见 接口契约.md「ID 约定」）----
    /// <summary>itemId 无效值。有效 itemId = 共享物品表（ItemTable，双方引用同一份资产）中的索引。</summary>
    public const int InvalidItemId = -1;
    /// <summary>netId 无效值。所有可调查物体必须有 Mirror NetworkIdentity。</summary>
    public const uint InvalidNetId = 0;
}
