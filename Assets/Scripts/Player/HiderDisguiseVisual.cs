using Mirror;
using UnityEngine;

/// <summary>
/// 客户端表现：根据 RoomPlayer.disguiseItemId / hiderState 切换 Visual_Hider 外观与透明度，
/// 并按伪装物品尺寸同步根节点 BoxCollider2D（与摆放物一致）。
/// Ghost 态恢复躲藏者原型循环动画与默认碰撞箱，供 Seeker 劈砍。
/// 挂在 RoomPlayerPrefab 根上；不依赖 NetworkBehaviour。
/// </summary>
public class HiderDisguiseVisual : MonoBehaviour
{
    [Header("引用（可空，运行时自动查找）")]
    [SerializeField] RoomPlayer roomPlayer;
    [SerializeField] SpriteRenderer hiderSpriteRenderer;
    [SerializeField] Animator hiderAnimator;
    [SerializeField] HiderPrototypeFloat prototypeFloat;
    [SerializeField] BoxCollider2D bodyCollider;
    [SerializeField] ItemTable itemTable;

    [Header("透明度")]
    [SerializeField] [Range(0f, 1f)] float invisibleLocalAlpha = 0.35f;

    [Header("碰撞箱")]
    [Tooltip("碰撞箱相对物品尺寸的缩放（1=完全贴合）")]
    [SerializeField] float colliderScale = 1f;
    [SerializeField] Vector2 minColliderSize = new Vector2(0.25f, 0.25f);
    [SerializeField] Vector2 defaultColliderSize = new Vector2(1f, 1f);
    [SerializeField] Vector2 defaultColliderOffset = Vector2.zero;

    Sprite defaultSprite;
    Vector3 defaultVisualLocalScale = Vector3.one;
    int lastItemId = int.MinValue;
    HiderState lastState = (HiderState)(-1);
    PlayerRole lastRole = (PlayerRole)(-1);
    bool lastIsLocal;

    void Awake()
    {
        CacheRefs();
        if (hiderSpriteRenderer != null)
        {
            defaultSprite = hiderSpriteRenderer.sprite;
            defaultVisualLocalScale = hiderSpriteRenderer.transform.localScale;
        }

        if (bodyCollider != null)
        {
            // 存外轮廓（含已有 edgeRadius），Restore 后再 ApplyColliderRounding，避免二次缩小
            defaultColliderSize = bodyCollider.size
                + new Vector2(2f * bodyCollider.edgeRadius, 2f * bodyCollider.edgeRadius);
            defaultColliderOffset = bodyCollider.offset;
        }
    }

    void OnEnable()
    {
        CacheRefs();
        ForceRefresh();
    }

    void Update()
    {
        CacheRefs();
        if (roomPlayer == null || hiderSpriteRenderer == null)
            return;

        bool isLocal = roomPlayer.isLocalPlayer;
        if (roomPlayer.role == lastRole
            && roomPlayer.disguiseItemId == lastItemId
            && roomPlayer.hiderState == lastState
            && isLocal == lastIsLocal)
            return;

        ApplyVisual(isLocal);
    }

    void CacheRefs()
    {
        if (roomPlayer == null)
            roomPlayer = GetComponent<RoomPlayer>();

        if (bodyCollider == null)
            bodyCollider = GetComponent<BoxCollider2D>();

        if (hiderSpriteRenderer == null)
        {
            if (roomPlayer != null && roomPlayer.visualHider != null)
                hiderSpriteRenderer = roomPlayer.visualHider.GetComponent<SpriteRenderer>();
            if (hiderSpriteRenderer == null)
            {
                Transform t = transform.Find("Visual_Hider");
                if (t != null)
                    hiderSpriteRenderer = t.GetComponent<SpriteRenderer>();
            }
        }

        if (hiderAnimator == null && hiderSpriteRenderer != null)
            hiderAnimator = hiderSpriteRenderer.GetComponent<Animator>();
        if (hiderAnimator == null)
        {
            Transform t = transform.Find("Visual_Hider");
            if (t != null)
                hiderAnimator = t.GetComponent<Animator>();
        }

        if (prototypeFloat == null && hiderSpriteRenderer != null)
            prototypeFloat = hiderSpriteRenderer.GetComponent<HiderPrototypeFloat>();
        if (prototypeFloat == null)
        {
            Transform t = transform.Find("Visual_Hider");
            if (t != null)
                prototypeFloat = t.GetComponent<HiderPrototypeFloat>();
        }

        if (itemTable == null)
            itemTable = Resources.Load<ItemTable>("ItemTable");
    }

