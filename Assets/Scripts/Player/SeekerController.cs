using Mirror;
using UnityEngine;

public class SeekerController : NetworkBehaviour
{
    [Header("移动设置")]
    public float moveSpeed = GameConstants.SeekerMoveSpeed;  // 使用契约常量 0.5
    public float speedMultiplier = 10f;  // 速度倍率，可在 Inspector 调整
    
    [Header("交互范围（使用契约常量）")]
    public float investigateRange = GameConstants.InvestigateRange;  // 1.5
    public float slashRange = GameConstants.SlashRange;  // 1.0
    
    [Header("检测")]
    public float groundCheckRadius = 0.2f;
    public Transform groundCheckPoint;
    public LayerMask groundLayer;
    
    [Header("测试模式")]
    public bool testMode = false;           // 勾选后无需联网即可测试
    public KeyCode testInvestigateKey = KeyCode.F;
    public KeyCode testSlashKey = KeyCode.Space;
    
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private float moveInput;
    private bool isGrounded;
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
                spriteRenderer.color = new Color(0.9f, 0.3f, 0.2f);
            Debug.Log("🧪 SeekerController 测试模式已启用，无需联网");
            return;
        }
        
        if (!isLocalPlayer)
        {
            Debug.Log("❌ SeekerController: 不是本地玩家，禁用控制");
            enabled = false;
            isLocalPlayerReady = false;
            return;
        }
        
        isLocalPlayerReady = true;
        moveSpeed = GameConstants.SeekerMoveSpeed * speedMultiplier;
        Debug.Log("✅ SeekerController: 本地玩家已就绪");
        
        if (spriteRenderer != null)
            spriteRenderer.color = new Color(0.9f, 0.3f, 0.2f);
    }
    
    void Update()
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;
        
        // ===== 移动 (A/D) =====
        moveInput = Input.GetAxisRaw("Horizontal");
        
        // ===== 调查 (F) =====
        if (Input.GetKeyDown(testInvestigateKey))
        {
            Investigate();
        }
        
        // ===== 劈砍 (空格) =====
        if (Input.GetKeyDown(testSlashKey))
        {
            Slash();
        }
        
        // ===== 更新朝向 =====
        UpdateFacing();
    }
    
    void FixedUpdate()
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;
        
        // ===== 水平移动 =====
        Vector2 velocity = rb.velocity;
        velocity.x = moveInput * moveSpeed;
        rb.velocity = velocity;
        
        // ===== 检测地面 =====
        CheckGrounded();
    }
    
    void CheckGrounded()
    {
        if (groundCheckPoint == null) return;
        
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            groundCheckPoint.position, 
            groundCheckRadius, 
            groundLayer
        );
        isGrounded = hits.Length > 0;
    }
    
    void UpdateFacing()
    {
        if (moveInput > 0)
            transform.localScale = new Vector3(1, 1, 1);
        else if (moveInput < 0)
            transform.localScale = new Vector3(-1, 1, 1);
    }
    
    void Investigate()
    {
        if (testMode)
        {
            Debug.Log("🧪 [测试模式] 调查");
            return;
        }
        
        if (!isLocalPlayer) return;
        
        Debug.Log("🔍 F 调查");
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.Investigate();
        }
        else
        {
            Debug.Log("⚠️ 契约未绑定，模拟调查");
        }
    }
    
    void Slash()
    {
        if (testMode)
        {
            Debug.Log("🧪 [测试模式] 劈砍");
            return;
        }
        
        if (!isLocalPlayer) return;
        
        Debug.Log("⚔️ 空格 劈砍");
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.Slash();
        }
        else
        {
            Debug.Log("⚠️ 契约未绑定，模拟劈砍");
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