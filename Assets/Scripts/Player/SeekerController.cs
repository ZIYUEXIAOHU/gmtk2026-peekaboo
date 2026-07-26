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
    public float investigateRange = GameConstants.InvestigateRange;  // 5.0
    public float slashRange = GameConstants.SlashRange;  // 2.0
    
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

    [Header("攻击冷却（空格与鼠标共用）")]
    public float attackCooldown = 0.5f;
    float lastAttackTime = -999f;
    
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
    /// <summary>变身波锁定结束时刻（NetworkTime.time）。</summary>
    private double transformLockUntil;
    private bool gameEventsSubscribed;
    private IGameEvents boundEvents;
    
    void Start()
    {
        SetupController();
        TrySubscribeGameEvents();
    }

    void OnEnable()
    {
        SetupController();
        TrySubscribeGameEvents();
    }

    void OnDisable()
    {
        UnsubscribeGameEvents();
        if (isLocalPlayerReady)
            SeekerBlackoutOverlay.Ensure().Hide();
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

        TrySubscribeGameEvents();
        UpdateTransformLockVisual();
        
        // ===== 移动 (A/D)；攻击硬直 / 变身波锁定期间不可移动 =====
        if (IsMoveLocked)
            moveInput = 0f;
        else
            moveInput = Input.GetAxisRaw("Horizontal");
        
        // ===== 调查 (F) =====
        if (Input.GetKeyDown(testInvestigateKey) && !IsTransformLocked)
        {
            Investigate();
        }
        
        // ===== 劈砍 (空格)：身位 + 鼠标特效双点，与左键共享冷却 =====
        if (Input.GetKeyDown(testSlashKey) && !IsMoveLocked)
        {
            TryPerformAttack();
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
        velocity.x = IsMoveLocked ? 0f : moveInput * moveSpeed;
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

    /// <summary>变身波锁定：用 NetworkTime.time 与 invulnerableUntil 比较，抵抗延迟。</summary>
    public bool IsTransformLocked => NetworkTime.time < transformLockUntil;

    public bool IsMoveLocked => IsAttackMoveLocked || IsTransformLocked;

    void TrySubscribeGameEvents()
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;
        if (!GameContract.IsBound || GameContract.Events == null) return;
        if (gameEventsSubscribed && ReferenceEquals(boundEvents, GameContract.Events)) return;

        UnsubscribeGameEvents();
        boundEvents = GameContract.Events;
        boundEvents.OnHiderTransformed += OnHiderTransformed;
        boundEvents.OnCommandRejected += OnCommandRejected;
        gameEventsSubscribed = true;
    }

    void UnsubscribeGameEvents()
    {
        if (!gameEventsSubscribed) return;
        if (boundEvents != null)
        {
            boundEvents.OnHiderTransformed -= OnHiderTransformed;
            boundEvents.OnCommandRejected -= OnCommandRejected;
        }
        else if (GameContract.IsBound && GameContract.Events != null)
        {
            GameContract.Events.OnHiderTransformed -= OnHiderTransformed;
            GameContract.Events.OnCommandRejected -= OnCommandRejected;
        }
        boundEvents = null;
        gameEventsSubscribed = false;
    }

    void OnCommandRejected(CommandRejected rejected)
    {
        if (rejected.command != GameCommandType.Investigate) return;
        Debug.LogWarning($"⚠️ 调查被拒绝：{rejected.reason}");
    }

    void OnHiderTransformed(TransformInfo info)
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;

        // 同一波每个躲藏者各发一条事件，取 max 幂等
        if (info.invulnerableUntil > transformLockUntil)
            transformLockUntil = info.invulnerableUntil;

        UpdateTransformLockVisual();
    }

    void UpdateTransformLockVisual()
    {
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;

        var overlay = SeekerBlackoutOverlay.Ensure();
        if (IsTransformLocked)
        {
            overlay.Show();
            moveInput = 0f;
            if (rb != null)
            {
                Vector2 velocity = rb.velocity;
                velocity.x = 0f;
                rb.velocity = velocity;
            }
        }
        else
        {
            overlay.Hide();
        }
    }

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
    
    /// <summary>
    /// 空格 / 鼠标共用：共享冷却 → BeginAttack → 鼠标处特效 → 双点 Slash。
    /// </summary>
    public bool TryPerformAttack()
    {
        if (!isLocalPlayerReady) return false;
        if (!testMode && !isLocalPlayer) return false;
        if (IsMoveLocked) return false;
        if (Time.time - lastAttackTime < attackCooldown) return false;

        lastAttackTime = Time.time;
        BeginAttack();

        Vector2 mouseWorld = GetMouseWorldPosition();
        SlashVfxPresenter.Ensure();
        SeekerAttackEffect.Spawn(mouseWorld);

        if (testMode)
        {
            Debug.Log($"🧪 [测试模式] 劈砍 effect={mouseWorld}");
            return true;
        }

        Debug.Log($"⚔️ 劈砍 身位+鼠标 effect={mouseWorld}");

        if (GameContract.IsBound)
            GameContract.Commands.Slash(mouseWorld);
        else
            Debug.Log("⚠️ 契约未绑定，模拟劈砍");

        return true;
    }

    static Vector2 GetMouseWorldPosition()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return Vector2.zero;
        Vector3 p = cam.ScreenToWorldPoint(Input.mousePosition);
        return new Vector2(p.x, p.y);
    }

    void Investigate()
    {
        if (testMode)
        {
            Debug.Log("🧪 [测试模式] 调查");
            return;
        }
        
        if (!isLocalPlayer) return;

        Vector2 mouseWorld = GetMouseWorldPosition();
        Debug.Log($"🔍 F 调查 mouse={mouseWorld}");
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.Investigate(mouseWorld);
        }
        else
        {
            Debug.Log("⚠️ 契约未绑定，模拟调查");
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, slashRange);
    }
}
