using UnityEngine;

public class ResultListController : MonoBehaviour
{
    [Header("面板引用")]
    public RectTransform winPanel;      // Win 面板
    public RectTransform lostPanel;     // Lost 面板
    
    [Header("设置")]
    public float itemWidth = 155f;      // 每个条目宽度
    public float spacing = 5f;          // 间距
    public float minWidth = 20f;        // 最小宽度（防止为 0 看不见）
    
    [Header("自动绑定")]
    public bool autoFindPanels = true;
    public string winPanelName = "Results_Win";
    public string lostPanelName = "Results_Lost";
    public string containerName = "Panel";
    
    private RectTransform contentRect;
    private int lastWinCount = -1;
    private int lastLostCount = -1;
    
    void Start()
    {
        contentRect = GetComponent<RectTransform>();
        
        // ===== 自动查找面板 =====
        if (autoFindPanels)
        {
            if (winPanel == null)
            {
                Transform found = transform.Find(winPanelName);
                if (found == null)
                {
                    GameObject go = GameObject.Find(winPanelName);
                    if (go != null) found = go.transform;
                }
                if (found != null) winPanel = found as RectTransform;
            }
            
            if (lostPanel == null)
            {
                Transform found = transform.Find(lostPanelName);
                if (found == null)
                {
                    GameObject go = GameObject.Find(lostPanelName);
                    if (go != null) found = go.transform;
                }
                if (found != null) lostPanel = found as RectTransform;
            }
            
            // ===== 如果还是找不到，自动创建 =====
            if (winPanel == null)
            {
                GameObject newPanel = new GameObject(winPanelName, typeof(RectTransform));
                newPanel.transform.SetParent(transform);
                winPanel = newPanel.GetComponent<RectTransform>();
                winPanel.sizeDelta = new Vector2(minWidth, 100);
                EnsureContainer(winPanel);
            }
            
            if (lostPanel == null)
            {
                GameObject newPanel = new GameObject(lostPanelName, typeof(RectTransform));
                newPanel.transform.SetParent(transform);
                lostPanel = newPanel.GetComponent<RectTransform>();
                lostPanel.sizeDelta = new Vector2(minWidth, 100);
                EnsureContainer(lostPanel);
            }
        }
        
        // ===== 确保容器存在 =====
        if (winPanel != null) EnsureContainer(winPanel);
        if (lostPanel != null) EnsureContainer(lostPanel);
        
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
    
    void EnsureContainer(RectTransform parent)
    {
        if (parent == null) return;
        Transform child = parent.Find(containerName);
        if (child == null)
        {
            GameObject container = new GameObject(containerName, typeof(RectTransform));
            container.transform.SetParent(parent);
            RectTransform rect = container.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }
    }
    
    int GetChildCount(RectTransform panel)
    {
        if (panel == null) return 0;
        Transform child = panel.Find(containerName);
        if (child != null) return child.childCount;
        return panel.childCount;
    }
    
    void UpdateLayout()
    {
        if (winPanel == null || lostPanel == null) return;
        
        int winCount = GetChildCount(winPanel);
        int lostCount = GetChildCount(lostPanel);
        
        float winWidth = winCount > 0 ? winCount * (itemWidth + spacing) : minWidth;
        float lostWidth = lostCount > 0 ? lostCount * (itemWidth + spacing) : minWidth;
        
        winPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, winWidth);
        winPanel.anchoredPosition = new Vector2(0, 0);
        
        lostPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, lostWidth);
        lostPanel.anchoredPosition = new Vector2(winWidth + spacing, 0);
        
        SetChildPanelWidth(winPanel, winWidth);
        SetChildPanelWidth(lostPanel, lostWidth);
        
        Debug.Log($"📊 Win: {winCount} → {winWidth:F0}px, Lost: {lostCount} → {lostWidth:F0}px");
    }
    
    void SetChildPanelWidth(RectTransform parent, float width)
    {
        Transform child = parent.Find(containerName);
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