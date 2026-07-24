using UnityEngine;

public class ContentHeightLimiter : MonoBehaviour
{
    public RectTransform viewport;          // Viewport
    public RectTransform scorePanel;        // ScorePanel（父面板）
    public float itemHeight = 28f;          // 每个 Item 的高度
    public float maxHeight = 200f;          // 最大高度
    public float paddingTop = 30f;          // 标题占用的高度
    
    private RectTransform rect;
    private int lastChildCount = -1;         // 记录上次子物体数量
    
    void Start()
    {
        rect = GetComponent<RectTransform>();
    }
    
    void Update()
    {
        if (rect == null) return;
        
        // 获取当前子物体数量
        int childCount = rect.childCount;
        
        // 如果子物体数量没有变化，不需要更新
        if (childCount == lastChildCount) return;
        lastChildCount = childCount;
        
        // 计算需要的高度
        float contentHeight = childCount * itemHeight + 10f;
        // 限制最大高度
        contentHeight = Mathf.Min(contentHeight, maxHeight);
        
        // 如果没有子物体，设置最小高度
        if (childCount == 0)
        {
            contentHeight = 20f;
        }
        
        // 更新 Content 高度
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        
        // 更新 ScorePanel 高度
        if (scorePanel != null)
        {
            float panelHeight = contentHeight + paddingTop + 10f;
            // 限制最大高度
            panelHeight = Mathf.Min(panelHeight, maxHeight + paddingTop + 10f);
            scorePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
        }
    }
}