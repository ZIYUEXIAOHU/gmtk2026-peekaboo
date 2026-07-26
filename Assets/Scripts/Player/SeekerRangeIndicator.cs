using UnityEngine;
using System.Collections.Generic;

public class SeekerRangeIndicator : MonoBehaviour
{
    [Header("范围设置")]
    public float detectRadius = 5f;
    public LayerMask targetLayer;
    
    [Header("发光效果")]
    public SpriteRenderer indicatorSprite;
    public Color normalColor = new Color(1f, 1f, 1f, 0.3f);
    public Color activeColor = new Color(1f, 0.3f, 0.1f, 0.6f);
    public float pulseSpeed = 2f;
    
    [Header("检测")]
    public Transform detectCenter;
    
    [Header("调试")]
    public bool showGizmos = true;
    public Color gizmoColor = Color.green;
    public Color gizmoActiveColor = Color.red;
    
    private bool isPlayerDetected = false;
    private Collider2D[] detectedTargets = new Collider2D[20];
    private Vector3 baseScale = Vector3.one;
    
    void Start()
    {
        if (detectCenter == null)
            detectCenter = transform;
        
        if (indicatorSprite != null)
        {
            baseScale = indicatorSprite.transform.localScale;
            indicatorSprite.color = normalColor;
        }
    }
    
    void Update()
    {
        DetectPlayers();
        UpdateIndicator();
    }
    
    void DetectPlayers()
    {
        // ===== 物理检测范围内的碰撞体 =====
        int hitCount = Physics2D.OverlapCircleNonAlloc(
            detectCenter.position,
            detectRadius,
            detectedTargets,
            targetLayer
        );
        
        isPlayerDetected = false;
        
        // ===== 契约未绑定时，用旧方法 =====
        if (!GameContract.IsBound || GameContract.State == null)
        {
            for (int i = 0; i < hitCount; i++)
            {
                if (detectedTargets[i] == null) continue;
                RoomPlayer rp = detectedTargets[i].GetComponent<RoomPlayer>();
                if (rp != null && rp.Role == PlayerRole.Hider && rp.hiderState != HiderState.Captured)
                {
                    isPlayerDetected = true;
                    return;
                }
            }
            return;
        }
        
        // ===== 通过契约获取所有存活躲藏者的 NetId =====
        HashSet<uint> hiderNetIds = new HashSet<uint>();
        foreach (var player in GameContract.State.Players)
        {
            if (player != null && 
                player.Role == PlayerRole.Hider && 
                player.HiderState != HiderState.Captured)
            {
                hiderNetIds.Add(player.NetId);
            }
        }
        
        // ===== 检查物理检测到的物体是否匹配 =====
        for (int i = 0; i < hitCount; i++)
        {
            if (detectedTargets[i] == null) continue;
            
            RoomPlayer rp = detectedTargets[i].GetComponent<RoomPlayer>();
            if (rp != null && hiderNetIds.Contains(rp.netId))
            {
                isPlayerDetected = true;
                return;
            }
        }
    }
    
    void UpdateIndicator()
    {
        if (indicatorSprite == null) return;
        
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * 0.3f + 0.7f;
        
        if (isPlayerDetected)
        {
            Color targetColor = activeColor;
            targetColor.a = activeColor.a * pulse;
            indicatorSprite.color = targetColor;
            indicatorSprite.transform.localScale = baseScale * (1f + pulse * 0.05f);
        }
        else
        {
            indicatorSprite.color = normalColor;
            indicatorSprite.transform.localScale = baseScale;
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (detectCenter == null) detectCenter = transform;
        
        Color currentGizmoColor = isPlayerDetected ? gizmoActiveColor : gizmoColor;
        Gizmos.color = currentGizmoColor;
        Gizmos.DrawWireSphere(detectCenter.position, detectRadius);
        
        Gizmos.color = new Color(currentGizmoColor.r, currentGizmoColor.g, currentGizmoColor.b, 0.15f);
        Gizmos.DrawSphere(detectCenter.position, detectRadius);
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(detectCenter.position, 0.1f);
    }
    
    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        if (detectCenter == null) return;
        
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(detectCenter.position, detectRadius);
        
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(
            detectCenter.position + new Vector3(detectRadius, 0, 0),
            $"半径: {detectRadius:F1}"
        );
        #endif
    }
}