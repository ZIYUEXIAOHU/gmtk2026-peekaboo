using Mirror;
using UnityEngine;

public class StairController : MonoBehaviour
{
    [Header("传送点")]
    public Transform upPoint;
    public Transform downPoint;
    
    [Header("设置")]
    public KeyCode interactKey = KeyCode.W;
    public float cooldown = 0.6f;  // 传送冷却时间
    
    [Header("上方触发器")]
    public Collider2D upperTrigger;

    [Header("放置物推送")]
    [Tooltip("楼梯井区内放置物被推向中心的水平速度；≤0 使用 GameConstants。")]
    public float itemCenterPushSpeed = -1f;
    [Tooltip("额外放大 lossyScale 作为推送检测区（宽/高）。")]
    public Vector2 itemForceZonePadding = new Vector2(0.4f, 0.2f);
    
    private Collider2D triggerCollider;
    private bool playerInside = false;
    private bool playerInUpper = false;
    private Vector3 upperTriggerLocalPos;
    private float lastTeleportTime = 0f;  // 上次传送时间
    private readonly Collider2D[] itemOverlapBuffer = new Collider2D[16];
    
    void Start()
    {
        triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider != null)
            triggerCollider.isTrigger = true;
        
        if (GetComponent<Rigidbody2D>() == null)
        {
            Rigidbody2D rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
        }
        
