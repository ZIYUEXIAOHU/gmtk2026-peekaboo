using Mirror;
using UnityEngine;

public class HiderController : NetworkBehaviour
{
    [Header("移动设置")]
    public float moveSpeed = GameConstants.HiderMoveSpeed;  // 使用契约常量 0.3
    public float speedMultiplier = 13.33f;  // 速度倍率（4 / 0.3 ≈ 13.33），可在 Inspector 调整
    public float jumpForce = 18f;  // 跳跃力
    
    [Header("检测")]
    public float groundCheckRadius = 0.3f;
    public Transform groundCheckPoint;
    public LayerMask groundLayer;
    
    [Header("测试模式")]
    public bool testMode = false;
    public KeyCode testJumpKey = KeyCode.W;
    public KeyCode testPlaceKey = KeyCode.F;
    
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private float moveInput;
    private bool isGrounded;
    private bool hasJumped = false;
    private bool isLocalPlayerReady = false;
    
    void Start()
    {
        SetupController();
    }

    void OnEnable()
    {
        SetupController();
    }

    void SetupController()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        
        if (rb != null)
        {
            rb.gravityScale = 3f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            rb.useFullKinematicContacts = true;
        }
        
        // ===== 确保 GroundCheck 存在（位置由碰撞箱/变身脚本决定，勿写死）=====
        if (groundCheckPoint == null)
        {
            Transform existing = transform.Find("GroundCheck");
            if (existing != null)
                groundCheckPoint = existing;
            else
            {
                GameObject go = new GameObject("GroundCheck");
                go.transform.SetParent(transform);
                groundCheckPoint = go.transform;
            }
        }

        // 初始贴合当前 BoxCollider2D 底边；之后变身由 HiderDisguiseVisual 同步
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null)
        {
            float bottom = col.offset.y - col.size.y * 0.5f;
            groundCheckPoint.localPosition = new Vector3(col.offset.x, bottom - 0.02f, 0f);
        }
        else if (groundCheckPoint.localPosition == Vector3.zero)
        {
            groundCheckPoint.localPosition = new Vector3(0f, -0.5f, 0f);
        }

        Debug.Log($"✅ GroundCheck 位置: {groundCheckPoint.localPosition}");
        
        // ===== 确保 groundLayer 包含 Ground 层 =====
        groundLayer |= LayerMask.GetMask("Ground");
        Debug.Log($"✅ groundLayer 已包含 Ground 层: {groundLayer.value}");
        
        if (testMode)
        {
            isLocalPlayerReady = true;
            enabled = true;
            if (spriteRenderer != null)
                spriteRenderer.color = new Color(0.2f, 0.8f, 0.2f);
            Debug.Log("🧪 测试模式已启用，无需联网");
            return;
        }
        
        if (!isLocalPlayer)
        {
            enabled = false;
            isLocalPlayerReady = false;
            return;
        }
        
        isLocalPlayerReady = true;
        moveSpeed = GameConstants.HiderMoveSpeed * speedMultiplier;
        
        Debug.Log($"✅ 躲藏者速度: 契约={GameConstants.HiderMoveSpeed}, 倍率={speedMultiplier}, 实际={moveSpeed}");
        
        if (spriteRenderer != null)
            spriteRenderer.color = new Color(0.2f, 0.8f, 0.2f);
    }
    
    void Update()
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;
        
        moveInput = Input.GetAxisRaw("Horizontal");
        
        if (Input.GetKeyDown(testJumpKey) || Input.GetKeyDown(KeyCode.Space))
        {
            Jump();
        }
        
        if (Input.GetKeyDown(KeyCode.S))
        {
            DropDown();
        }
        
        if (Input.GetKeyDown(testPlaceKey))
        {
            PlaceItem();
        }
        
        UpdateFacing();
    }
    
    void FixedUpdate()
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;
        
        Vector2 velocity = rb.velocity;
        velocity.x = moveInput * moveSpeed;
        rb.velocity = velocity;
        
        CheckGrounded();
    }
    
    void Jump()
    {
        Debug.Log($"🔍 Jump 调用: isGrounded={isGrounded}, hasJumped={hasJumped}");
        
        if (isGrounded)
        {
            hasJumped = false;
        }
        
        if (hasJumped && !isGrounded)
        {
            Debug.Log("⚠️ 已经跳过了，不能二段跳");
            return;
        }
        
        rb.velocity = new Vector2(rb.velocity.x, jumpForce);
        hasJumped = true;
        isGrounded = false;
        
        Debug.Log($"✅ 跳跃！hasJumped={hasJumped}");
    }
    
    void DropDown()
    {
        if (!isGrounded)
        {
            Debug.Log("⚠️ 未在地面，无法跳下");
            return;
        }
        
        RaycastHit2D hit = Physics2D.Raycast(
            groundCheckPoint.position,
            Vector2.down,
            0.2f,
            groundLayer
        );
        
        if (hit.collider != null)
        {
            PlatformEffector2D effector = hit.collider.GetComponent<PlatformEffector2D>();
            if (effector != null && effector.useOneWay)
            {
                StartCoroutine(DisableCollision());
                Debug.Log($"⬇️ 跳下平台: {hit.collider.gameObject.name}");
            }
            else
            {
                Debug.Log("⚠️ 下面是实心地面，无法跳下");
            }
        }
        else
        {
            Debug.Log("⚠️ 脚下没有检测到平台");
        }
    }
    
    System.Collections.IEnumerator DisableCollision()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.enabled = false;
            yield return new WaitForSeconds(0.3f);
            col.enabled = true;
        }
    }
    
    void CheckGrounded()
    {
        if (groundCheckPoint == null)
        {
            Transform existing = transform.Find("GroundCheck");
            if (existing != null)
                groundCheckPoint = existing;
            else
            {
                GameObject go = new GameObject("GroundCheck");
                go.transform.SetParent(transform);
                go.transform.localPosition = new Vector3(0, -0.5f, 0);
                groundCheckPoint = go.transform;
            }
        }
        
        groundCheckPoint.localPosition = new Vector3(0, -0.5f, 0);
        
        // ===== 用 Layer 检测 =====
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            groundCheckPoint.position, 
            groundCheckRadius, 
            groundLayer
        );
        
        bool wasGrounded = isGrounded;
        isGrounded = hits.Length > 0;
        
        // ===== 调试日志 =====
        if (hits.Length > 0)
        {
            Debug.Log($"🔍 检测到 {hits.Length} 个地面物体 (Layer)");
        }
        
        if (!wasGrounded && isGrounded)
        {
            hasJumped = false;
            Debug.Log("✅ 落地，重置跳跃状态");
        }
    }
    
    void UpdateFacing()
    {
        if (moveInput > 0)
            transform.localScale = new Vector3(1, 1, 1);
        else if (moveInput < 0)
            transform.localScale = new Vector3(-1, 1, 1);
    }
    
    void PlaceItem()
    {
        if (testMode)
        {
            Debug.Log("🧪 [测试模式] 放置物品");
            return;
        }
        
        if (!isLocalPlayer) return;
        
        Debug.Log("F 放置物品");
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.PlaceItem();
        }
        else
        {
            Debug.Log("⚠️ 契约未绑定，模拟放置");
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }
    }
}