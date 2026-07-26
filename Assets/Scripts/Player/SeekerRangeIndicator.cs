using UnityEngine;

/// <summary>
/// 抓捕者探测圈表现：可见圈半径 = GameConstants.InvestigateRange，
/// 并高亮范围内最近的可调查目标（InvestigableObject 或伪装中的躲藏者本体）。
/// 不因「圈内有躲藏者」变红，避免直接暴露伪装。
/// </summary>
public class SeekerRangeIndicator : MonoBehaviour
{
    [Header("范围设置（运行时强制对齐 InvestigateRange）")]
    public float detectRadius = GameConstants.InvestigateRange;
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
        detectRadius = GameConstants.InvestigateRange;
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
        detectRadius = GameConstants.InvestigateRange;

        bool show = ShouldShowIndicator();
        if (indicatorSprite != null)
        {
            if (!indicatorSprite.gameObject.activeSelf)
                indicatorSprite.gameObject.SetActive(true);
            indicatorSprite.enabled = show;
            if (show && detectCenter != null)
                indicatorSprite.transform.position = detectCenter.position;
        }

        if (!show)
        {
            ClearHighlight();
            hasHighlightTarget = false;
            return;
        }

        FindAndHighlightNearest();
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
        return roomPlayer.isLocalPlayer && roomPlayer.Role == PlayerRole.Seeker;
    }

    void FindAndHighlightNearest()
    {
        Vector2 origin = detectCenter != null ? (Vector2)detectCenter.position : (Vector2)transform.position;
        float bestDist = float.MaxValue;
        SpriteRenderer bestRenderer = null;

        foreach (InvestigableObject obj in FindObjectsOfType<InvestigableObject>())
        {
            if (obj == null) continue;
            float d = Vector2.Distance(origin, obj.transform.position);
            if (d > detectRadius || d >= bestDist) continue;

            SpriteRenderer sr = ResolveItemSprite(obj.transform);
            if (sr == null || !sr.enabled) continue;
            bestDist = d;
            bestRenderer = sr;
        }

        foreach (RoomPlayer rp in FindObjectsOfType<RoomPlayer>())
        {
            if (rp == null || rp.Role != PlayerRole.Hider) continue;
            if (rp.hiderState != HiderState.Disguised && rp.hiderState != HiderState.Invisible)
                continue;

            float d = Vector2.Distance(origin, rp.transform.position);
            if (d > detectRadius || d >= bestDist) continue;

            SpriteRenderer sr = ResolveHiderSprite(rp);
            if (sr == null || !sr.enabled) continue;

            bestDist = d;
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

    /// <summary>按世界半径对齐圆 Sprite 的 localScale（不受父节点 0.1 / 3.5×4 缩放干扰）。</summary>
    void ApplyIndicatorScale(float pulseMul)
    {
        if (indicatorSprite == null) return;

        float parentLossy = 1f;
        if (indicatorSprite.transform.parent != null)
        {
            Vector3 ls = indicatorSprite.transform.parent.lossyScale;
            parentLossy = Mathf.Max(0.0001f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y)) * 0.5f);
        }

        float spriteRadius = 0.5f;
        if (indicatorSprite.sprite != null)
            spriteRadius = Mathf.Max(0.0001f, indicatorSprite.sprite.bounds.extents.x);

        float local = detectRadius / (parentLossy * spriteRadius);
        float s = local * pulseMul;
        indicatorSprite.transform.localScale = new Vector3(s, s, 1f);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (detectCenter == null) detectCenter = transform;

        float radius = Application.isPlaying ? detectRadius : GameConstants.InvestigateRange;
        Color currentGizmoColor = hasHighlightTarget ? gizmoActiveColor : gizmoColor;
        Gizmos.color = currentGizmoColor;
        Gizmos.DrawWireSphere(detectCenter.position, radius);

        Gizmos.color = new Color(currentGizmoColor.r, currentGizmoColor.g, currentGizmoColor.b, 0.15f);
        Gizmos.DrawSphere(detectCenter.position, radius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(detectCenter.position, 0.1f);
    }

    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        if (detectCenter == null) return;

        float radius = Application.isPlaying ? detectRadius : GameConstants.InvestigateRange;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(detectCenter.position, radius);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            detectCenter.position + new Vector3(radius, 0, 0),
            $"探测圈: {radius:F1}"
        );
#endif
    }
}
