using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class LobbyPlayerListController : MonoBehaviour
{
    [Header("UI 引用")]
    public Transform contentParent;         // Content
    public GameObject playerItemPrefab;     // PlayerItemPrefab
    public GameObject playerListContainer;  // 包裹 Viewport 的 Panel
    public Button toggleBtn;                // 折叠按钮
    public RectTransform viewport;          // Viewport
    public RectTransform panelRect;         // PlayerListScrollView（外层）
    
    [Header("高度参数")]
    public float itemHeight = 28f;
    public float maxHeight = 300f;
    public float minHeight = 40f;
    public float paddingTop = 30f;
    public float paddingBottom = 10f;
    public float collapsedHeight = 40f;
    
    private List<GameObject> playerItems = new List<GameObject>();
    private bool isExpanded = true;
    private RectTransform contentRect;
    private float cachedContentHeight = 40f;
    private int lastChildCount = -1;
    
    void Start()
    {
        if (toggleBtn != null)
            toggleBtn.onClick.AddListener(TogglePlayerList);
        
        contentRect = contentParent as RectTransform;
        
        RefreshHeight();
        
        Debug.Log($"✅ LobbyPlayerListController 初始化完成");
    }
    
    void Update()
    {
        if (!isExpanded) return;
        if (contentRect == null) return;
        
        int childCount = contentRect.childCount;
        if (childCount == lastChildCount) return;
        lastChildCount = childCount;
        
        UpdateHeight();
    }
    
    void TogglePlayerList()
    {
        isExpanded = !isExpanded;
        
        if (isExpanded)
        {
            ExpandPlayerList();
        }
        else
        {
            CollapsePlayerList();
        }
        
        TextMeshProUGUI btnText = toggleBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            btnText.text = isExpanded ? "▼" : "▶";
        }
        
        Debug.Log($"📊 玩家列表 {(isExpanded ? "展开" : "折叠")}");
    }
    
    void ExpandPlayerList()
    {
        // ===== 先显示容器 =====
        if (playerListContainer != null)
            playerListContainer.SetActive(true);
        
        // ===== 延迟一帧设置高度 =====
        StartCoroutine(DelayedExpand());
    }
    
    System.Collections.IEnumerator DelayedExpand()
    {
        yield return null; // 等待一帧
        
        int childCount = contentRect != null ? contentRect.childCount : 0;
        float contentHeight = CalculateHeight(childCount);
        cachedContentHeight = contentHeight;
        
        SetAllHeights(contentHeight);
        
        Debug.Log($"📏 展开高度: {contentHeight} (玩家数: {childCount})");
    }
    
    void CollapsePlayerList()
    {
        if (contentRect != null)
        {
            cachedContentHeight = contentRect.rect.height;
            if (cachedContentHeight < minHeight) cachedContentHeight = minHeight;
        }
        
        SetAllHeights(0f);
        
        if (playerListContainer != null)
            playerListContainer.SetActive(false);
        
        Debug.Log($"📏 折叠高度: 0 (缓存: {cachedContentHeight})");
    }
    
    float CalculateHeight(int playerCount)
    {
        float contentHeight;
        if (playerCount > 0)
        {
            contentHeight = playerCount * itemHeight + paddingBottom;
        }
        else
        {
            contentHeight = minHeight;
        }
        return Mathf.Clamp(contentHeight, minHeight, maxHeight);
    }
    
    void UpdateHeight()
    {
        if (contentRect == null) return;
        
        int childCount = contentRect.childCount;
        float contentHeight = CalculateHeight(childCount);
        cachedContentHeight = contentHeight;
        
        SetAllHeights(contentHeight);
        
        Debug.Log($"📏 Content 高度已更新: {contentHeight} (玩家数: {childCount})");
    }
    
    void SetAllHeights(float height)
    {
        if (contentRect != null)
        {
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        
        if (viewport != null)
        {
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        
        if (panelRect != null)
        {
            float panelHeight = height > 0 ? height + paddingTop + paddingBottom : collapsedHeight;
            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
        }
    }
    
    public void RefreshHeight()
    {
        if (!isExpanded)
        {
            SetAllHeights(0f);
            if (playerListContainer != null)
                playerListContainer.SetActive(false);
            return;
        }
        
        lastChildCount = -1;
        UpdateHeight();
    }
    
    public void UpdatePlayerList(List<IPlayerStateReadonly> players)
    {
        foreach (var item in playerItems)
        {
            if (item != null) Destroy(item);
        }
        playerItems.Clear();
        
        if (contentParent != null)
        {
            for (int i = contentParent.childCount - 1; i >= 0; i--)
            {
                Transform child = contentParent.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }
        }
        
        if (players == null || players.Count == 0)
        {
            RefreshHeight();
            return;
        }
        
        foreach (var player in players)
        {
            if (player == null) continue;
            
            GameObject item = Instantiate(playerItemPrefab, contentParent);
            
            TextMeshProUGUI nameText = item.transform.Find("PlayerNameText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI roleText = item.transform.Find("RoleNameText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI readyText = item.transform.Find("ReadyText")?.GetComponent<TextMeshProUGUI>();
            
            if (nameText != null)
                nameText.text = player.PlayerName;
            
            if (roleText != null)
            {
                roleText.text = GetRoleDisplayName(player.Role);
                roleText.color = GetRoleColor(player.Role);
            }
            
            if (readyText != null)
                readyText.text = "";
            
            playerItems.Add(item);
        }
        
        RefreshHeight();
        Debug.Log($"📊 玩家列表已更新，共 {playerItems.Count} 名玩家");
    }
    
    string GetRoleDisplayName(PlayerRole role)
    {
        switch (role)
        {
            case PlayerRole.Hider: return "🟢 躲藏者";
            case PlayerRole.Seeker: return "🔴 抓捕者";
            default: return "❓ 未选择";
        }
    }
    
    Color GetRoleColor(PlayerRole role)
    {
        switch (role)
        {
            case PlayerRole.Hider: return new Color(0.2f, 0.8f, 0.2f);
            case PlayerRole.Seeker: return new Color(0.9f, 0.3f, 0.2f);
            default: return Color.gray;
        }
    }
}