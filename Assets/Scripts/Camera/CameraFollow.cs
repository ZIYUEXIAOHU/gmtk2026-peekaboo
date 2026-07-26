using UnityEngine;
using UnityEngine.SceneManagement;

public class CameraFollow : MonoBehaviour
{
    [Header("跟随设置")]
    public float smoothSpeed = 0.125f;
    public Vector3 offset = new Vector3(0, 1.2f, -10);
    [Tooltip("目标瞬移超过此距离时镜头直接贴上，避免从大厅慢慢飘到房间")]
    public float snapDistance = 8f;

    [Header("大厅边界（Waiting）")]
    [Tooltip("大厅世界内容范围；镜头画面（含可视半宽半高）不会超出此矩形")]
    public bool useLobbyBounds = true;
    public Vector2 lobbyMinBounds = new Vector2(-11f, -13f);
    public Vector2 lobbyMaxBounds = new Vector2(11f, 4f);

    [Header("对局地图边界（Prep / Playing）")]
    [Tooltip("四房间整体范围；玩家不在任何楼层区间时的兜底边界")]
    public bool useMatchBounds = true;
    public Vector2 matchMinBounds = new Vector2(-22.5f, -12.2f);
    public Vector2 matchMaxBounds = new Vector2(22.5f, 11.8f);

    [Tooltip("对局分楼层边界：按玩家 Y 选择所在楼层，镜头只在该楼层矩形内活动")]
    public FloorBounds[] matchFloors = new FloorBounds[]
    {
        new FloorBounds
        {
            name = "二楼",
            playerMinY = -0.2f,
            playerMaxY = 1000f,
            minBounds = new Vector2(-22.5f, -0.4f),
            maxBounds = new Vector2(22.5f, 11.8f),
        },
        new FloorBounds
        {
            name = "一楼",
            playerMinY = -1000f,
            playerMaxY = -0.2f,
            minBounds = new Vector2(-22.5f, -12.2f),
            maxBounds = new Vector2(22.5f, -0.1f),
        },
    };

    [Header("边界切换平滑")]
    [Tooltip("楼层/阶段边界切换时，边界矩形过渡到位所需时间（秒）；0 = 立即切换")]
    public float boundsBlendTime = 0.35f;

    [Header("边界限制（运行时，由阶段/楼层刷新）")]
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
    private IGameEvents boundEvents;
    private bool phaseSubscribed;
    private Camera cam;
    private bool inMatchPhase;
    private int currentFloorIndex = -1;

    // 平滑过渡中的实际生效边界
    private Vector2 blendedMin;
    private Vector2 blendedMax;
    private Vector2 blendMinVelocity;
    private Vector2 blendMaxVelocity;
    private bool blendInitialized;

    [System.Serializable]
    public class SceneBounds
    {
        public string sceneName;
        public Vector2 minBounds;
        public Vector2 maxBounds;
        public bool useBounds = true;
    }

    [System.Serializable]
    public class FloorBounds
    {
        public string name;
        [Tooltip("玩家 Y 在此区间内即视为在该楼层")]
        public float playerMinY;
        public float playerMaxY;
        public Vector2 minBounds;
        public Vector2 maxBounds;

        public bool ContainsPlayerY(float y) => y >= playerMinY && y <= playerMaxY;
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Start()
    {
        // 兼容旧场景：若尚未填大厅边界，沿用 Inspector 里的 min/max
        if (lobbyMinBounds == Vector2.zero && lobbyMaxBounds == Vector2.zero
            && (minBounds != Vector2.zero || maxBounds != Vector2.zero))
        {
            lobbyMinBounds = minBounds;
            lobbyMaxBounds = maxBounds;
        }

        FindLocalPlayer();
        ApplySceneBounds();
        ApplyPhaseBounds(GameContract.IsBound ? GameContract.State?.Phase ?? GamePhase.Waiting : GamePhase.Waiting);
        TrySubscribePhase();
    }

    void OnEnable()
    {
        TrySubscribePhase();
    }

    void OnDisable()
    {
        UnsubscribePhase();
    }

    void Update()
    {
        TrySubscribePhase();

        if (Time.frameCount - lastCheckFrame > CheckInterval)
        {
            FindLocalPlayer();
            lastCheckFrame = Time.frameCount;
        }

        if (target == null)
        {
            FindLocalPlayer();
        }

        string newSceneName = SceneManager.GetActiveScene().name;
        if (newSceneName != currentSceneName)
        {
            currentSceneName = newSceneName;
            ApplySceneBounds();
            ApplyPhaseBounds(GameContract.IsBound ? GameContract.State?.Phase ?? GamePhase.Waiting : GamePhase.Waiting);
            Debug.Log($"📏 切换到场景: {currentSceneName}");
        }
    }

