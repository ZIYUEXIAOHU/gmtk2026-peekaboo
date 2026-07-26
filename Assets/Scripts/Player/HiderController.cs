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
    [Tooltip("刚离开地面后仍可起跳的宽限时间（秒）")]
    public float coyoteTime = 0.1f;
    [Tooltip("最大跳跃次数（含地面跳 + 空中二段）")]
    public int maxJumpCount = 2;
    [Tooltip("起跳后多久才允许落地刷新次数，避免仍贴地时刷回满次数")]
    public float jumpGroundLockout = 0.08f;
    
    [Header("测试模式")]
    public bool testMode = false;
    public KeyCode testJumpKey = KeyCode.W;
    public KeyCode testPlaceKey = KeyCode.F;
    
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer visualSpriteRenderer;
    private float moveInput;
    private bool isGrounded;
    private int jumpsRemaining;
    private bool isLocalPlayerReady = false;
    private float coyoteUntil;
    private float lastJumpTime = -999f;
    private bool coyoteConsumedGroundJump;
    private bool eventsSubscribed;
    private IGameEvents boundEvents;
    
    void Start()
    {
        SetupController();
        TrySubscribeEvents();
    }

    void OnEnable()
    {
        SetupController();
        TrySubscribeEvents();
    }

    void OnDisable()
    {
        UnsubscribeEvents();
    }

    void OnDestroy()
    {
        UnsubscribeEvents();
    }

    void SetupController()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        CacheVisualSprite();
        
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

        // 初始贴合当前 BoxCollider2D 底边（含圆角）；之后变身由 HiderDisguiseVisual 同步
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null)
        {
            float bottom = col.offset.y - col.size.y * 0.5f - col.edgeRadius;
            groundCheckPoint.localPosition = new Vector3(col.offset.x, bottom - 0.02f, 0f);
        }
        else if (groundCheckPoint.localPosition == Vector3.zero)
        {
            groundCheckPoint.localPosition = new Vector3(0f, -0.5f, 0f);
        }

        Debug.Log($"✅ GroundCheck 位置: {groundCheckPoint.localPosition}");
        
        // 地面 / 放置物 / 其它躲藏者均可作为起跳面与刷新次数面
        groundLayer |= LayerMask.GetMask(
            CollisionLayers.Ground,
            CollisionLayers.HiderItem,
            CollisionLayers.Hider);
        jumpsRemaining = maxJumpCount;
        Debug.Log($"✅ groundLayer 掩码: {groundLayer.value}");
        
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

    void CacheVisualSprite()
    {
        if (visualSpriteRenderer != null)
            return;

        RoomPlayer rp = GetComponent<RoomPlayer>();
        if (rp != null && rp.visualHider != null)
            visualSpriteRenderer = rp.visualHider.GetComponent<SpriteRenderer>();
        if (visualSpriteRenderer == null)
        {
            Transform t = transform.Find("Visual_Hider");
            if (t != null)
                visualSpriteRenderer = t.GetComponent<SpriteRenderer>();
        }
    }

    void TrySubscribeEvents()
    {
        if (eventsSubscribed) return;
        if (!isLocalPlayerReady) return;
        if (!testMode && !isLocalPlayer) return;
        if (!GameContract.IsBound || GameContract.Events == null) return;

        boundEvents = GameContract.Events;
        boundEvents.OnHiderTransformed += OnHiderTransformed;
        boundEvents.OnPlaceResult += OnPlaceResult;
        eventsSubscribed = true;
        Debug.Log("✅ HiderController 订阅事件成功");
    }

    void UnsubscribeEvents()
    {
        if (!eventsSubscribed) return;
        if (boundEvents != null)
        {
            boundEvents.OnHiderTransformed -= OnHiderTransformed;
            boundEvents.OnPlaceResult -= OnPlaceResult;
        }
        else if (GameContract.IsBound && GameContract.Events != null)
        {
            GameContract.Events.OnHiderTransformed -= OnHiderTransformed;
            GameContract.Events.OnPlaceResult -= OnPlaceResult;
        }
        boundEvents = null;
        eventsSubscribed = false;
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
        Debug.Log($"🔍 Jump 调用: grounded={isGrounded}, jumpsRemaining={jumpsRemaining}, coyote={Time.time < coyoteUntil}");

        if (jumpsRemaining <= 0)
        {
            Debug.Log("⚠️ 跳跃次数已用尽");
            return;
        }

        rb.velocity = new Vector2(rb.velocity.x, jumpForce);
        jumpsRemaining--;
        lastJumpTime = Time.time;
        isGrounded = false;
        coyoteUntil = 0f;
        coyoteConsumedGroundJump = false;

        Debug.Log($"✅ 跳跃！剩余次数={jumpsRemaining}");

        // ===== 播放跳跃音效（仅本地躲藏者） =====
        if (GameContract.IsAudioBound)
        {
            GameContract.Audio.PlayPlaceLocal();
        }
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
        
        if (hit.collider != null && !IsOwnCollider(hit.collider))
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

    Vector2 GetGroundCheckBoxSize()
    {
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        float width = col != null
            ? Mathf.Max(
                (col.size.x + 2f * col.edgeRadius) * Mathf.Abs(transform.lossyScale.x),
                groundCheckRadius * 2f)
            : groundCheckRadius * 2f;
        float height = groundCheckRadius * 2f;
        return new Vector2(width, height);
    }

    bool IsOwnCollider(Collider2D hit)
    {
        if (hit == null)
            return false;
        return hit.transform == transform || hit.transform.IsChildOf(transform);
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
                BoxCollider2D col = GetComponent<BoxCollider2D>();
                if (col != null)
                {
                    float bottom = col.offset.y - col.size.y * 0.5f - col.edgeRadius;
                    go.transform.localPosition = new Vector3(col.offset.x, bottom - 0.02f, 0f);
                }
                else
                {
                    go.transform.localPosition = new Vector3(0, -0.5f, 0);
                }
                groundCheckPoint = go.transform;
            }
        }

        // 不要每帧写死 GroundCheck 位置——变身后由 HiderDisguiseVisual 同步底边

        Vector2 boxSize = GetGroundCheckBoxSize();
        Collider2D[] hits = Physics2D.OverlapBoxAll(
            groundCheckPoint.position,
            boxSize,
            0f,
            groundLayer
        );

        bool foundGround = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsOwnCollider(hits[i]))
                continue;
            foundGround = true;
            break;
        }
        
        bool wasGrounded = isGrounded;
        isGrounded = foundGround;

        float vy = rb != null ? rb.velocity.y : 0f;
        bool lockoutPassed = Time.time >= lastJumpTime + jumpGroundLockout;
        bool trulyLanded = isGrounded && vy <= 0.1f && lockoutPassed;

        if (trulyLanded)
        {
            if (jumpsRemaining < maxJumpCount)
            {
                jumpsRemaining = maxJumpCount;
                Debug.Log("✅ 落地（地面/物品/队友），刷新跳跃次数");
            }
            coyoteUntil = Time.time + coyoteTime;
            coyoteConsumedGroundJump = false;
        }
        else if (wasGrounded && !isGrounded && jumpsRemaining >= maxJumpCount)
        {
            // 走下支撑面且尚未起跳：开启 coyote，仍保留满次数
            coyoteUntil = Time.time + coyoteTime;
            coyoteConsumedGroundJump = false;
        }
        else if (!isGrounded
                 && jumpsRemaining >= maxJumpCount
                 && !coyoteConsumedGroundJump
                 && Time.time >= coyoteUntil
                 && coyoteUntil > 0f)
        {
            // coyote 过期仍未起跳：收束为只剩空中一段
            jumpsRemaining = maxJumpCount - 1;
            coyoteConsumedGroundJump = true;
            coyoteUntil = 0f;
        }
    }
    
    void UpdateFacing()
    {
        // 用 flipX 翻转外观，避免改根节点 localScale（NetworkTransform 不同步 scale，
        // 且根节点 3.5×4 与物品 Prefab 尺度不一致时会把碰撞箱/外观拉歪）
        CacheVisualSprite();
        if (visualSpriteRenderer == null)
            return;

        if (moveInput > 0f)
            visualSpriteRenderer.flipX = false;
        else if (moveInput < 0f)
            visualSpriteRenderer.flipX = true;
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
        
        // ===== 播放放置声（仅本地躲藏者） =====
        if (GameContract.IsAudioBound)
        {
            GameContract.Audio.PlayPlaceLocal();
        }
        
        if (GameContract.IsBound)
        {
            GameContract.Commands.PlaceItem();
        }
        else
        {
            Debug.Log("⚠️ 契约未绑定，模拟放置");
        }
    }

    // ===== 契约事件回调 =====

    void OnHiderTransformed(TransformInfo info)
    {
        // 只处理本地玩家
        if (!isLocalPlayer) return;
        if (!testMode && !isLocalPlayer) return;

        // ===== 布谷鸟钟声（全玩家） =====
        if (GameContract.IsAudioBound)
        {
            GameContract.Audio.PlayCuckoo(transform.position);
        }

        // ===== 沉闷"变"声（仅本地躲藏者） =====
        if (GameContract.IsAudioBound)
        {
            GameContract.Audio.PlayTransformLocal();
        }

        Debug.Log($"🔄 躲藏者变换: NetId={info.hiderNetId}, ItemId={info.newItemId}");
    }

    void OnPlaceResult(PlaceItemResult result)
    {
        if (!isLocalPlayer) return;

        if (result.success)
        {
            Debug.Log($"✅ 放置成功: ItemId={result.itemId}, Position={result.position}");
        }
        else
        {
            Debug.Log($"⚠️ 放置失败: {result.failReason}");
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (groundCheckPoint == null)
            return;

        Gizmos.color = Color.green;
        Vector2 size = Application.isPlaying
            ? GetGroundCheckBoxSize()
            : new Vector2(
                GetComponent<BoxCollider2D>() != null
                    ? Mathf.Max(
                        (GetComponent<BoxCollider2D>().size.x + 2f * GetComponent<BoxCollider2D>().edgeRadius)
                            * Mathf.Abs(transform.lossyScale.x),
                        groundCheckRadius * 2f)
                    : groundCheckRadius * 2f,
                groundCheckRadius * 2f);
        Gizmos.DrawWireCube(groundCheckPoint.position, size);
    }
}