    void ForceRefresh()
    {
        lastItemId = int.MinValue;
        lastState = (HiderState)(-1);
        lastRole = (PlayerRole)(-1);
    }

    void ApplyVisual(bool isLocal)
    {
        lastRole = roomPlayer.role;
        lastItemId = roomPlayer.disguiseItemId;
        lastState = roomPlayer.hiderState;
        lastIsLocal = isLocal;

        if (roomPlayer.role != PlayerRole.Hider)
        {
            SetPrototypeLoop(false);
            RestoreDefaultCollider();
            RestoreDefaultVisualScale();
            return;
        }

        Color c = hiderSpriteRenderer.color;
        c.r = 1f;
        c.g = 1f;
        c.b = 1f;

        switch (roomPlayer.hiderState)
        {
            case HiderState.Disguised:
            case HiderState.Invisible:
            {
                // 伪装/隐身：套用物品外观与碰撞箱
                Sprite sprite = ResolveSprite(roomPlayer.disguiseItemId);
                if (sprite != null)
                {
                    SetPrototypeLoop(false);
                    hiderSpriteRenderer.sprite = sprite;
                    ApplyVisualScaleToItemDisplay();
                }
                else
                {
                    // 无有效伪装贴图时回退原身循环
                    SetPrototypeLoop(true);
                    RestoreDefaultVisualScale();
                }

                ApplyColliderForItem(roomPlayer.disguiseItemId, sprite);

                if (roomPlayer.hiderState == HiderState.Disguised)
                {
                    c.a = 1f;
                    hiderSpriteRenderer.enabled = true;
                }
                else if (isLocal)
                {
                    c.a = invisibleLocalAlpha;
                    hiderSpriteRenderer.enabled = true;
                }
                else
                {
                    c.a = 0f;
                    hiderSpriteRenderer.enabled = false;
                }
                break;
            }
            case HiderState.Ghost:
                // 被调查命中：变回原型循环动画，全员可见，可被劈砍
                SetPrototypeLoop(true);
                RestoreDefaultVisualScale();
                RestoreDefaultCollider();
                c.a = 1f;
                hiderSpriteRenderer.enabled = true;
                break;
            case HiderState.Captured:
                SetPrototypeLoop(false);
                RestoreDefaultVisualScale();
                RestoreDefaultCollider();
                c.a = 0f;
                hiderSpriteRenderer.enabled = false;
                break;
            default:
                SetPrototypeLoop(true);
                c.a = 1f;
                hiderSpriteRenderer.enabled = true;
                break;
        }

        hiderSpriteRenderer.color = c;
    }

    /// <summary>
    /// 原身眼睛循环 + 毛团漂浮：开 Animator/Float；伪装成物品时必须关掉，否则会覆盖物品贴图。
    /// </summary>
    void SetPrototypeLoop(bool enabled)
    {
        if (hiderAnimator != null && hiderAnimator.enabled != enabled)
        {
            hiderAnimator.enabled = enabled;
            if (enabled)
                hiderAnimator.Play(0, 0, 0f);
        }

        if (prototypeFloat != null && prototypeFloat.enabled != enabled)
            prototypeFloat.enabled = enabled;
    }

    /// <summary>把 Visual_Hider 调到与放置物相同的世界尺度（GameConstants.ItemScale*）。</summary>
    void ApplyVisualScaleToItemDisplay()
    {
        if (hiderSpriteRenderer == null)
            return;

        Transform visual = hiderSpriteRenderer.transform;
        Vector3 parentLossy = visual.parent != null ? visual.parent.lossyScale : Vector3.one;
        visual.localScale = new Vector3(
            GameConstants.ItemScaleX / ApproxAbs(parentLossy.x),
            GameConstants.ItemScaleY / ApproxAbs(parentLossy.y),
            1f);
    }