    void LateUpdate()
    {
        if (!isFollowing || target == null) return;

        RefreshTargetBounds();

        Vector3 desiredPosition = target.position + offset;
        bool farAway = (desiredPosition - transform.position).sqrMagnitude > snapDistance * snapDistance;

        if (farAway)
        {
            // 传送（Prep 分房等）：边界与镜头都立即到位
            velocity = Vector3.zero;
            SnapBlendedBounds();
            transform.position = ClampToViewBounds(desiredPosition);
            return;
        }

        BlendBounds();

        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref velocity,
            smoothSpeed
        );

        transform.position = ClampToViewBounds(smoothedPosition);
    }

    /// <summary>
    /// 按阶段 + 楼层刷新目标边界（minBounds/maxBounds）。
    /// </summary>
    void RefreshTargetBounds()
    {
        if (!inMatchPhase)
        {
            // 大厅：单一矩形，不分楼层
            useBounds = useLobbyBounds;
            minBounds = lobbyMinBounds;
            maxBounds = lobbyMaxBounds;
            return;
        }

        useBounds = useMatchBounds;
        minBounds = matchMinBounds;
        maxBounds = matchMaxBounds;
        SelectFloor(matchFloors);
    }

    /// <summary>按玩家 Y 从楼层表中选中对应边界；找不到则保持兜底整图边界。</summary>
    void SelectFloor(FloorBounds[] floors)
    {
        if (floors == null || floors.Length == 0 || target == null)
            return;

        float playerY = target.position.y;

        // 仍在当前楼层区间内则不切换，避免边界抖动
        if (currentFloorIndex >= 0 && currentFloorIndex < floors.Length
            && floors[currentFloorIndex] != null
            && floors[currentFloorIndex].ContainsPlayerY(playerY))
        {
            ApplyFloor(floors[currentFloorIndex]);
            return;
        }

        for (int i = 0; i < floors.Length; i++)
        {
            if (floors[i] != null && floors[i].ContainsPlayerY(playerY))
            {
                currentFloorIndex = i;
                ApplyFloor(floors[i]);
                return;
            }
        }
        // 不在任何楼层区间：保持兜底的整图边界
    }

    void ApplyFloor(FloorBounds floor)
    {
        minBounds = floor.minBounds;
        maxBounds = floor.maxBounds;
    }

    /// <summary>边界矩形向目标值平滑过渡，避免上下楼时镜头瞬跳。</summary>
    void BlendBounds()
    {
        if (!blendInitialized || boundsBlendTime <= 0f)
        {
            SnapBlendedBounds();
            return;
        }

        blendedMin = Vector2.SmoothDamp(blendedMin, minBounds, ref blendMinVelocity, boundsBlendTime);
        blendedMax = Vector2.SmoothDamp(blendedMax, maxBounds, ref blendMaxVelocity, boundsBlendTime);
    }

    void SnapBlendedBounds()
    {
        blendedMin = minBounds;
        blendedMax = maxBounds;
        blendMinVelocity = Vector2.zero;
        blendMaxVelocity = Vector2.zero;
        blendInitialized = true;
    }

    /// <summary>
    /// 按正交相机可视范围夹取镜头中心，画面四边不超出当前边界矩形。
    /// </summary>
    Vector3 ClampToViewBounds(Vector3 position)
    {
        if (!useBounds) return position;

        GetViewHalfExtents(out float halfW, out float halfH);
        GetCameraCenterLimits(blendedMin, blendedMax, halfW, halfH,
            out float minX, out float maxX, out float minY, out float maxY);

        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.y = Mathf.Clamp(position.y, minY, maxY);
        return position;
    }

    void GetViewHalfExtents(out float halfW, out float halfH)
    {
        if (cam == null)
            cam = GetComponent<Camera>();

        if (cam != null && cam.orthographic)
        {
            halfH = cam.orthographicSize;
            halfW = halfH * cam.aspect;
        }
        else
        {
            halfW = 0f;
            halfH = 0f;
        }
    }

    static void GetCameraCenterLimits(
        Vector2 worldMin,
        Vector2 worldMax,
        float halfW,
        float halfH,
        out float minX,
        out float maxX,
        out float minY,
        out float maxY)
    {
        minX = worldMin.x + halfW;
        maxX = worldMax.x - halfW;
        minY = worldMin.y + halfH;
        maxY = worldMax.y - halfH;

        // 视口比边界矩形大时：锁定到矩形中心线，避免任何一侧越界
        if (minX > maxX)
        {
            float cx = (worldMin.x + worldMax.x) * 0.5f;
            minX = maxX = cx;
        }

        if (minY > maxY)
        {
            float cy = (worldMin.y + worldMax.y) * 0.5f;
            minY = maxY = cy;
        }
    }

