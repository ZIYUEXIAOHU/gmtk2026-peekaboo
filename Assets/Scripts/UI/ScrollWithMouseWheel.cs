using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ScrollWithMouseWheel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("滚动设置")]
    public ScrollRect scrollRect;           // 父级 ScrollRect
    public float scrollSpeed = 20f;         // 滚动速度
    
    private bool isHovering = false;
    
    void Start()
    {
        if (scrollRect == null)
            scrollRect = GetComponentInParent<ScrollRect>();
    }
    
    void Update()
    {
        if (scrollRect == null) return;
        if (!isHovering) return;
        
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        
        if (scroll == 0f) return;
        
        // ===== 鼠标滚轮控制左右滚动 =====
        scrollRect.horizontalNormalizedPosition -= scroll * scrollSpeed * Time.deltaTime;
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
    }
}