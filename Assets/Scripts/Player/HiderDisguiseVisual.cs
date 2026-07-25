using Mirror;
using UnityEngine;

/// <summary>
/// 客户端表现：根据 RoomPlayer.disguiseItemId / hiderState 切换 Visual_Hider 外观与透明度，
/// 并按伪装物品尺寸同步根节点 BoxCollider2D（与摆放物一致）。
/// 挂在 RoomPlayerPrefab 根上；不依赖 NetworkBehaviour。
/// </summary>
public class HiderDisguiseVisual : MonoBehaviour
{
    [Header("引用（可空，运行时自动查找）")]
    [SerializeField] RoomPlayer roomPlayer;
    [SerializeField] SpriteRenderer hiderSpriteRenderer;
    [SerializeField] BoxCollider2D bodyCollider;
    [SerializeField] ItemTable itemTable;

    [Header("透明度")]
    [SerializeField] [Range(0f, 1f)] float invisibleLocalAlpha = 0.35f;
    [SerializeField] [Range(0f, 1f)] float ghostAlpha = 0.5f;

    [Header("碰撞箱")]
    [Tooltip("碰撞箱相对物品尺寸的缩放（1=完全贴合）")]
    [SerializeField] float colliderScale = 1f;
    [SerializeField] Vector2 minColliderSize = new Vector2(0.25f, 0.25f);
    [SerializeField] Vector2 defaultColliderSize = new Vector2(1f, 1f);
    [SerializeField] Vector2 defaultColliderOffset = Vector2.zero;

    Sprite defaultSprite;
    int lastItemId = int.MinValue;
    HiderState lastState = (HiderState)(-1);
    PlayerRole lastRole = (PlayerRole)(-1);
    bool lastIsLocal;

    void Awake()
    {
        CacheRefs();
        if (hiderSpriteRenderer != null)
            defaultSprite = hiderSpriteRenderer.sprite;

        if (bodyCollider != null)
        {
            defaultColliderSize = bodyCollider.size;
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
            RestoreDefaultCollider();
            return;
        }

        // 换装
        Sprite sprite = ResolveSprite(roomPlayer.disguiseItemId);
        if (sprite != null)
            hiderSpriteRenderer.sprite = sprite;
        else if (defaultSprite != null)
            hiderSpriteRenderer.sprite = defaultSprite;

        ApplyColliderForItem(roomPlayer.disguiseItemId, sprite);

        // 透明度 / 显隐
        Color c = hiderSpriteRenderer.color;
        c.r = 1f;
        c.g = 1f;
        c.b = 1f;

        switch (roomPlayer.hiderState)
        {
            case HiderState.Disguised:
                c.a = 1f;
                hiderSpriteRenderer.enabled = true;
                break;
            case HiderState.Invisible:
                if (isLocal)
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
            case HiderState.Ghost:
                c.a = ghostAlpha;
                hiderSpriteRenderer.enabled = true;
                break;
            case HiderState.Captured:
                c.a = 0f;
                hiderSpriteRenderer.enabled = false;
                break;
            default:
                c.a = 1f;
                hiderSpriteRenderer.enabled = true;
                break;
        }

        hiderSpriteRenderer.color = c;
    }

    void ApplyColliderForItem(int itemId, Sprite sprite)
    {
        if (bodyCollider == null)
            return;

        Vector2 size = defaultColliderSize;
        Vector2 offset = defaultColliderOffset;

        // 优先用物品 Prefab 上的碰撞箱（与摆放物一致）
        if (TryGetItemCollider(itemId, out Vector2 prefabSize, out Vector2 prefabOffset))
        {
            size = prefabSize;
            offset = prefabOffset;
        }
        else if (sprite != null)
        {
            Bounds b = sprite.bounds;
            size = b.size;
            offset = b.center;
        }

        size *= colliderScale;
        size.x = Mathf.Max(size.x, minColliderSize.x);
        size.y = Mathf.Max(size.y, minColliderSize.y);

        bodyCollider.size = size;
        bodyCollider.offset = offset;
        SyncGroundCheckToColliderBottom(size, offset);
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

        bodyCollider.size = defaultColliderSize;
        bodyCollider.offset = defaultColliderOffset;
        SyncGroundCheckToColliderBottom(defaultColliderSize, defaultColliderOffset);
    }

    /// <summary>把 GroundCheck 放到碰撞箱底边略下方，避免变身高低不同时落地检测错位。</summary>
    void SyncGroundCheckToColliderBottom(Vector2 size, Vector2 offset)
    {
        Transform groundCheck = transform.Find("GroundCheck");
        if (groundCheck == null)
            return;

        float bottom = offset.y - size.y * 0.5f;
        groundCheck.localPosition = new Vector3(offset.x, bottom - 0.02f, 0f);

        // 同步控制器引用（若已缓存）
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
