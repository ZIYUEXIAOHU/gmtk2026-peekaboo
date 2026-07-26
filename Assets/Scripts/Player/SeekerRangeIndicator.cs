using UnityEngine;

/// <summary>
/// 抓捕者探测圈表现：可见椭圆半轴 = InvestigateRangeX / InvestigateRangeY，
/// 并高亮探测范围内、鼠标判定半径内最近的可调查目标（InvestigableObject 或伪装中的躲藏者本体）。
/// 不因「圈内有躲藏者」变红，避免直接暴露伪装。
/// </summary>
public class SeekerRangeIndicator : MonoBehaviour
{
    [Header("范围设置（运行时强制对齐 InvestigateRangeX/Y）")]
    public float detectRadiusX = GameConstants.InvestigateRangeX;
    public float detectRadiusY = GameConstants.InvestigateRangeY;
    public LayerMask targetLayer = ~0;

    [Header("发光效果")]
    public SpriteRenderer indicatorSprite;
    public Color normalColor = new Color(1f, 1f, 1f, 0.3f);
    public Color activeColor = new Color(1f, 0.85f, 0.2f, 0.45f);
    public float pulseSpeed = 2f;

    [Header("高亮")]
    public Color highlightTint = new Color(1f, 0.92f, 0.35f, 1f);

    [Header("检测")]
    public Transform detectCenter;

    [Header("调试")]
    public bool showGizmos = true;
    public Color gizmoColor = Color.green;
    public Color gizmoActiveColor = Color.yellow;

    private bool hasHighlightTarget;
    private SpriteRenderer highlightedRenderer;
    private Color highlightedOriginalColor;
    private bool highlightedHadOriginal;
    private RoomPlayer roomPlayer;

    void Awake()
    {
        CacheRefs();
    }

    void Start()
    {
        CacheRefs();
        detectRadiusX = GameConstants.InvestigateRangeX;
        detectRadiusY = GameConstants.InvestigateRangeY;
        if (indicatorSprite != null)
            indicatorSprite.color = normalColor;
        ApplyIndicatorScale(1f);
    }

    void OnDisable()
    {
        ClearHighlight();
    }

    void Update()
    {
        CacheRefs();
        detectRadiusX = GameConstants.InvestigateRangeX;
        detectRadiusY = GameConstants.InvestigateRangeY;

        bool show = ShouldShowIndicator();
        if (indicatorSprite != null)
        {
            if (!indicatorSprite.gameObject.activeSelf)
                indicatorSprite.gameObject.SetActive(true);
            indicatorSprite.enabled = show;
            if (show && detectCenter != null)
            {
                // 世界对齐：不受父节点非等比缩放/旋转影响（根节点 3.5×4）
                indicatorSprite.transform.SetPositionAndRotation(
                    detectCenter.position, Quaternion.identity);
            }
        }

        if (!show)
        {
            ClearHighlight();
            hasHighlightTarget = false;
            return;
        }

        FindAndHighlightUnderCursor();
        UpdateIndicator();
    }

    void CacheRefs()
    {
        if (roomPlayer == null)
            roomPlayer = GetComponentInParent<RoomPlayer>();

        if (detectCenter == null)
        {
            detectCenter = roomPlayer != null ? roomPlayer.transform : transform;
        }

        if (indicatorSprite == null)
        {
            Transform t = transform.Find("RangeIndicator");
            if (t != null)
                indicatorSprite = t.GetComponent<SpriteRenderer>();
        }
    }

    bool ShouldShowIndicator()
    {
        if (roomPlayer == null) return false;
        if (!roomPlayer.isLocalPlayer || roomPlayer.Role != PlayerRole.Seeker)
            return false;
        // Prep：抓捕者近视且不可调查，隐藏探测圈
        if (GameContract.IsBound && GameContract.State != null
            && GameContract.State.Phase == GamePhase.Prep)
            return false;
        return true;
    }

    void FindAndHighlightUnderCursor()
    {
        Vector2 origin = detectCenter != null ? (Vector2)detectCenter.position : (Vector2)transform.position;
        Vector2 mousePos = GetMouseWorldPosition();
        float pickRadius = GameConstants.InvestigateCursorPickRadius;
        float bestDist = float.MaxValue;
        SpriteRenderer bestRenderer = null;

        foreach (InvestigableObject obj in FindObjectsOfType<InvestigableObject>())
        {
            if (obj == null) continue;
            Vector2 pos = obj.transform.position;
            if (!GameConstants.IsInInvestigateRange(origin, pos)) continue;

            float dMouse = Vector2.Distance(mousePos, pos);
            if (dMouse > pickRadius || dMouse >= bestDist) continue;

            SpriteRenderer sr = ResolveItemSprite(obj.transform);
            if (sr == null || !sr.enabled) continue;
            bestDist = dMouse;
            bestRenderer = sr;
        }

        foreach (RoomPlayer rp in FindObjectsOfType<RoomPlayer>())
        {
            if (rp == null || rp.Role != PlayerRole.Hider) continue;
            if (rp.hiderState != HiderState.Disguised && rp.hiderState != HiderState.Invisible)
                continue;

            Vector2 pos = rp.transform.position;
            if (!GameConstants.IsInInvestigateRange(origin, pos)) continue;

            float dMouse = Vector2.Distance(mousePos, pos);
            if (dMouse > pickRadius || dMouse >= bestDist) continue;

            SpriteRenderer sr = ResolveHiderSprite(rp);
            if (sr == null || !sr.enabled) continue;

            bestDist = dMouse;
            bestRenderer = sr;
        }

        hasHighlightTarget = bestRenderer != null;

        if (bestRenderer == highlightedRenderer)
            return;

        ClearHighlight();
        if (bestRenderer != null)
        {
            highlightedRenderer = bestRenderer;
            highlightedOriginalColor = bestRenderer.color;
            highlightedHadOriginal = true;
            bestRenderer.color = highlightTint;
        }
    }

