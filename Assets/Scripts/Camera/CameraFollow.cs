using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("跟随设置")]
    public float smoothSpeed = 0.125f;
    public Vector3 offset = new Vector3(0, 1.5f, -10);
    
    [Header("边界限制")]
    public bool useBounds = false;
    public Vector2 minBounds;
    public Vector2 maxBounds;
    
    [Header("区域边界（同一场景不同房间）")]
    public ZoneBounds[] zoneBounds;
    
    [Header("可视化调试")]
    public bool showBoundsInScene = true;      // 在 Scene 视图中显示边界
    public Color boundsColor = Color.green;     // 边界颜色
    public Color activeBoundsColor = Color.red; // 激活边界颜色
    
    private Transform target;
    private Vector3 velocity = Vector3.zero;
    private bool isFollowing = false;
    private int lastCheckFrame = -1;
    private const int CheckInterval = 30;
    private uint lastLocalPlayerNetId = 0;
    private string currentZoneName = "";
    
    [System.Serializable]
    public class ZoneBounds
    {
        public string zoneName;
        public Vector2 minBounds;
        public Vector2 maxBounds;
        public bool useBounds = true;
    }
    
    void Start()
    {
        FindLocalPlayer();
    }
    
    void Update()
    {
        if (Time.frameCount - lastCheckFrame > CheckInterval)
        {
            FindLocalPlayer();
            lastCheckFrame = Time.frameCount;
        }
        
        if (target == null)
        {
            FindLocalPlayer();
        }
        
        UpdateZoneBounds();
    }
    
    void LateUpdate()
    {
        if (!isFollowing || target == null) return;
        
        Vector3 desiredPosition = target.position + offset;
        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref velocity,
            smoothSpeed
        );
        
        if (useBounds)
        {
            smoothedPosition.x = Mathf.Clamp(smoothedPosition.x, minBounds.x, maxBounds.x);
            smoothedPosition.y = Mathf.Clamp(smoothedPosition.y, minBounds.y, maxBounds.y);
        }
        
        transform.position = smoothedPosition;
    }
    
    void UpdateZoneBounds()
    {
        if (target == null) return;
        if (zoneBounds == null || zoneBounds.Length == 0) return;
        
        Vector2 playerPos = target.position;
        
        foreach (var zone in zoneBounds)
        {
            if (playerPos.x >= zone.minBounds.x && playerPos.x <= zone.maxBounds.x &&
                playerPos.y >= zone.minBounds.y && playerPos.y <= zone.maxBounds.y)
            {
                useBounds = zone.useBounds;
                minBounds = zone.minBounds;
                maxBounds = zone.maxBounds;
                
                if (currentZoneName != zone.zoneName)
                {
                    currentZoneName = zone.zoneName;
                    Debug.Log($"📏 进入区域: {zone.zoneName}, 边界: {minBounds} ~ {maxBounds}");
                }
                return;
            }
        }
    }
    
    void FindLocalPlayer()
    {
        if (!GameContract.IsBound)
        {
            Debug.LogWarning("⚠️ GameContract 未绑定，无法获取本地玩家");
            return;
        }
        
        IPlayerStateReadonly localPlayer = GameContract.State?.LocalPlayer;
        if (localPlayer == null)
        {
            return;
        }
        
        if (lastLocalPlayerNetId == localPlayer.NetId && target != null)
        {
            return;
        }
        
        lastLocalPlayerNetId = localPlayer.NetId;
        
        RoomPlayer[] roomPlayers = FindObjectsOfType<RoomPlayer>();
        foreach (var rp in roomPlayers)
        {
            if (rp != null && rp.netId == localPlayer.NetId)
            {
                target = rp.transform;
                isFollowing = true;
                Debug.Log($"✅ 相机跟随本地玩家: {localPlayer.PlayerName} (NetId: {localPlayer.NetId})");
                return;
            }
        }
        
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (var player in players)
        {
            RoomPlayer rp = player.GetComponent<RoomPlayer>();
            if (rp != null && rp.netId == localPlayer.NetId)
            {
                target = player.transform;
                isFollowing = true;
                Debug.Log($"✅ 相机跟随本地玩家(备用): {localPlayer.PlayerName}");
                return;
            }
        }
        
        Debug.LogWarning($"⚠️ 未找到 NetId {localPlayer.NetId} 对应的玩家");
    }
    
    public void SetBounds(Vector2 min, Vector2 max, bool use = true)
    {
        useBounds = use;
        minBounds = min;
        maxBounds = max;
        Debug.Log($"📏 手动设置边界: {min} ~ {max}");
    }
    
    // ==================== 可视化边界线 ====================
    void OnDrawGizmos()
    {
        if (!showBoundsInScene) return;
        
        // ===== 绘制当前激活的边界 =====
        if (useBounds)
        {
            DrawBounds(minBounds, maxBounds, activeBoundsColor, "当前边界");
        }
        
        // ===== 绘制所有区域边界 =====
        if (zoneBounds != null)
        {
            foreach (var zone in zoneBounds)
            {
                if (zone.useBounds)
                {
                    // 判断是否激活
                    bool isActive = (zone.minBounds == minBounds && zone.maxBounds == maxBounds);
                    Color color = isActive ? activeBoundsColor : boundsColor;
                    DrawBounds(zone.minBounds, zone.maxBounds, color, zone.zoneName, !isActive);
                }
            }
        }
    }
    
    void DrawBounds(Vector2 min, Vector2 max, Color color, string label = "", bool dashed = false)
    {
        // ===== 绘制矩形边框 =====
        Vector3[] corners = new Vector3[5];
        corners[0] = new Vector3(min.x, min.y, 0);
        corners[1] = new Vector3(max.x, min.y, 0);
        corners[2] = new Vector3(max.x, max.y, 0);
        corners[3] = new Vector3(min.x, max.y, 0);
        corners[4] = new Vector3(min.x, min.y, 0);
        
        Gizmos.color = color;
        
        if (dashed)
        {
            // 虚线效果（通过短线段模拟）
            float dashLength = 0.5f;
            float gapLength = 0.3f;
            DrawDashedLine(corners[0], corners[1], dashLength, gapLength);
            DrawDashedLine(corners[1], corners[2], dashLength, gapLength);
            DrawDashedLine(corners[2], corners[3], dashLength, gapLength);
            DrawDashedLine(corners[3], corners[0], dashLength, gapLength);
        }
        else
        {
            Gizmos.DrawLine(corners[0], corners[1]);
            Gizmos.DrawLine(corners[1], corners[2]);
            Gizmos.DrawLine(corners[2], corners[3]);
            Gizmos.DrawLine(corners[3], corners[0]);
        }
        
        // ===== 绘制标签（在编辑器模式下） =====
        #if UNITY_EDITOR
        if (!string.IsNullOrEmpty(label))
        {
            Vector3 center = new Vector3((min.x + max.x) / 2, (max.y + 0.3f), 0);
            UnityEditor.Handles.Label(center, label);
            
            // 显示坐标
            Vector3 bottomCenter = new Vector3((min.x + max.x) / 2, min.y - 0.3f, 0);
            UnityEditor.Handles.Label(bottomCenter, $"({min.x:F1}, {min.y:F1}) ~ ({max.x:F1}, {max.y:F1})");
        }
        #endif
    }
    
    void DrawDashedLine(Vector3 start, Vector3 end, float dashLength, float gapLength)
    {
        float totalLength = Vector3.Distance(start, end);
        Vector3 direction = (end - start).normalized;
        float currentLength = 0f;
        bool isDash = true;
        
        while (currentLength < totalLength)
        {
            float segLength = isDash ? dashLength : gapLength;
            if (currentLength + segLength > totalLength)
                segLength = totalLength - currentLength;
            
            Vector3 segStart = start + direction * currentLength;
            Vector3 segEnd = start + direction * (currentLength + segLength);
            
            if (isDash)
            {
                Gizmos.DrawLine(segStart, segEnd);
            }
            
            currentLength += segLength;
            isDash = !isDash;
        }
    }
}