    void TrySubscribePhase()
    {
        if (!GameContract.IsBound || GameContract.Events == null) return;
        if (phaseSubscribed && ReferenceEquals(boundEvents, GameContract.Events)) return;

        UnsubscribePhase();
        boundEvents = GameContract.Events;
        boundEvents.OnPhaseChanged += OnPhaseChanged;
        phaseSubscribed = true;
        ApplyPhaseBounds(GameContract.State?.Phase ?? GamePhase.Waiting);
    }

    void UnsubscribePhase()
    {
        if (boundEvents != null)
            boundEvents.OnPhaseChanged -= OnPhaseChanged;
        else if (GameContract.IsBound && GameContract.Events != null)
            GameContract.Events.OnPhaseChanged -= OnPhaseChanged;

        boundEvents = null;
        phaseSubscribed = false;
    }

    void OnPhaseChanged(GamePhase phase, float duration)
    {
        ApplyPhaseBounds(phase);
    }

    /// <summary>
    /// Waiting 用大厅边界；Prep 起用对局楼层边界（含 Ended 结算）。
    /// </summary>
    void ApplyPhaseBounds(GamePhase phase)
    {
        inMatchPhase = phase != GamePhase.Waiting;
        currentFloorIndex = -1;
        RefreshTargetBounds();
        SnapBlendedBounds();
        Debug.Log($"📏 镜头边界（{(inMatchPhase ? "对局" : "大厅")}）: use={useBounds} X({minBounds.x}~{maxBounds.x}) Y({minBounds.y}~{maxBounds.y})");
    }

    void ApplySceneBounds()
    {
        if (sceneBounds == null || sceneBounds.Length == 0)
            return;

        string sceneName = SceneManager.GetActiveScene().name;
        currentSceneName = sceneName;

        foreach (var preset in sceneBounds)
        {
            if (preset.sceneName == sceneName)
            {
                // 场景预设视为大厅默认；对局边界仍由阶段切换
                useLobbyBounds = preset.useBounds;
                lobbyMinBounds = preset.minBounds;
                lobbyMaxBounds = preset.maxBounds;
                Debug.Log($"📏 应用场景 {sceneName} 大厅边界: X({lobbyMinBounds.x}~{lobbyMaxBounds.x}), Y({lobbyMinBounds.y}~{lobbyMaxBounds.y})");
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
                BindTarget(rp.transform);
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
                BindTarget(player.transform);
                Debug.Log($"✅ 相机跟随本地玩家(备用): {localPlayer.PlayerName}");
                return;
            }
        }

        Debug.LogWarning($"⚠️ 未找到 NetId {localPlayer.NetId} 对应的玩家");
    }

    void BindTarget(Transform t)
    {
        target = t;
        isFollowing = t != null;
        currentFloorIndex = -1;
        velocity = Vector3.zero;
    }

    public void SetBounds(Vector2 min, Vector2 max, bool use = true)
    {
        useBounds = use;
        minBounds = min;
        maxBounds = max;
        SnapBlendedBounds();
        Debug.Log($"📏 手动设置边界: {min} ~ {max}");
    }

    void OnDrawGizmos()
    {
        if (!showBoundsInScene) return;

        if (useBounds)
        {
            DrawBounds(minBounds, maxBounds, activeBoundsColor, "当前边界");
            DrawCameraCenterLimits(minBounds, maxBounds, activeBoundsColor * 0.7f, "镜头中心限");
        }

        if (useLobbyBounds)
            DrawBounds(lobbyMinBounds, lobbyMaxBounds, boundsColor, "大厅", dashed: true);

        if (useMatchBounds && matchFloors != null)
        {
            Color matchColor = new Color(0.2f, 0.6f, 1f);
            for (int i = 0; i < matchFloors.Length; i++)
            {
                if (matchFloors[i] == null) continue;
                DrawBounds(matchFloors[i].minBounds, matchFloors[i].maxBounds, matchColor,
                    string.IsNullOrEmpty(matchFloors[i].name) ? $"楼层{i}" : matchFloors[i].name,
                    dashed: true);
            }
        }
    }

    void DrawCameraCenterLimits(Vector2 worldMin, Vector2 worldMax, Color color, string label, bool dashed = false)
    {
        GetViewHalfExtents(out float halfW, out float halfH);
        GetCameraCenterLimits(worldMin, worldMax, halfW, halfH, out float minX, out float maxX, out float minY, out float maxY);
        DrawBounds(new Vector2(minX, minY), new Vector2(maxX, maxY), color, label, dashed);
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
                Gizmos.DrawLine(segStart, segEnd);

            currentLength += segLength;
            isDash = !isDash;
        }
    }
}
