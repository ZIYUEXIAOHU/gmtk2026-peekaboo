using Mirror;
using UnityEngine;

public class SeekerController : NetworkBehaviour
{
    static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    static readonly int FacingDirectionHash = Animator.StringToHash("FacingDirection");
    static readonly int AttackHash = Animator.StringToHash("Attack");

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

    [Header("动画")]
    public Animator animator;
    public NetworkAnimator networkAnimator;
    public SpriteRenderer visualSpriteRenderer;

    [Header("攻击硬直")]
    [Tooltip("攻击期间禁止移动的时长，约等于 Attack 动画长度")]
    public float attackMoveLockDuration = 0.6f;
    
    [Header("测试模式")]
    public bool testMode = false;           // 勾选后无需联网即可测试
    public KeyCode testInvestigateKey = KeyCode.F;
    public KeyCode testSlashKey = KeyCode.Space;
    
    private Rigidbody2D rb;
    private float moveInput;
    private bool isGrounded;
    private bool isLocalPlayerReady = false;
    private float facingDirection = 0f;
    private float attackMoveLockUntil;
    
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
        CacheAnimationRefs();
        
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
    }

    void CacheAnimationRefs()
    {
        if (animator == null)
        {
            Transform visual = transform.Find("Visual_Seeker");
            if (visual != null)
                animator = visual.GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);
        }

        if (networkAnimator == null)
        {
            Transform visual = transform.Find("Visual_Seeker");
            if (visual != null)
                networkAnimator = visual.GetComponent<NetworkAnimator>();
            if (networkAnimator == null)
                networkAnimator = GetComponentInChildren<NetworkAnimator>(true);
        }

        if (visualSpriteRenderer == null)
        {
            Transform visual = transform.Find("Visual_Seeker");
            if (visual != null)
                visualSpriteRenderer = visual.GetComponent<SpriteRenderer>();
        }
    }
    
    void Update()
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;
        
        // ===== 移动 (A/D)；攻击硬直期间不可移动 =====
        if (IsAttackMoveLocked)
            moveInput = 0f;
        else
            moveInput = Input.GetAxisRaw("Horizontal");
        
        // ===== 调查 (F) =====
        if (Input.GetKeyDown(testInvestigateKey))
        {
            Investigate();
        }
        
        // ===== 劈砍 (空格) =====
        if (Input.GetKeyDown(testSlashKey) && !IsAttackMoveLocked)
        {
            Slash();
        }
        
        // ===== 更新朝向与动画 =====
        UpdateFacing();
        UpdateAnimator();
    }
    
    void FixedUpdate()
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;
        
        // ===== 水平移动 =====
        Vector2 velocity = rb.velocity;
        velocity.x = IsAttackMoveLocked ? 0f : moveInput * moveSpeed;
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
        if (moveInput > 0f)
            facingDirection = 1f;
        else if (moveInput < 0f)
            facingDirection = -1f;

        // 用 flipX 翻转外观，避免改根节点 localScale（NetworkTransform 不同步 scale）
        if (visualSpriteRenderer != null)
            visualSpriteRenderer.flipX = facingDirection < 0f;
    }

    void UpdateAnimator()
    {
        CacheAnimationRefs();
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        bool moving = Mathf.Abs(moveInput) > 0.01f;
        animator.SetBool(IsMovingHash, moving);
        // ±1 = 侧面朝向（由 NetworkAnimator 同步）；0 = 正面 Idle
        animator.SetFloat(FacingDirectionHash, facingDirection);
    }

    public bool IsAttackMoveLocked => Time.time < attackMoveLockUntil;

    /// <summary>播放攻击动画并进入移动硬直。空格劈砍 / 鼠标攻击共用。</summary>
    public void BeginAttack()
    {
        TriggerAttackAnimation();
        attackMoveLockUntil = Time.time + attackMoveLockDuration;
        moveInput = 0f;
        if (rb != null)
        {
            Vector2 velocity = rb.velocity;
            velocity.x = 0f;
            rb.velocity = velocity;
        }
    }

    void TriggerAttackAnimation()
    {
        CacheAnimationRefs();
        if (networkAnimator != null && !testMode)
            networkAnimator.SetTrigger(AttackHash);
        else if (animator != null && animator.isActiveAndEnabled)
            animator.SetTrigger(AttackHash);
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
        BeginAttack();

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
