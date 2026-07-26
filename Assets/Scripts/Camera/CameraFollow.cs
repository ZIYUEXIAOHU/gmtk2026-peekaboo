using UnityEngine;
using UnityEngine.SceneManagement;

public class CameraFollow : MonoBehaviour
{
    [Header("跟随设置")]
    public float smoothSpeed = 0.125f;
    public Vector3 offset = new Vector3(0, 1.5f, -10);
    [Tooltip("目标瞬移超过此距离时镜头直接贴上，避免从大厅慢慢飘到房间")]
    public float snapDistance = 8f;

    [Header("大厅边界（Waiting）")]
    public bool useLobbyBounds = true;
    public Vector2 lobbyMinBounds = new Vector2(-2, -8);
    public Vector2 lobbyMaxBounds = new Vector2(2, -3);

    [Header("对局地图边界（Prep / Playing）")]
    [Tooltip("四房间地图范围；关闭则进对局后不限制镜头")]
    public bool useMatchBounds = true;
    public Vector2 matchMinBounds = new Vector2(-18, -12);
    public Vector2 matchMaxBounds = new Vector2(18, 12);

    [Header("边界限制（运行时，由阶段切换）")]
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

    [System.Serializable]
    public class SceneBounds
    {
        public string sceneName;
        public Vector2 minBounds;
        public Vector2 maxBounds;
        public bool useBounds = true;
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

        Vector3 desiredPosition = target.position + offset;

        // 躲藏者 Prep 传送到房间后，立刻跟上，不要卡在大厅边界里慢慢追
        if ((desiredPosition - transform.position).sqrMagnitude > snapDistance * snapDistance)
        {
            velocity = Vector3.zero;
            Vector3 snapped = desiredPosition;
            if (useBounds)
            {
                snapped.x = Mathf.Clamp(snapped.x, minBounds.x, maxBounds.x);
                snapped.y = Mathf.Clamp(snapped.y, minBounds.y, maxBounds.y);
            }
            transform.position = snapped;
            return;
        }

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
    /// Waiting 用大厅小边界；Prep 起用四房间边界（或关闭限制），否则镜头会卡在大厅看不到躲藏者。
    /// </summary>
    void ApplyPhaseBounds(GamePhase phase)
    {
        // Waiting = 小队大厅；其余阶段都在四房间地图（含 Ended 结算）
        bool inMatch = phase != GamePhase.Waiting;

        if (inMatch)
        {
            useBounds = useMatchBounds;
            minBounds = matchMinBounds;
            maxBounds = matchMaxBounds;
            Debug.Log($"📏 对局镜头边界: use={useBounds} X({minBounds.x}~{maxBounds.x}) Y({minBounds.y}~{maxBounds.y})");
        }
        else
        {
            useBounds = useLobbyBounds;
            minBounds = lobbyMinBounds;
            maxBounds = lobbyMaxBounds;
            Debug.Log($"📏 大厅镜头边界: use={useBounds} X({minBounds.x}~{maxBounds.x}) Y({minBounds.y}~{maxBounds.y})");
        }
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

    void OnDrawGizmos()
    {
        if (!showBoundsInScene) return;

        if (useBounds)
            DrawBounds(minBounds, maxBounds, activeBoundsColor, "当前边界");

        if (useLobbyBounds)
            DrawBounds(lobbyMinBounds, lobbyMaxBounds, boundsColor, "大厅", dashed: true);

        if (useMatchBounds)
            DrawBounds(matchMinBounds, matchMaxBounds, new Color(0.2f, 0.6f, 1f), "对局", dashed: true);
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
