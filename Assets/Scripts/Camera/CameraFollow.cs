using UnityEngine;
using UnityEngine.SceneManagement;

public class CameraFollow : MonoBehaviour
{
    [Header("跟随设置")]
    public float smoothSpeed = 0.125f;
    public Vector3 offset = new Vector3(0, 1.5f, -10);
    
    [Header("边界限制")]
    public bool useBounds = false;
    public Vector2 minBounds;
    public Vector2 maxBounds;
    
    [Header("场景边界预设（根据场景切换）")]
    public SceneBounds[] sceneBounds;
    
    [Header("可视化调试")]
    public bool showBoundsInScene = true;
    public Color boundsColor = Color.green;
    public Color activeBoundsColor = Color.red;
    
    private Transform target;
    private Vector3 velocity = Vector3.zero;
    private bool isFollowing = false;
    private int lastCheckFrame = -1;
    private const int CheckInterval = 30;
    private uint lastLocalPlayerNetId = 0;
    private string currentSceneName = "";
    
    [System.Serializable]
    public class SceneBounds
    {
        public string sceneName;        // 场景名称（LobbyScene / GameScene）
        public Vector2 minBounds;
        public Vector2 maxBounds;
        public bool useBounds = true;
    }
    
    void Start()
    {
        FindLocalPlayer();
        ApplySceneBounds();
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
        
        // ===== 检测场景变化 =====
        string newSceneName = SceneManager.GetActiveScene().name;
        if (newSceneName != currentSceneName)
        {
            currentSceneName = newSceneName;
            ApplySceneBounds();
            Debug.Log($"📏 切换到场景: {currentSceneName}");
        }
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
    
    /// <summary>
    /// 根据当前场景应用边界
    /// </summary>
    void ApplySceneBounds()
    {
        if (sceneBounds == null || sceneBounds.Length == 0)
        {
            Debug.Log("📏 没有场景边界预设，使用当前设置");
            return;
        }
        
        string sceneName = SceneManager.GetActiveScene().name;
        currentSceneName = sceneName;
        
        foreach (var preset in sceneBounds)
        {
            if (preset.sceneName == sceneName)
            {
                useBounds = preset.useBounds;
                minBounds = preset.minBounds;
                maxBounds = preset.maxBounds;
                Debug.Log($"📏 应用场景 {sceneName} 边界: X({minBounds.x}~{maxBounds.x}), Y({minBounds.y}~{maxBounds.y})");
                return;
            }
        }
        
        Debug.Log($"⚠️ 未找到场景 {sceneName} 的边界预设，使用默认设置");
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
        
        if (useBounds)
        {
            DrawBounds(minBounds, maxBounds, activeBoundsColor, "当前边界");
        }
        
        if (sceneBounds != null)
        {
            foreach (var preset in sceneBounds)
            {
                if (preset.useBounds)
                {
                    bool isActive = (preset.minBounds == minBounds && preset.maxBounds == maxBounds);
                    Color color = isActive ? activeBoundsColor : boundsColor;
                    DrawBounds(preset.minBounds, preset.maxBounds, color, preset.sceneName, !isActive);
                }
            }
        }
    }
    
    void DrawBounds(Vector2 min, Vector2 max, Color color, string label = "", bool dashed = false)
    {
        Vector3[] corners = new Vector3[5];
        corners[0] = new Vector3(min.x, min.y, 0);
        corners[1] = new Vector3(max.x, min.y, 0);
        corners[2] = new Vector3(max.x, max.y, 0);
        corners[3] = new Vector3(min.x, max.y, 0);
        corners[4] = new Vector3(min.x, min.y, 0);
        
        Gizmos.color = color;
        
        if (dashed)
        {
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
        
        #if UNITY_EDITOR
        if (!string.IsNullOrEmpty(label))
        {
            Vector3 center = new Vector3((min.x + max.x) / 2, (max.y + 0.3f), 0);
            UnityEditor.Handles.Label(center, label);
            
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