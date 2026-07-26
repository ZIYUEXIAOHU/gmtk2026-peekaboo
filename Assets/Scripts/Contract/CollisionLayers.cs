// ============================================================
// 物理层约定：玩家互不碰撞；放置物仅与躲藏者固体碰撞。
// ============================================================

using UnityEngine;

public static class CollisionLayers
{
    public const string Hider = "Hider";
    public const string Seeker = "Seeker";
    public const string HiderItem = "HiderItem";
    public const string Ground = "Ground";
    public const string Player = "Player";
    public const string Default = "Default";

    /// <summary>放置瞬间与脚下重叠的躲藏者短暂忽略碰撞，避免卡在物体里。</summary>
    public const float PlaceOverlapIgnoreSeconds = 0.75f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InitLayerCollisionMatrix() => EnsureCollisionMatrix();

    /// <summary>可重复调用；保证层忽略矩阵生效。</summary>
    public static void EnsureCollisionMatrix()
    {
        int hider = LayerMask.NameToLayer(Hider);
        int seeker = LayerMask.NameToLayer(Seeker);
        int item = LayerMask.NameToLayer(HiderItem);
        int player = LayerMask.NameToLayer(Player);

        if (hider < 0 || seeker < 0 || item < 0)
        {
            Debug.LogWarning(
                "[CollisionLayers] Hider/Seeker/HiderItem 层缺失，放置物与抓捕者碰撞过滤可能失效。");
            return;
        }

        // 玩家之间不碰撞
        Physics2D.IgnoreLayerCollision(hider, hider, true);
        Physics2D.IgnoreLayerCollision(seeker, seeker, true);
        Physics2D.IgnoreLayerCollision(hider, seeker, true);
        if (player >= 0)
        {
            Physics2D.IgnoreLayerCollision(player, player, true);
            Physics2D.IgnoreLayerCollision(player, hider, true);
            Physics2D.IgnoreLayerCollision(player, seeker, true);
            Physics2D.IgnoreLayerCollision(item, player, true);
        }

        // 放置物不与抓捕者、其它放置物碰撞（只留给躲藏者）
        Physics2D.IgnoreLayerCollision(item, seeker, true);
        Physics2D.IgnoreLayerCollision(item, item, true);
    }

    public static void ApplyPlayerRoleLayer(GameObject go, PlayerRole role)
    {
        if (go == null)
            return;

        EnsureCollisionMatrix();

        switch (role)
        {
            case PlayerRole.Hider:
                SetLayer(go, Hider);
                ConfigureHiderCollider(go);
                break;
            case PlayerRole.Seeker:
                SetLayer(go, Seeker);
                ConfigureSeekerCollider(go);
                break;
            default:
                SetLayer(go, Player);
                ConfigureNeutralPlayerCollider(go);
                break;
        }
    }

    public static void ConfigurePlacedItem(GameObject go)
    {
        if (go == null)
            return;

        EnsureCollisionMatrix();
        SetLayer(go, HiderItem);

        // 与躲藏者伪装世界尺度一致
        go.transform.localScale = new Vector3(
            GameConstants.ItemScaleX,
            GameConstants.ItemScaleY,
            GameConstants.ItemScaleZ);

        BoxCollider2D col = go.GetComponent<BoxCollider2D>();
        if (col == null)
            return;

        col.isTrigger = false;
        // 显式排除抓捕者等；不依赖仅 IgnoreLayerCollision（角色层偶发未切到 Seeker 时仍会撞 Default/Player）
        col.excludeLayers = Mask(Seeker, Player, HiderItem, Default);
        IgnoreColliderAgainstAllSeekers(col);
    }

    static void ConfigureSeekerCollider(GameObject go)
    {
        Collider2D col = go.GetComponent<Collider2D>();
        if (col == null)
            return;

        // 抓捕者不与放置物、其它玩家固体碰撞
        col.excludeLayers = Mask(HiderItem, Hider, Seeker, Player);
        IgnoreColliderAgainstAllItems(col);
    }

    static void ConfigureHiderCollider(GameObject go)
    {
        Collider2D col = go.GetComponent<Collider2D>();
        if (col == null)
            return;

        // 躲藏者仍与 HiderItem 碰撞；只排除其它玩家
        col.excludeLayers = Mask(Hider, Seeker, Player);
    }

    static void ConfigureNeutralPlayerCollider(GameObject go)
    {
        Collider2D col = go.GetComponent<Collider2D>();
        if (col == null)
            return;

        col.excludeLayers = Mask(Hider, Seeker, Player, HiderItem);
    }

    static void IgnoreColliderAgainstAllSeekers(Collider2D itemCol)
    {
        if (itemCol == null)
            return;

        RoomPlayer[] players = Object.FindObjectsOfType<RoomPlayer>();
        for (int i = 0; i < players.Length; i++)
        {
            RoomPlayer rp = players[i];
            if (rp == null || rp.role != PlayerRole.Seeker)
                continue;
            Collider2D seekerCol = rp.GetComponent<Collider2D>();
            if (seekerCol != null)
                Physics2D.IgnoreCollision(itemCol, seekerCol, true);
        }
    }

    static void IgnoreColliderAgainstAllItems(Collider2D seekerCol)
    {
        if (seekerCol == null)
            return;

        InvestigableObject[] items = Object.FindObjectsOfType<InvestigableObject>();
        for (int i = 0; i < items.Length; i++)
        {
            InvestigableObject item = items[i];
            if (item == null)
                continue;
            Collider2D itemCol = item.GetComponent<Collider2D>();
            if (itemCol != null)
                Physics2D.IgnoreCollision(seekerCol, itemCol, true);
        }
    }

    static LayerMask Mask(params string[] layerNames)
    {
        return LayerMask.GetMask(layerNames);
    }

    static void SetLayer(GameObject go, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
        {
            Debug.LogWarning($"[CollisionLayers] 缺少 Layer「{layerName}」，请在 Tag Manager 中添加。");
            return;
        }

        go.layer = layer;
    }
}
