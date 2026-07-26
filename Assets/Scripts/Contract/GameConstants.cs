// ============================================================
// 契约文件：数值常量（唯一定义处，两个程序都从这里引用）
// 修改规则：改动需双方确认
// ============================================================

using UnityEngine;

public static class GameConstants
{
    // ---- 移动 ----
    public const float HiderMoveSpeed = 0.3f;
    public const float SeekerMoveSpeed = 0.5f;

    // ---- 阶段时长（秒）----
    public const float PrepDuration = 10f;   // 躲藏者准备阶段
    public const float MatchDuration = 180f; // 正式对局

    /// <summary>
    /// Prep 阶段抓捕者近视：正交相机 Size（对局默认约 5）。
    /// 越小视野越近，准备阶段无法看清远处。
    /// </summary>
    public const float SeekerPrepOrthoSize = 2.4f;

    // ---- 躲藏者随机变化 ----
    public const float TransformInterval = 50f;             // 每 50 秒变身一次
    public const float InvulnerableDuration = 3f;           // 变身后隐身无敌 3 秒

    // ---- 抓捕者心跳（数值可调，但只在此处改）----
    public const float HeartbeatInterval = 1.0f;  // 节奏间隔
    /// <summary>心跳水平半轴。须与 InvestigateRangeX 一致。</summary>
    public const float HeartbeatRadiusX = 5.0f;
    /// <summary>心跳竖直半轴。须与 InvestigateRangeY 一致。</summary>
    public const float HeartbeatRadiusY = 7.0f;
    /// <summary>兼容旧名：心跳水平半轴。</summary>
    public const float HeartbeatRadius = HeartbeatRadiusX;

    // ---- 交互范围（数值可调，但只在此处改）----
    /// <summary>探测椭圆水平半轴（世界单位）。</summary>
    public const float InvestigateRangeX = 5.0f;
    /// <summary>探测椭圆竖直半轴（世界单位）。略大于水平，便于上下层调查。</summary>
    public const float InvestigateRangeY = 7.0f;
    /// <summary>兼容旧名：水平半轴。</summary>
    public const float InvestigateRange = InvestigateRangeX;
    /// <summary>F 调查：鼠标判定半径（物品约 2.6×3.0，取半幅量级）。</summary>
    public const float InvestigateCursorPickRadius = 2.0f;

    /// <summary>点是否在探测椭圆内（半轴 InvestigateRangeX / InvestigateRangeY）。</summary>
    public static bool IsInInvestigateRange(Vector2 origin, Vector2 point)
    {
        float nx = (point.x - origin.x) / InvestigateRangeX;
        float ny = (point.y - origin.y) / InvestigateRangeY;
        return nx * nx + ny * ny <= 1f;
    }

    /// <summary>点是否在心跳椭圆内（半轴 HeartbeatRadiusX / HeartbeatRadiusY）。</summary>
    public static bool IsInHeartbeatRange(Vector2 origin, Vector2 point)
    {
        float nx = (point.x - origin.x) / HeartbeatRadiusX;
        float ny = (point.y - origin.y) / HeartbeatRadiusY;
        return nx * nx + ny * ny <= 1f;
    }
    public const float SlashRange = 2.0f;         // 空格/身位劈砍范围
    /// <summary>鼠标攻击特效圆心半径（与身位圈并集裁定鬼魂）。</summary>
    public const float MouseSlashRange = 2.0f;
    /// <summary>鼠标特效点相对 Seeker 的最大距离（防恶意远程）。</summary>
    public const float MouseSlashMaxDistance = 40f;

    // ---- 鬼魂 ----
    // 鬼魂恢复时机 = 下一次随机变化，无独立时长；此处不定义恢复时间

    // ---- 物品显示尺度（略小于 RoomPlayer 根节点 3.5×4，伪装/放置物共用）----
    public const float ItemScaleX = 2.6f;
    public const float ItemScaleY = 3.0f;
    public const float ItemScaleZ = 2.6f;

    /// <summary>
    /// BoxCollider2D 圆角的世界半径。直角底边易卡小突起；运行时换算为本地 edgeRadius。
    /// </summary>
    public const float ColliderEdgeRadiusWorld = 0.35f;

    // ---- 放置物物理（与 Hider 重力一致，质量略轻便于推动）----
    public const float ItemGravityScale = 3f;
    public const float ItemMass = 0.45f;
    public const float ItemLinearDrag = 0.8f;
    /// <summary>楼梯区内放置物被推向水平中心的水平力（ForceMode.Force）。</summary>
    public const float StairItemCenterPushForce = 3.5f;

    // ---- 本地玩家档案（PlayerPrefs，仅本机）----
    public const string DefaultPlayerName = "Shui";
    public const int MaxPlayerNameLength = 16;
    public const string PlayerNamePrefsKey = "PlayerName";

    public const string MasterVolumePrefsKey = "MasterVolume";
    public const string MusicVolumePrefsKey = "MusicVolume";
    public const string SFXVolumePrefsKey = "SFXVolume";
    public const float DefaultMasterVolume = 0.8f;
    public const float DefaultMusicVolume = 0.3f;
    public const float DefaultSFXVolume = 0.6f;

    // ---- 得分（仅 Playing 阶段累计；练习大厅不计分）----
    /// <summary>抓捕者：调查出一名躲藏者（变鬼魂）+ 分。</summary>
    public const int SeekerScorePerInvestigate = 50;
    /// <summary>抓捕者：击杀一名鬼魂（捕获）+ 分。</summary>
    public const int SeekerScorePerKill = 50;
    /// <summary>抓捕者：调查到放置物/诱饵 − 分（不低于 0）。</summary>
    public const int SeekerScorePenaltyPlacedItem = 10;
    /// <summary>躲藏者：存活躲藏每满 1 秒 + 分（含 Ghost，Captured 后停止）。</summary>
    public const int HiderScorePerSecond = 2;

    // ---- ID 约定（详见 接口契约.md「ID 约定」）----
    /// <summary>itemId 无效值。有效 itemId = 共享物品表（ItemTable，双方引用同一份资产）中的索引。</summary>
    public const int InvalidItemId = -1;
    /// <summary>netId 无效值。所有可调查物体必须有 Mirror NetworkIdentity。</summary>
    public const uint InvalidNetId = 0;
}
