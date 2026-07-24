using UnityEngine;

public class ContentHeightLimiter : MonoBehaviour
{
    public RectTransform viewport;          // Viewport
    public RectTransform scorePanel;        // ScorePanel（父面板）
    public float itemHeight = 28f;          // 每个 Item 的高度
    public float maxHeight = 200f;          // 最大高度
    public float minHeight = 40f;           // 最小高度
    public float paddingTop = 30f;          // 标题占用的高度
    public float paddingBottom = 10f;       // 底部内边距
    
    private RectTransform rect;
    private int lastChildCount = -1;
    private float cachedContentHeight = 40f;
    private bool isExpanded = true;
    private bool isInitialized = false;
    
    void Start()
    {
        rect = GetComponent<RectTransform>();
        if (rect == null)
        {
            Debug.LogWarning("⚠️ ContentHeightLimiter: RectTransform 未找到！");
            return;
        }
        
        // 确保锚点在顶部
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, 0);
        
        isInitialized = true;
        
        // 延迟刷新，等待子物体生成
        Invoke(nameof(DelayedRefresh), 0.1f);
        
        Debug.Log($"✅ ContentHeightLimiter 初始化完成，目标 Panel: {(scorePanel != null ? scorePanel.name : "null")}");
    }
    
    void DelayedRefresh()
    {
        Refresh();
    }
    
    void Update()
    {
        if (!isInitialized) return;
        if (rect == null) return;
        if (!isExpanded) return;
        
        int childCount = GetActiveChildCount();
        if (childCount == lastChildCount) return;
        lastChildCount = childCount;
        
        UpdateHeight();
    }
    
    /// <summary>
    /// 切换展开/折叠
    /// </summary>
    public void Toggle(bool expand)
    {
        isExpanded = expand;
        
        if (rect == null) rect = GetComponent<RectTransform>();
        if (rect == null) return;
        
        if (isExpanded)
        {
            float targetHeight = Mathf.Max(cachedContentHeight, minHeight);
            
            // 更新 Content
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);
            rect.anchoredPosition = new Vector2(0, 0);
            
            // 同步更新 Viewport
            if (viewport != null)
            {
                viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);
                viewport.anchoredPosition = new Vector2(0, 0);
            }
            
            if (scorePanel != null)
            {
                float panelHeight = targetHeight + paddingTop + paddingBottom;
                scorePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
                Debug.Log($"📏 展开 Panel 高度: {panelHeight}");
            }
            
            Debug.Log($"📏 展开 Content 高度: {targetHeight}");
        }
        else
        {
            cachedContentHeight = rect.rect.height;
            if (cachedContentHeight < minHeight) cachedContentHeight = minHeight;
            
            // 更新 Content
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0f);
            rect.anchoredPosition = new Vector2(0, 0);
            
            // 同步更新 Viewport
            if (viewport != null)
            {
                viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0f);
                viewport.anchoredPosition = new Vector2(0, 0);
            }
            
            if (scorePanel != null)
            {
                scorePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, paddingTop + paddingBottom);
                Debug.Log($"📏 折叠 Panel 高度: {paddingTop + paddingBottom}");
            }
            
            Debug.Log($"📏 折叠 Content 高度: 0 (缓存: {cachedContentHeight})");
        }
    }
    
    /// <summary>
    /// 外部调用：刷新高度
    /// </summary>
    public void Refresh()
    {
        if (rect == null) rect = GetComponent<RectTransform>();
        if (rect == null) return;
        
        Debug.Log($"🔄 Refresh 被调用，isExpanded={isExpanded}");
        
        if (isExpanded)
        {
            lastChildCount = -1;
            UpdateHeight();
        }
        else
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0f);
            rect.anchoredPosition = new Vector2(0, 0);
            
            if (viewport != null)
            {
                viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0f);
                viewport.anchoredPosition = new Vector2(0, 0);
            }
            
            if (scorePanel != null)
            {
                scorePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, paddingTop + paddingBottom);
            }
        }
    }
    
    /// <summary>
    /// 计算并更新 Content、Viewport 和 ScorePanel 的高度
    /// </summary>
    void UpdateHeight()
    {
        if (rect == null) return;
        
        int childCount = GetActiveChildCount();
        Debug.Log($"📊 UpdateHeight: childCount={childCount}");
        
        // 计算高度
        float contentHeight;
        if (childCount > 0)
        {
            contentHeight = childCount * itemHeight + paddingBottom;
        }
        else
        {
            contentHeight = minHeight;
        }
        
        // 限制最大/最小高度
        contentHeight = Mathf.Clamp(contentHeight, minHeight, maxHeight);
        
        // 缓存高度
        cachedContentHeight = contentHeight;
        
        // 更新 Content 高度
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        rect.anchoredPosition = new Vector2(0, 0);
        Debug.Log($"📏 Content 高度已更新: {contentHeight} (子物体数: {childCount})");
        
        // 同步更新 Viewport 高度
        if (viewport != null)
        {
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
            viewport.anchoredPosition = new Vector2(0, 0);
            Debug.Log($"📏 Viewport 高度已同步: {contentHeight}");
        }
        
        // 更新 Panel 高度
        if (scorePanel != null)
        {
            float panelHeight = contentHeight + paddingTop + paddingBottom;
            panelHeight = Mathf.Min(panelHeight, maxHeight + paddingTop + paddingBottom);
            scorePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
            Debug.Log($"📏 ScorePanel 高度已更新: {panelHeight} (Panel: {scorePanel.name})");
        }
        else
        {
            Debug.LogWarning("⚠️ scorePanel 未绑定！");
        }
    }
    
    /// <summary>
    /// 获取活跃的子物体数量
    /// </summary>
    int GetActiveChildCount()
    {
        int count = 0;
        for (int i = 0; i < rect.childCount; i++)
        {
            Transform child = rect.GetChild(i);
            if (child != null && child.gameObject.activeSelf)
            {
                count++;
            }
        }
        return count;
    }
    
    /// <summary>
    /// 获取当前是否展开
    /// </summary>
    public bool IsExpanded => isExpanded;
}