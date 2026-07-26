// ============================================================
// 物理层约定：躲藏者之间可固体碰撞（叠跳）；抓捕者互不撞、躲藏者↔抓捕者不撞；
// 放置物与躲藏者、地面/场景、其它放置物固体碰撞（可下落、可推动）。
// ============================================================

using UnityEngine;
using UnityEngine.SceneManagement;

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
    static void InitLayerCollisionMatrix()
    {
        EnsureCollisionMatrix();
        SceneManager.sceneLoaded -= OnSceneLoadedRoundColliders;
        SceneManager.sceneLoaded += OnSceneLoadedRoundColliders;
    }

    static void OnSceneLoadedRoundColliders(Scene scene, LoadSceneMode mode)
    {
        ApplyRoundingToEnvironmentColliders();
    }

    /// <summary>可重复调用；保证层忽略 / HiderItem 白名单生效。</summary>
    public static void EnsureCollisionMatrix()
    {
        int hider = LayerMask.NameToLayer(Hider);
        int seeker = LayerMask.NameToLayer(Seeker);
        int item = LayerMask.NameToLayer(HiderItem);
        int player = LayerMask.NameToLayer(Player);
        int ground = LayerMask.NameToLayer(Ground);
        int defaultLayer = LayerMask.NameToLayer(Default);

        if (hider < 0 || seeker < 0 || item < 0)
        {
            Debug.LogWarning(
                "[CollisionLayers] Hider/Seeker/HiderItem 层缺失，放置物与抓捕者碰撞过滤可能失效。");
            return;
        }

        // 躲藏者互撞（支持站队友起跳）；抓捕者互不撞；躲藏者↔抓捕者不撞
        Physics2D.IgnoreLayerCollision(hider, hider, false);
        Physics2D.IgnoreLayerCollision(seeker, seeker, true);
        Physics2D.IgnoreLayerCollision(hider, seeker, true);
        if (player >= 0)
        {
            Physics2D.IgnoreLayerCollision(player, player, true);
            Physics2D.IgnoreLayerCollision(player, hider, true);
            Physics2D.IgnoreLayerCollision(player, seeker, true);
        }

        // HiderItem 白名单：Hider、Ground、Default、其它放置物
        int itemMask = 1 << hider;
        itemMask |= 1 << item;
        if (ground >= 0)
            itemMask |= 1 << ground;
        if (defaultLayer >= 0)
            itemMask |= 1 << defaultLayer;
        Physics2D.SetLayerCollisionMask(item, itemMask);

        // 双保险：显式忽略 Seeker / Player；放置物互撞开启（可堆叠/互推）
        Physics2D.IgnoreLayerCollision(item, seeker, true);
        Physics2D.IgnoreLayerCollision(item, item, false);
        if (player >= 0)
            Physics2D.IgnoreLayerCollision(item, player, true);
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

        ConfigurePlacedItemRigidbody(go);

        BoxCollider2D col = go.GetComponent<BoxCollider2D>();
        if (col == null)
            return;

        col.isTrigger = false;
        // 碰撞范围交给 SetLayerCollisionMask；此处不再排除 Default（以免误伤未切层的 Hider）
        col.excludeLayers = 0;
        col.includeLayers = 0;

        // 每次从贴图重建，避免重复 Configure 时叠乘；比 sprite 略大便于站立/推动
        ApplyItemColliderFromSprite(go, col);
        ApplyColliderRounding(col);
        IgnoreColliderAgainstAllSeekers(col);
    }

    /// <summary>按 Sprite.bounds × ItemColliderScaleX/Y 写入碰撞箱（幂等）。</summary>
    public static void ApplyItemColliderFromSprite(GameObject go, BoxCollider2D col)
    {
        if (go == null || col == null)
            return;

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null)
            return;

        Bounds b = sr.sprite.bounds;
        col.edgeRadius = 0f;
        col.size = new Vector2(
            b.size.x * GameConstants.ItemColliderScaleX,
            b.size.y * GameConstants.ItemColliderScaleY);
        col.offset = b.center;
    }

    /// <summary>放置物动态刚体：下落、可被躲藏者推动。</summary>
    public static Rigidbody2D ConfigurePlacedItemRigidbody(GameObject go)
    {
        if (go == null)
            return null;

        Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = go.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.mass = GameConstants.ItemMass;
        rb.gravityScale = GameConstants.ItemGravityScale;
        rb.drag = GameConstants.ItemLinearDrag;
        rb.angularDrag = 0.05f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        PhysicsMaterial2D noFriction = Resources.Load<PhysicsMaterial2D>("Prefabs/NoFriction");
        if (noFriction != null)
            rb.sharedMaterial = noFriction;

        return rb;
    }

    static void ConfigureSeekerCollider(GameObject go)
    {
        Collider2D col = go.GetComponent<Collider2D>();
        if (col == null)
            return;

        // 抓捕者不与放置物、其它玩家固体碰撞
        col.excludeLayers = Mask(HiderItem, Hider, Seeker, Player);
        if (col is BoxCollider2D box)
        {
            ApplyColliderRounding(box);
            SyncGroundCheckToColliderBottom(go, box);
        }
        IgnoreColliderAgainstAllItems(col);
    }

    static void ConfigureHiderCollider(GameObject go)
    {
        Collider2D col = go.GetComponent<Collider2D>();
        if (col == null)
            return;

        // 排除抓捕者/中立玩家；允许与其它躲藏者、HiderItem 固体碰撞
        col.excludeLayers = Mask(Seeker, Player);
        if (col is BoxCollider2D box)
        {
            ApplyColliderRounding(box);
            SyncGroundCheckToColliderBottom(go, box);
        }
    }

    static void ConfigureNeutralPlayerCollider(GameObject go)
    {
        Collider2D col = go.GetComponent<Collider2D>();
        if (col == null)
            return;

        col.excludeLayers = Mask(Hider, Seeker, Player, HiderItem);
        if (col is BoxCollider2D box)
        {
            ApplyColliderRounding(box);
            SyncGroundCheckToColliderBottom(go, box);
        }
    }

    /// <summary>
    /// 给场景里尚未由角色/放置物配置过的 BoxCollider2D 加圆角。
    /// 环境物体常用极大非均匀 Scale，须用 max(scale) 换算，避免水平圆角被放大到几十单位。
    /// </summary>
    public static void ApplyRoundingToEnvironmentColliders()
    {
        BoxCollider2D[] cols = Object.FindObjectsOfType<BoxCollider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            BoxCollider2D col = cols[i];
            if (col == null)
                continue;

            // 玩家与放置物由 Configure* / HiderDisguiseVisual 处理
            if (col.GetComponent<RoomPlayer>() != null ||
                col.GetComponent<InvestigableObject>() != null)
                continue;

            ApplyColliderRounding(col, useMinScale: false);
        }
    }

    /// <summary>
    /// 给 BoxCollider2D 加 edgeRadius 圆角，外轮廓大致不变（可重复调用）。
    /// 角色/物品用 min(scale) 保证较短轴达到世界半径；环境用 max(scale) 防止大 Scale 轴圆角爆炸。
    /// </summary>
    public static void ApplyColliderRounding(BoxCollider2D col)
    {
        ApplyColliderRounding(col, useMinScale: true);
    }

    public static void ApplyColliderRounding(BoxCollider2D col, bool useMinScale)
    {
        if (col == null)
            return;

        Vector2 outer = col.size + new Vector2(2f * col.edgeRadius, 2f * col.edgeRadius);

        float scaleX = Mathf.Max(Mathf.Abs(col.transform.lossyScale.x), 0.0001f);
        float scaleY = Mathf.Max(Mathf.Abs(col.transform.lossyScale.y), 0.0001f);
        float scaleFactor = useMinScale ? Mathf.Min(scaleX, scaleY) : Mathf.Max(scaleX, scaleY);
        float localRadius = GameConstants.ColliderEdgeRadiusWorld / scaleFactor;

        // 扁物体（如茶几、薄平台）限制圆角，避免核心矩形退化
        float maxRadius = Mathf.Min(outer.x, outer.y) * 0.4f;
        localRadius = Mathf.Clamp(localRadius, 0f, maxRadius);

        col.size = new Vector2(
            Mathf.Max(outer.x - 2f * localRadius, 0.01f),
            Mathf.Max(outer.y - 2f * localRadius, 0.01f));
        col.edgeRadius = localRadius;
    }

    public static void SyncGroundCheckToColliderBottom(GameObject go, BoxCollider2D col)
    {
        if (go == null || col == null)
            return;

        Transform groundCheck = go.transform.Find("GroundCheck");
        if (groundCheck == null)
            return;

        float bottom = col.offset.y - col.size.y * 0.5f - col.edgeRadius;
        groundCheck.localPosition = new Vector3(col.offset.x, bottom - 0.02f, 0f);
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
