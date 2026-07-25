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
    
    private Collider2D triggerCollider;
    private bool playerInside = false;
    private bool playerInUpper = false;
    private Vector3 upperTriggerLocalPos;
    private float lastTeleportTime = 0f;  // 上次传送时间
    
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
                if (hit.attachedRigidbody != null && hit.attachedRigidbody.gameObject != gameObject)
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
            if (hit.attachedRigidbody != null && hit.attachedRigidbody.gameObject != gameObject)
            {
                player = hit.transform;
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
            if (hit.attachedRigidbody != null && hit.attachedRigidbody.gameObject != gameObject)
            {
                player = hit.transform;
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
    }
}