    static Vector2 GetMouseWorldPosition()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return Vector2.zero;
        Vector3 p = cam.ScreenToWorldPoint(Input.mousePosition);
        return new Vector2(p.x, p.y);
    }

    static SpriteRenderer ResolveItemSprite(Transform root)
    {
        Transform bob = root.Find("BobVisual");
        if (bob != null)
        {
            SpriteRenderer bobSr = bob.GetComponent<SpriteRenderer>();
            if (bobSr != null) return bobSr;
        }

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
                return renderers[i];
        }
        return root.GetComponent<SpriteRenderer>();
    }

    static SpriteRenderer ResolveHiderSprite(RoomPlayer rp)
    {
        if (rp.visualHider != null)
        {
            SpriteRenderer sr = rp.visualHider.GetComponent<SpriteRenderer>();
            if (sr != null) return sr;
        }

        Transform visual = rp.transform.Find("Visual_Hider");
        return visual != null ? visual.GetComponent<SpriteRenderer>() : null;
    }

    void ClearHighlight()
    {
        if (highlightedRenderer != null && highlightedHadOriginal)
            highlightedRenderer.color = highlightedOriginalColor;

        highlightedRenderer = null;
        highlightedHadOriginal = false;
    }

    void UpdateIndicator()
    {
        if (indicatorSprite == null) return;

        float pulse = Mathf.Sin(Time.time * pulseSpeed) * 0.3f + 0.7f;
        ApplyIndicatorScale(hasHighlightTarget ? 1f + pulse * 0.05f : 1f);

        if (hasHighlightTarget)
        {
            Color targetColor = activeColor;
            targetColor.a = activeColor.a * pulse;
            indicatorSprite.color = targetColor;
        }
        else
        {
            indicatorSprite.color = normalColor;
        }
    }

    /// <summary>
    /// 按世界半轴对齐椭圆 Sprite：对父节点 X/Y 非等比 lossyScale 分别补偿，
    /// 再按 InvestigateRangeX/Y 拉成竖直略长的探测椭圆。
    /// </summary>
    void ApplyIndicatorScale(float pulseMul)
    {
        if (indicatorSprite == null) return;

        float parentLossyX = 1f;
        float parentLossyY = 1f;
        if (indicatorSprite.transform.parent != null)
        {
            Vector3 ls = indicatorSprite.transform.parent.lossyScale;
            parentLossyX = Mathf.Max(0.0001f, Mathf.Abs(ls.x));
            parentLossyY = Mathf.Max(0.0001f, Mathf.Abs(ls.y));
        }

        float spriteRadiusX = 0.5f;
        float spriteRadiusY = 0.5f;
        if (indicatorSprite.sprite != null)
        {
            Bounds b = indicatorSprite.sprite.bounds;
            spriteRadiusX = Mathf.Max(0.0001f, b.extents.x);
            spriteRadiusY = Mathf.Max(0.0001f, b.extents.y);
        }

        float sx = detectRadiusX / (parentLossyX * spriteRadiusX) * pulseMul;
        float sy = detectRadiusY / (parentLossyY * spriteRadiusY) * pulseMul;
        indicatorSprite.transform.localScale = new Vector3(sx, sy, 1f);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (detectCenter == null) detectCenter = transform;

        float rx = Application.isPlaying ? detectRadiusX : GameConstants.InvestigateRangeX;
        float ry = Application.isPlaying ? detectRadiusY : GameConstants.InvestigateRangeY;
        Color currentGizmoColor = hasHighlightTarget ? gizmoActiveColor : gizmoColor;
        DrawEllipseGizmo(detectCenter.position, rx, ry, currentGizmoColor, filled: true);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(detectCenter.position, 0.1f);
    }

    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        if (detectCenter == null) return;

        float rx = Application.isPlaying ? detectRadiusX : GameConstants.InvestigateRangeX;
        float ry = Application.isPlaying ? detectRadiusY : GameConstants.InvestigateRangeY;
        DrawEllipseGizmo(detectCenter.position, rx, ry, Color.cyan, filled: false);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            detectCenter.position + new Vector3(rx, 0, 0),
            $"探测椭圆: {rx:F1}×{ry:F1}"
        );
#endif
    }

    static void DrawEllipseGizmo(Vector3 center, float radiusX, float radiusY, Color color, bool filled)
    {
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(radiusX, radiusY, 1f));
        Gizmos.color = color;
        Gizmos.DrawWireSphere(Vector3.zero, 1f);
        if (filled)
        {
            Gizmos.color = new Color(color.r, color.g, color.b, 0.15f);
            Gizmos.DrawSphere(Vector3.zero, 1f);
        }
        Gizmos.matrix = old;
    }
}