    void RestoreDefaultVisualScale()
    {
        if (hiderSpriteRenderer != null)
            hiderSpriteRenderer.transform.localScale = defaultVisualLocalScale;
    }

    void ApplyColliderForItem(int itemId, Sprite sprite)
    {
        if (bodyCollider == null)
            return;

        Vector2 size = defaultColliderSize;
        Vector2 offset = defaultColliderOffset;

        // 与放置物一致：按贴图 bounds 重建，再乘宽/高缩放（不用 Prefab 里已圆角的 size，避免叠乘）
        if (sprite != null)
        {
            Bounds b = sprite.bounds;
            size = b.size;
            offset = b.center;
        }
        else if (TryGetItemCollider(itemId, out Vector2 prefabSize, out Vector2 prefabOffset))
        {
            size = prefabSize;
            offset = prefabOffset;
        }

        size.x *= colliderScale * GameConstants.ItemColliderScaleX;
        size.y *= colliderScale * GameConstants.ItemColliderScaleY;

        Vector3 lossy = transform.lossyScale;
        float absX = ApproxAbs(lossy.x);
        float absY = ApproxAbs(lossy.y);
        size.x = size.x * GameConstants.ItemScaleX / absX;
        size.y = size.y * GameConstants.ItemScaleY / absY;
        offset.x = offset.x * GameConstants.ItemScaleX / absX;
        offset.y = offset.y * GameConstants.ItemScaleY / absY;

        size.x = Mathf.Max(size.x, minColliderSize.x * GameConstants.ItemScaleX / absX);
        size.y = Mathf.Max(size.y, minColliderSize.y * GameConstants.ItemScaleY / absY);

        // 先写完整矩形再圆角，使伪装碰撞外形与放置物一致
        bodyCollider.edgeRadius = 0f;
        bodyCollider.size = size;
        bodyCollider.offset = offset;
        CollisionLayers.ApplyColliderRounding(bodyCollider);
        SyncGroundCheckToColliderBottom();
    }

    static float ApproxAbs(float v)
    {
        float a = Mathf.Abs(v);
        return a < 0.0001f ? 1f : a;
    }

    bool TryGetItemCollider(int itemId, out Vector2 size, out Vector2 offset)
    {
        size = defaultColliderSize;
        offset = defaultColliderOffset;

        if (itemTable == null || !itemTable.IsValid(itemId))
            return false;

        ItemTable.Entry entry = itemTable.Get(itemId);
        if (entry?.prefab == null)
            return false;

        BoxCollider2D col = entry.prefab.GetComponent<BoxCollider2D>();
        if (col == null)
            return false;

        size = col.size;
        offset = col.offset;
        return true;
    }

    void RestoreDefaultCollider()
    {
        if (bodyCollider == null)
            return;

        bodyCollider.edgeRadius = 0f;
        bodyCollider.size = defaultColliderSize;
        bodyCollider.offset = defaultColliderOffset;
        CollisionLayers.ApplyColliderRounding(bodyCollider);
        SyncGroundCheckToColliderBottom();
    }

    /// <summary>把 GroundCheck 放到碰撞箱底边（含 edgeRadius）略下方，避免变身高低不同时落地检测错位。</summary>
    void SyncGroundCheckToColliderBottom()
    {
        if (bodyCollider == null)
            return;

        CollisionLayers.SyncGroundCheckToColliderBottom(gameObject, bodyCollider);

        Transform groundCheck = transform.Find("GroundCheck");
        if (groundCheck == null)
            return;

        var hider = GetComponent<HiderController>();
        if (hider != null)
            hider.groundCheckPoint = groundCheck;

        var seeker = GetComponent<SeekerController>();
        if (seeker != null)
            seeker.groundCheckPoint = groundCheck;
    }

    Sprite ResolveSprite(int itemId)
    {
        if (itemTable == null || !itemTable.IsValid(itemId))
            return null;

        ItemTable.Entry entry = itemTable.Get(itemId);
        if (entry == null)
            return null;

        if (entry.icon != null)
            return entry.icon;

        if (entry.prefab != null)
        {
            var sr = entry.prefab.GetComponent<SpriteRenderer>();
            if (sr != null)
                return sr.sprite;
        }

        return null;
    }
}
