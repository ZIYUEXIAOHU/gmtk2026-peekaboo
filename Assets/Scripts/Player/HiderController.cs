using Mirror;
using UnityEngine;

public class HiderController : NetworkBehaviour
{
    [Header("移动设置")]
    public float moveSpeed = GameConstants.HiderMoveSpeed;  // 使用契约常量 0.3
    public float jumpForce = 12f;
    
    [Header("检测")]
    public float groundCheckRadius = 0.2f;
    public Transform groundCheckPoint;
    public LayerMask groundLayer;
    
    [Header("测试模式")]
    public bool testMode = false;           // 勾选后无需联网即可测试
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
            
            // ===== 防止穿透 =====
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            rb.useFullKinematicContacts = true;
        }
        
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
        
        // ===== 测试模式：直接启用 =====
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
        moveSpeed = GameConstants.HiderMoveSpeed;
        
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
        if (hasJumped && !isGrounded)
        {
            Debug.Log("⚠️ 已经跳过了，不能二段跳");
            return;
        }
        
        if (isGrounded)
        {
            hasJumped = false;
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
            transform.position, 
            Vector2.down, 
            0.6f, 
            groundLayer
        );
        
        if (hit.collider != null)
        {
            PlatformEffector2D effector = hit.collider.GetComponent<PlatformEffector2D>();
            if (effector != null && effector.useOneWay)
            {
                StartCoroutine(DisableCollision());
                Debug.Log("跳下单向平台");
            }
            else
            {
                Debug.Log("⚠️ 下面是实心地面，无法跳下");
            }
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
        if (groundCheckPoint == null) return;
        
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            groundCheckPoint.position, 
            groundCheckRadius, 
            groundLayer
        );
        
        bool wasGrounded = isGrounded;
        isGrounded = hits.Length > 0;
        
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