        if (upperTrigger != null)
        {
            if (upperTrigger.GetComponent<Rigidbody2D>() == null)
            {
                Rigidbody2D rb = upperTrigger.gameObject.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.useFullKinematicContacts = true;
            }
            
            upperTriggerLocalPos = upperTrigger.transform.localPosition;
        }
    }

    void FixedUpdate()
    {
        // 仅权威端推动放置物，客户端由 InvestigableObject 同步位姿
        if (!NetworkServer.active)
            return;

        PushItemsTowardCenter();
    }
    
    void Update()
    {
        // ===== 冷却检查 =====
        if (Time.time - lastTeleportTime < cooldown) return;
        
        // ===== 备用检测：每帧检测上方触发器 =====
        if (upperTrigger != null)
        {
            Collider2D[] hits = Physics2D.OverlapBoxAll(
                upperTrigger.bounds.center,
                upperTrigger.bounds.size,
                0f
            );
            
            bool found = false;
            foreach (var hit in hits)
            {
                if (IsPlayerBody(hit) && hit.attachedRigidbody.gameObject != gameObject)
                {
                    found = true;
                    break;
                }
            }
            
            if (found != playerInUpper)
            {
                playerInUpper = found;
                Debug.Log($"🔄 上方触发器状态: {found}");
            }
        }
        
        // ===== 下传上 =====
        if (playerInside && Input.GetKeyDown(interactKey))
        {
            TeleportUp();
        }
        
        // ===== 上传下 =====
        if (playerInUpper && Input.GetKeyDown(interactKey))
        {
            TeleportDown();
        }
    }
    
    static bool IsPlayerBody(Collider2D col)
    {
        if (col == null || col.attachedRigidbody == null)
            return false;
        // 放置物也有 Rigidbody2D，传送只认玩家
        if (col.GetComponentInParent<InvestigableObject>() != null)
            return false;
        return col.GetComponentInParent<RoomPlayer>() != null;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.attachedRigidbody == null) return;
        if (other.attachedRigidbody.gameObject == gameObject) return;
        
        Debug.Log($"🔍 进入: {other.gameObject.name}");
        
        if (upperTrigger != null && other.gameObject == upperTrigger.gameObject)
        {
            playerInUpper = true;
            Debug.Log($"✅ 玩家进入上方触发器");
            return;
        }

        if (!IsPlayerBody(other))
            return;
        
        playerInside = true;
        Debug.Log($"✅ 玩家进入楼梯触发范围");
    }
    
    void OnTriggerExit2D(Collider2D other)
    {
        if (other.attachedRigidbody == null) return;
        if (other.attachedRigidbody.gameObject == gameObject) return;
        
        Debug.Log($"🔍 离开: {other.gameObject.name}");
        
        if (upperTrigger != null && other.gameObject == upperTrigger.gameObject)
        {
            playerInUpper = false;
            Debug.Log($"❌ 玩家离开上方触发器");
            return;
        }

        if (!IsPlayerBody(other))
            return;
        
        playerInside = false;
        Debug.Log($"❌ 玩家离开楼梯触发范围");
    }
    
    void TeleportUp()
    {
        if (triggerCollider == null || upPoint == null) return;
        if (Time.time - lastTeleportTime < cooldown) return;
        
        Collider2D[] hits = Physics2D.OverlapBoxAll(
            triggerCollider.bounds.center,
            triggerCollider.bounds.size,
            0f
        );
        
        Transform player = null;
        foreach (var hit in hits)
        {
            if (IsPlayerBody(hit) && hit.attachedRigidbody.gameObject != gameObject)
            {
                player = hit.attachedRigidbody.transform;
                break;
            }
        }
        
        if (player == null) return;
        
        player.position = upPoint.position;
        
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.velocity = Vector2.zero;
        
        playerInside = false;
        lastTeleportTime = Time.time;
        Debug.Log($"⬆️ 下传上完成");
    }
    
    void TeleportDown()
    {
        if (downPoint == null || upperTrigger == null) return;
        if (Time.time - lastTeleportTime < cooldown) return;
        
        Collider2D[] hits = Physics2D.OverlapBoxAll(
            upperTrigger.bounds.center,
            upperTrigger.bounds.size,
            0f
        );
        
        Transform player = null;
        foreach (var hit in hits)
        {
            if (IsPlayerBody(hit) && hit.attachedRigidbody.gameObject != gameObject)
            {
                player = hit.attachedRigidbody.transform;
                break;
            }
        }
        
        if (player == null) return;
        
        player.position = downPoint.position;
        
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.velocity = Vector2.zero;
        
        if (upperTrigger != null)
        {
            upperTrigger.transform.localPosition = upperTriggerLocalPos;
        }
        
        playerInUpper = false;
        lastTeleportTime = Time.time;
        Debug.Log($"⬇️ 上传下完成");
    }
    
    /// <summary>
    /// 楼梯井范围内的放置物获得指向楼梯中心（X）的水平速度，便于落入通道。
    /// </summary>
    void PushItemsTowardCenter()
    {
        float pushSpeed = itemCenterPushSpeed > 0f
            ? itemCenterPushSpeed
            : GameConstants.StairItemCenterPushSpeed;
        if (pushSpeed <= 0f)
            return;

        Vector2 zoneSize = new Vector2(
            Mathf.Abs(transform.lossyScale.x) + itemForceZonePadding.x,
            Mathf.Abs(transform.lossyScale.y) + itemForceZonePadding.y);
        if (zoneSize.x < 0.1f || zoneSize.y < 0.1f)
            return;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(LayerMask.GetMask(CollisionLayers.HiderItem));
        filter.useLayerMask = true;

        int count = Physics2D.OverlapBox(
            transform.position,
            zoneSize,
            0f,
            filter,
            itemOverlapBuffer);

        float centerX = transform.position.x;
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = itemOverlapBuffer[i];
            if (hit == null)
                continue;

            Rigidbody2D itemRb = hit.attachedRigidbody;
            if (itemRb == null || itemRb.bodyType != RigidbodyType2D.Dynamic)
                continue;
            if (hit.GetComponent<InvestigableObject>() == null)
                continue;

            float dx = centerX - itemRb.position.x;
            if (Mathf.Abs(dx) < 0.05f)
            {
                // 已接近中线：清掉水平速度，避免左右抖
                Vector2 v = itemRb.velocity;
                v.x = 0f;
                itemRb.velocity = v;
                continue;
            }

            itemRb.velocity = new Vector2(Mathf.Sign(dx) * pushSpeed, itemRb.velocity.y);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (upPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(upPoint.position, 0.3f);
        }
        if (downPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(downPoint.position, 0.3f);
        }
        if (upperTrigger != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(upperTrigger.bounds.center, upperTrigger.bounds.size);
        }

        Gizmos.color = new Color(1f, 0.6f, 0f, 0.35f);
        Vector2 zoneSize = new Vector2(
            Mathf.Abs(transform.lossyScale.x) + itemForceZonePadding.x,
            Mathf.Abs(transform.lossyScale.y) + itemForceZonePadding.y);
        Gizmos.DrawWireCube(transform.position, zoneSize);
    }
}