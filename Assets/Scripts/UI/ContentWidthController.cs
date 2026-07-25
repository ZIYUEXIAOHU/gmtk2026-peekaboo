using UnityEngine;
using UnityEngine.UI;

public class ContentWidthController : MonoBehaviour
{
    [Header("面板引用")]
    public RectTransform winPanel;
    public RectTransform lostPanel;
    
    [Header("设置")]
    public float spacing = 5f;          // Win 和 Lost 间距
    public float minWidth = 100f;       // 最小宽度
    
    private RectTransform contentRect;
    private ScrollRect scrollRect;
    private Scrollbar scrollbar;
    
    void Start()
    {
        contentRect = GetComponent<RectTransform>();
        scrollRect = GetComponentInParent<ScrollRect>();
        if (scrollRect != null)
            scrollbar = scrollRect.horizontalScrollbar;
        
        UpdateContentWidth();
    }
    
    void Update()
    {
        // 每帧检查宽度是否变化
        if (winPanel == null || lostPanel == null) return;
        
        float winWidth = winPanel.rect.width;
        float lostWidth = lostPanel.rect.width;
        float totalWidth = winWidth + lostWidth + spacing;
        
        // 如果当前 Content 宽度不等于总宽度，更新
        if (Mathf.Abs(contentRect.rect.width - totalWidth) > 0.1f)
        {
            UpdateContentWidth();
        }
    }
    
    void UpdateContentWidth()
    {
        if (contentRect == null) return;
        if (winPanel == null || lostPanel == null) return;
        
        // ===== 计算总宽度 =====
        float winWidth = winPanel.rect.width;
        float lostWidth = lostPanel.rect.width;
        float totalWidth = winWidth + lostWidth + spacing;
        
        if (totalWidth < minWidth) totalWidth = minWidth;
        
        // ===== 设置 Content 宽度 =====
        contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, totalWidth);
        contentRect.anchoredPosition = new Vector2(0, 0);
        
        // ===== 调试日志 =====
        Debug.Log($"📊 Win: {winWidth:F0}px, Lost: {lostWidth:F0}px, 总宽度: {totalWidth:F0}px");
        
        // ===== 强制刷新滚动条 =====
        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.horizontalNormalizedPosition = 0;
        }
    }
    
    /// <summary>
    /// 强制刷新
    /// </summary>
    public void Refresh()
    {
        UpdateContentWidth();
    }
}