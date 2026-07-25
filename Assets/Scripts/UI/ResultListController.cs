using UnityEngine;

public class ResultListController : MonoBehaviour
{
    [Header("面板引用")]
    public RectTransform winPanel;      // Win 面板
    public RectTransform lostPanel;     // Lost 面板
    
    [Header("设置")]
    public float itemWidth = 155f;      // 每个条目宽度
    public float spacing = 5f;          // 间距
    
    private RectTransform contentRect;
    private int lastWinCount = -1;
    private int lastLostCount = -1;
    
    void Start()
    {
        contentRect = GetComponent<RectTransform>();
        UpdateLayout();
    }
    
    void Update()
    {
        if (winPanel == null || lostPanel == null) return;
        
        int winCount = GetChildCount(winPanel);
        int lostCount = GetChildCount(lostPanel);
        
        if (winCount == lastWinCount && lostCount == lastLostCount) return;
        
        lastWinCount = winCount;
        lastLostCount = lostCount;
        
        UpdateLayout();
    }
    
    int GetChildCount(RectTransform panel)
    {
        Transform child = panel.Find("Panel");
        if (child != null)
            return child.childCount;
        return panel.childCount;
    }
    
    void UpdateLayout()
    {
        if (winPanel == null || lostPanel == null) return;
        
        int winCount = GetChildCount(winPanel);
        int lostCount = GetChildCount(lostPanel);
        
        // ===== Win 宽度按条目数量计算 =====
        float winWidth = winCount * (itemWidth + spacing);
        
        // ===== Lost 宽度按条目数量计算 =====
        float lostWidth = lostCount * (itemWidth + spacing);
        
        // ===== 设置 Win 面板（左） =====
        winPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, winWidth);
        winPanel.anchoredPosition = new Vector2(0, 0);
        
        // ===== 设置 Lost 面板（右，顺延） =====
        lostPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, lostWidth);
        lostPanel.anchoredPosition = new Vector2(winWidth + spacing, 0);
        
        SetChildPanelWidth(winPanel, winWidth);
        SetChildPanelWidth(lostPanel, lostWidth);
        
        Debug.Log($"📊 Win: {winCount} → {winWidth:F0}px, Lost: {lostCount} → {lostWidth:F0}px");
    }
    
    void SetChildPanelWidth(RectTransform parent, float width)
    {
        Transform child = parent.Find("Panel");
        if (child != null)
        {
            RectTransform childRect = child as RectTransform;
            if (childRect != null)
            {
                childRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                childRect.anchoredPosition = new Vector2(0, 0);
            }
        }
    }
    
    public void Refresh()
    {
        lastWinCount = -1;
        lastLostCount = -1;
        UpdateLayout();
    }
}