using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class SeekerMouseController : NetworkBehaviour
{
    [Header("鼠标贴图")]
    public Texture2D defaultCursor;
    public Texture2D attackCursor;
    public Vector2 hotSpot = Vector2.zero;
    
    [Header("攻击设置")]
    public KeyCode attackKey = KeyCode.Mouse0;
    public float attackRange = 1.5f;
    public LayerMask targetLayer;
    
    [Header("攻击冷却")]
    public float attackCooldown = 0.5f;
    private float lastAttackTime = 0f;
    
    private bool isLocalPlayerReady = false;
    private HashSet<uint> hiderNetIds = new HashSet<uint>();  // 缓存存活躲藏者 NetId
    
    void Start()
    {
        if (!isLocalPlayer) return;
        
        isLocalPlayerReady = true;
        
        if (defaultCursor != null)
        {
            Cursor.SetCursor(defaultCursor, hotSpot, CursorMode.Auto);
        }
    }
    
    void Update()
    {
        if (!isLocalPlayerReady || !isLocalPlayer) return;
        
        // ===== 更新存活躲藏者列表（通过契约） =====
        UpdateHiderList();
        
        // ===== 检测鼠标悬停目标 =====
        DetectHoverTarget();
        
        // ===== 鼠标左键攻击 =====
        if (Input.GetKeyDown(attackKey))
        {
            Attack();
        }
    }
    
    /// <summary>
    /// 通过契约获取所有存活躲藏者的 NetId
    /// </summary>
    void UpdateHiderList()
    {
        hiderNetIds.Clear();
        
        if (!GameContract.IsBound || GameContract.State == null) return;
        
        foreach (var player in GameContract.State.Players)
        {
            if (player != null && 
                player.Role == PlayerRole.Hider && 
                player.HiderState != HiderState.Captured)
            {
                hiderNetIds.Add(player.NetId);
            }
        }
    }
    
    void DetectHoverTarget()
    {
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 mousePosition = new Vector2(mousePos.x, mousePos.y);
        
        RaycastHit2D hit = Physics2D.Raycast(mousePosition, Vector2.zero, 0f, targetLayer);
        
        bool isTargetHider = false;
        
        if (hit.collider != null)
        {
            // ===== 通过契约检查是否是躲藏者 =====
            RoomPlayer rp = hit.collider.GetComponent<RoomPlayer>();
            if (rp != null && hiderNetIds.Contains(rp.netId))
            {
                isTargetHider = true;
            }
        }
        
        // ===== 切换鼠标 =====
        if (isTargetHider && attackCursor != null)
        {
            Cursor.SetCursor(attackCursor, hotSpot, CursorMode.Auto);
        }
        else if (defaultCursor != null)
        {
            Cursor.SetCursor(defaultCursor, hotSpot, CursorMode.Auto);
        }
    }
    
    void Attack()
    {
        if (Time.time - lastAttackTime < attackCooldown) return;

        SeekerController seeker = GetComponentInParent<SeekerController>();
        if (seeker != null && seeker.IsAttackMoveLocked)
            return;
        
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 attackPoint = new Vector2(mousePos.x, mousePos.y);
        
        Collider2D[] hits = Physics2D.OverlapCircleAll(attackPoint, attackRange, targetLayer);
        
        foreach (var hit in hits)
        {
            if (hit == null) continue;
            
            RoomPlayer rp = hit.GetComponent<RoomPlayer>();
            if (rp != null && hiderNetIds.Contains(rp.netId))
            {
                // ===== 攻击命中躲藏者 =====
                lastAttackTime = Time.time;
                Debug.Log($"⚔️ 攻击躲藏者: {rp.playerName}");

                seeker?.BeginAttack();
                
                if (GameContract.IsBound)
                {
                    GameContract.Commands.Slash();
                }
                
                ShowAttackEffect(attackPoint);
                return;
            }
        }
        
        Debug.Log("💨 未命中");
        lastAttackTime = Time.time;
        // 空挥也播攻击并硬直，避免边跑边挥
        seeker?.BeginAttack();
    }
    
    void ShowAttackEffect(Vector2 position)
    {
        Debug.Log($"💥 攻击位置: {position}");
        // TODO: 添加特效预制体
    }
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}