using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PlayerListUIController : MonoBehaviour
{
    [Header("UI")]
    public Transform playerListParent;      // Content
    public GameObject playerItemPrefab;     // PlayerItemPrefab
    public GameObject playerListContainer;  // 包裹 Viewport + Scrollbar 的 Panel
    public Button toggleBtn;                // 折叠按钮
    
    [Header("高度控制")]
    public RectTransform panelRect;         // PlayerListPanel 的 RectTransform
    public float collapsedHeight = 40f;     // 折叠时的高度（只显示标题栏）
    
    private List<GameObject> playerItems = new List<GameObject>();
    private bool isExpanded = true;
    
    private RectTransform contentRect;
    private float cachedPanelHeight = 200f;
    private float cachedContentHeight = 20f;
    private ContentHeightLimiter heightLimiter;
    
    void Start()
    {
        if (toggleBtn != null)
            toggleBtn.onClick.AddListener(TogglePlayerList);
        
        // 获取 Content 的 RectTransform
        if (playerListParent != null)
        {
            contentRect = playerListParent as RectTransform;
            heightLimiter = playerListParent.GetComponent<ContentHeightLimiter>();
            if (contentRect != null)
            {
                cachedContentHeight = contentRect.rect.height;
                if (cachedContentHeight < 10f) cachedContentHeight = 20f;
            }
        }
        
        // 获取 PlayerListPanel 的 RectTransform
        if (panelRect == null && transform.parent != null)
        {
            panelRect = transform.parent.GetComponent<RectTransform>();
        }
        if (panelRect != null)
        {
            cachedPanelHeight = panelRect.rect.height;
            if (cachedPanelHeight < 10f) cachedPanelHeight = 200f;
        }
        
        Debug.Log($"📏 PlayerList 初始高度 - Content: {cachedContentHeight}, Panel: {cachedPanelHeight}");
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
    }
    
    void ExpandPlayerList()
    {
        // ===== 1. 启用 ContentHeightLimiter =====
        if (heightLimiter != null)
        {
            heightLimiter.enabled = true;
            Debug.Log("✅ PlayerList ContentHeightLimiter 已启用");
        }
        
        // ===== 2. 显示 PlayerListContainer（Viewport + Scrollbar） =====
        if (playerListContainer != null)
            playerListContainer.SetActive(true);
        
        // ===== 3. 恢复 Content 高度（不修改位置） =====
        if (contentRect != null)
        {
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cachedContentHeight);
            // 移除：contentRect.anchoredPosition = new Vector2(0, 0);
            Debug.Log($"📏 PlayerList Content 恢复高度: {cachedContentHeight}");
        }
        
        // ===== 4. 恢复 Panel 高度 =====
        if (panelRect != null)
        {
            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cachedPanelHeight);
            Debug.Log($"📏 PlayerList Panel 恢复高度: {cachedPanelHeight}");
        }
        
        // ===== 5. 刷新 ContentHeightLimiter =====
        if (heightLimiter != null)
        {
            heightLimiter.Refresh();
        }
        
        Debug.Log("📊 玩家列表已展开");
    }
    
    void CollapsePlayerList()
    {
        // ===== 1. 先禁用 ContentHeightLimiter =====
        if (heightLimiter != null)
        {
            heightLimiter.enabled = false;
            Debug.Log("⛔ PlayerList ContentHeightLimiter 已禁用");
        }
        
        // ===== 2. 缓存当前高度 =====
        if (contentRect != null)
        {
            cachedContentHeight = contentRect.rect.height;
            if (cachedContentHeight < 10f) cachedContentHeight = 20f;
        }
        if (panelRect != null)
        {
            cachedPanelHeight = panelRect.rect.height;
            if (cachedPanelHeight < 10f) cachedPanelHeight = 200f;
        }
        
        // ===== 3. Content 高度设为 collapsedHeight（不修改位置） =====
        if (contentRect != null)
        {
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, collapsedHeight);
            // 移除：contentRect.anchoredPosition = new Vector2(0, 0);
            Debug.Log($"📏 PlayerList Content 折叠高度: {collapsedHeight}");
        }
        
        // ===== 4. Panel 高度设为 collapsedHeight =====
        if (panelRect != null)
        {
            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, collapsedHeight);
            Debug.Log($"📏 PlayerList Panel 折叠高度: {collapsedHeight}");
        }
        
        // ===== 5. 隐藏 PlayerListContainer（Viewport + Scrollbar） =====
        if (playerListContainer != null)
            playerListContainer.SetActive(false);
        
        Debug.Log($"📊 玩家列表已折叠 (缓存 - Content: {cachedContentHeight}, Panel: {cachedPanelHeight})");
    }
    
    public void UpdatePlayerList(List<IPlayerStateReadonly> players)
    {
        ClearPlayerItems();
        
        Debug.Log($"📊 更新玩家列表，共 {players?.Count ?? 0} 名玩家");
        
        if (players != null && players.Count > 0)
        {
            foreach (var player in players)
            {
                if (player == null) continue;
                CreatePlayerItem(player);
            }
        }
        
        Debug.Log($"📊 玩家列表已更新，共 {playerItems.Count} 名玩家");
        
        // ===== 刷新高度 =====
        if (heightLimiter != null && isExpanded)
        {
            heightLimiter.Refresh();
            Debug.Log("📏 刷新玩家列表高度");
        }
    }
    
    void ClearPlayerItems()
    {
        foreach (var item in playerItems)
        {
            if (item != null)
                Destroy(item);
        }
        playerItems.Clear();
        
        if (playerListParent != null)
        {
            for (int i = playerListParent.childCount - 1; i >= 0; i--)
            {
                Transform child = playerListParent.GetChild(i);
                if (child != null)
                    Destroy(child.gameObject);
            }
        }
    }
    
    void CreatePlayerItem(IPlayerStateReadonly player)
    {
        if (playerItemPrefab == null || playerListParent == null) return;
        
        GameObject item = Instantiate(playerItemPrefab, playerListParent);
        
        TextMeshProUGUI nameText = item.transform.Find("PlayerNameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI roleText = item.transform.Find("RoleNameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI readyText = item.transform.Find("ReadyText")?.GetComponent<TextMeshProUGUI>();
        
        if (nameText != null)
            nameText.text = player.PlayerName;
        
        if (roleText != null)
        {
            string roleName = GetRoleDisplayName(player.Role);
            roleText.text = roleName;
            roleText.color = GetRoleColor(player.Role);
        }
        
        if (readyText != null)
        {
            readyText.text = "";
        }
        
        playerItems.Add(item);
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
    
    public void Show()
    {
        gameObject.SetActive(true);
    }
    
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}