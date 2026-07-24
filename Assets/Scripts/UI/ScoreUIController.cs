using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ScoreUIController : MonoBehaviour
{
    [Header("UI")]
    public Transform scoreListParent;      // Content
    public GameObject scoreItemPrefab;     // ScoreItemPrefab
    public GameObject scoreList;           // ScoreList (ScrollView)
    public Button toggleBtn;               // 折叠按钮
    
    [Header("高度控制")]
    public RectTransform scorePanelRect;   // ScorePanel 的 RectTransform
    public float collapsedHeight = 60f;    // 折叠时的高度
    
    private List<GameObject> scoreItems = new List<GameObject>();
    private bool isExpanded = true;
    
    private RectTransform contentRect;
    private float cachedPanelHeight = 200f;
    private float cachedContentHeight = 20f;
    private ContentHeightLimiter heightLimiter;
    
    void Start()
    {
        if (toggleBtn != null)
            toggleBtn.onClick.AddListener(ToggleScoreList);
        
        // 获取 Content 的 RectTransform
        if (scoreListParent != null)
        {
            contentRect = scoreListParent as RectTransform;
            heightLimiter = scoreListParent.GetComponent<ContentHeightLimiter>();
            if (contentRect != null)
            {
                cachedContentHeight = contentRect.rect.height;
                if (cachedContentHeight < 10f) cachedContentHeight = 20f;
            }
        }
        
        // 获取 ScorePanel 的 RectTransform
        if (scorePanelRect == null && transform.parent != null)
        {
            scorePanelRect = transform.parent.GetComponent<RectTransform>();
        }
        if (scorePanelRect != null)
        {
            cachedPanelHeight = scorePanelRect.rect.height;
            if (cachedPanelHeight < 10f) cachedPanelHeight = 200f;
        }
        
        Debug.Log($"📏 初始高度 - Content: {cachedContentHeight}, ScorePanel: {cachedPanelHeight}");
    }
    
    void ToggleScoreList()
    {
        isExpanded = !isExpanded;
        
        if (isExpanded)
        {
            ExpandScoreList();
        }
        else
        {
            CollapseScoreList();
        }
        
        TextMeshProUGUI btnText = toggleBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            btnText.text = isExpanded ? "▼" : "▶";
        }
    }
    
    void ExpandScoreList()
    {
        // ===== 1. 启用 ContentHeightLimiter =====
        if (heightLimiter != null)
        {
            heightLimiter.enabled = true;
            Debug.Log("✅ ContentHeightLimiter 已启用");
        }
        
        // ===== 2. 显示 ScoreList =====
        if (scoreList != null)
            scoreList.SetActive(true);
        
        // ===== 3. 恢复 Content 高度 =====
        if (contentRect != null)
        {
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cachedContentHeight);
            Debug.Log($"📏 Content 恢复高度: {cachedContentHeight}");
        }
        
        // ===== 4. 恢复 ScorePanel 高度 =====
        if (scorePanelRect != null)
        {
            scorePanelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cachedPanelHeight);
            Debug.Log($"📏 ScorePanel 恢复高度: {cachedPanelHeight}");
        }
        
        // ===== 5. 刷新 ContentHeightLimiter =====
        if (heightLimiter != null)
        {
            heightLimiter.Refresh();
        }
        
        Debug.Log("📊 得分列表已展开");
    }
    
    void CollapseScoreList()
    {
        // ===== 1. 先禁用 ContentHeightLimiter（防止它覆盖高度） =====
        if (heightLimiter != null)
        {
            heightLimiter.enabled = false;
            Debug.Log("⛔ ContentHeightLimiter 已禁用");
        }
        
        // ===== 2. 缓存当前高度（用于恢复） =====
        if (contentRect != null)
        {
            cachedContentHeight = contentRect.rect.height;
            if (cachedContentHeight < 10f) cachedContentHeight = 20f;
        }
        if (scorePanelRect != null)
        {
            cachedPanelHeight = scorePanelRect.rect.height;
            if (cachedPanelHeight < 10f) cachedPanelHeight = 200f;
        }
        
        // ===== 3. Content 高度设为 60 =====
        if (contentRect != null)
        {
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, collapsedHeight);
            contentRect.anchoredPosition = new Vector2(contentRect.anchoredPosition.x, 0);
            Debug.Log($"📏 Content 折叠高度: {collapsedHeight}");
        }
        
        // ===== 4. ScorePanel 高度设为 60 =====
        if (scorePanelRect != null)
        {
            scorePanelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, collapsedHeight);
            Debug.Log($"📏 ScorePanel 折叠高度: {collapsedHeight}");
        }
        
        // ===== 5. 隐藏 ScoreList =====
        if (scoreList != null)
            scoreList.SetActive(false);
        
        Debug.Log($"📊 得分列表已折叠 (缓存 - Content: {cachedContentHeight}, ScorePanel: {cachedPanelHeight})");
    }
    
    public void UpdateScoreList()
    {
        ClearScoreItems();
        
        List<IPlayerStateReadonly> players = new List<IPlayerStateReadonly>();
        if (GameContract.IsBound && GameContract.State != null)
        {
            foreach (var p in GameContract.State.Players)
            {
                if (p != null)
                    players.Add(p);
            }
        }
        
        Debug.Log($"📊 从契约读取到 {players.Count} 名玩家");
        
        if (players != null && players.Count > 0)
        {
            foreach (var player in players)
            {
                if (player == null) continue;
                CreateScoreItem(player);
            }
        }
        
        Debug.Log($"📊 得分列表已更新，共 {scoreItems.Count} 名玩家");
    }
    
    void ClearScoreItems()
    {
        foreach (var item in scoreItems)
        {
            if (item != null)
                Destroy(item);
        }
        scoreItems.Clear();
        
        if (scoreListParent != null)
        {
            for (int i = scoreListParent.childCount - 1; i >= 0; i--)
            {
                Transform child = scoreListParent.GetChild(i);
                if (child != null)
                    Destroy(child.gameObject);
            }
        }
    }
    
    void CreateScoreItem(IPlayerStateReadonly player)
    {
        if (scoreItemPrefab == null || scoreListParent == null) return;
        
        GameObject item = Instantiate(scoreItemPrefab, scoreListParent);
        
        TextMeshProUGUI nameText = item.transform.Find("PlayerNameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI roleText = item.transform.Find("RoleNameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI scoreText = item.transform.Find("ScoreText")?.GetComponent<TextMeshProUGUI>();
        
        if (nameText != null)
            nameText.text = player.PlayerName;
        
        Color roleColor = GetRoleColor(player.Role);
        string roleName = GetRoleDisplayName(player.Role);
        
        if (roleText != null)
        {
            roleText.text = roleName;
            roleText.color = roleColor;
        }
        
        if (scoreText != null)
            scoreText.text = "0分";
        
        scoreItems.Add(item);
    }
    
    string GetRoleDisplayName(PlayerRole role)
    {
        switch (role)
        {
            case PlayerRole.Hider: return "躲藏者";
            case PlayerRole.Seeker: return "抓捕者";
            default: return "未选择";
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