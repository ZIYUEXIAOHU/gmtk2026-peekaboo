using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
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
    
    struct ScoreRow
    {
        public GameObject root;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI roleText;
        public TextMeshProUGUI scoreText;
        public int lastScore;
    }

    private readonly Dictionary<uint, ScoreRow> rowsByNetId = new Dictionary<uint, ScoreRow>();
    private readonly List<uint> rowOrder = new List<uint>();
    private bool isExpanded = true;
    
    private RectTransform contentRect;
    private float cachedPanelHeight = 200f;
    private float cachedContentHeight = 20f;
    private ContentHeightLimiter heightLimiter;
    private bool isSubscribed;
    private float nextStructureRefreshTime;
    private float nextScorePollTime;
    
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
        
        // ===== 先展开初始化，再折叠隐藏（防止未初始化） =====
        ExpandScoreList();
        CollapseScoreList();
        
        // 确保按钮文字正确
        if (toggleBtn != null)
        {
            TextMeshProUGUI btnText = toggleBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null)
                btnText.text = "▶";
        }
        isExpanded = false;

        SubscribeEvents();
        RebuildScoreList();
    }

    void OnDestroy()
    {
        UnsubscribeEvents();
    }

    void SubscribeEvents()
    {
        if (isSubscribed) return;
        if (!GameContract.IsBound)
        {
            StartCoroutine(RetrySubscribeEvents());
            return;
        }

        try
        {
            GameContract.Events.OnPhaseChanged += OnPhaseChanged;
            GameContract.Events.OnCaptured += OnCaptured;
            GameContract.Events.OnInvestigated += OnInvestigated;
            GameContract.Events.OnRoleSlotsChanged += OnRoleSlotsChanged;
            RoomPlayer.ScoreChanged += OnRoomPlayerScoreChanged;
            isSubscribed = true;
            RebuildScoreList();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"ScoreUI 订阅事件失败：{e.Message}");
        }
    }

    IEnumerator RetrySubscribeEvents()
    {
        float waited = 0f;
        while (!GameContract.IsBound && waited < 5f)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
        }
        if (GameContract.IsBound)
            SubscribeEvents();
    }

    void UnsubscribeEvents()
    {
        RoomPlayer.ScoreChanged -= OnRoomPlayerScoreChanged;
        if (!isSubscribed || !GameContract.IsBound) return;
        try
        {
            GameContract.Events.OnPhaseChanged -= OnPhaseChanged;
            GameContract.Events.OnCaptured -= OnCaptured;
            GameContract.Events.OnInvestigated -= OnInvestigated;
            GameContract.Events.OnRoleSlotsChanged -= OnRoleSlotsChanged;
            isSubscribed = false;
        }
        catch { }
    }

    void Update()
    {
        if (!GameContract.IsBound || GameContract.State == null) return;

        // Playing：高频就地刷分（覆盖 SyncVar 晚于 Rpc / Host hook 差异）
        if (GameContract.State.Phase == GamePhase.Playing
            && Time.unscaledTime >= nextScorePollTime)
        {
            nextScorePollTime = Time.unscaledTime + 0.25f;
            RefreshAllScoreTexts();
        }

        // 名单结构偶发变化时兜底重建
        if (Time.unscaledTime >= nextStructureRefreshTime)
        {
            nextStructureRefreshTime = Time.unscaledTime + 2f;
            if (NeedsStructureRebuild())
                RebuildScoreList();
        }
    }

    void OnPhaseChanged(GamePhase phase, float duration) => RebuildScoreList();
    void OnCaptured(CaptureInfo info) => RebuildScoreList();
    void OnInvestigated(InvestigateInfo info)
    {
        // 查人加分 / 查放置物扣分：SyncVar 可能晚于 Rpc，多帧兜底刷新
        RefreshAllScoreTexts();
        StartCoroutine(RefreshScoresNextFrames());
    }
    void OnRoleSlotsChanged(RoleSlots slots) => RebuildScoreList();

    void OnRoomPlayerScoreChanged(RoomPlayer player, int newScore)
    {
        if (player == null) return;
        ApplyScoreText(player.netId, newScore);
    }

    IEnumerator RefreshScoresNextFrames()
    {
        yield return null;
        RefreshAllScoreTexts();
        yield return null;
        RefreshAllScoreTexts();
    }
    
    void ToggleScoreList()
    {
        isExpanded = !isExpanded;
        
        if (isExpanded)
        {
            RebuildScoreList();
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
        // ===== 1. 先显示 ScorePanel =====
        if (scorePanelRect != null)
            scorePanelRect.gameObject.SetActive(true);
        
        // ===== 2. 启用 ContentHeightLimiter =====
        if (heightLimiter != null)
        {
            heightLimiter.enabled = true;
            Debug.Log("✅ ContentHeightLimiter 已启用");
        }
        
        // ===== 3. 显示 ScoreList =====
        if (scoreList != null)
            scoreList.SetActive(true);
        
        // ===== 4. 恢复 Content 高度 =====
        if (contentRect != null)
        {
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cachedContentHeight);
            contentRect.anchoredPosition = new Vector2(0, 0);
            Debug.Log($"📏 Content 恢复高度: {cachedContentHeight}");
        }
        
        // ===== 5. 恢复 ScorePanel 高度 =====
        if (scorePanelRect != null)
        {
            scorePanelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cachedPanelHeight);
            Debug.Log($"📏 ScorePanel 恢复高度: {cachedPanelHeight}");
        }
        
        // ===== 6. 刷新 ContentHeightLimiter =====
        if (heightLimiter != null)
        {
            heightLimiter.Refresh();
        }
        
        Debug.Log("📊 得分列表已展开");
    }
    
    void CollapseScoreList()
    {
        // ===== 1. 先禁用 ContentHeightLimiter =====
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
        
        // ===== 3. 隐藏整个 ScorePanel =====
        if (scorePanelRect != null)
            scorePanelRect.gameObject.SetActive(false);
        
        Debug.Log($"📊 得分列表已折叠，ScorePanel 已隐藏");
    }

    /// <summary>兼容旧调用：完整重建名单。</summary>
    public void UpdateScoreList() => RebuildScoreList();

    bool NeedsStructureRebuild()
    {
        if (!GameContract.IsBound || GameContract.State == null) return false;

        var seen = new HashSet<uint>();
        int count = 0;
        foreach (var p in GameContract.State.Players)
        {
            if (p == null || p.Role == PlayerRole.None) continue;
            seen.Add(p.NetId);
            count++;
        }

        if (count != rowOrder.Count) return true;
        for (int i = 0; i < rowOrder.Count; i++)
        {
            if (!seen.Contains(rowOrder[i])) return true;
        }
        return false;
    }
    
    public void RebuildScoreList()
    {
        ClearScoreItems();
        
        List<IPlayerStateReadonly> players = new List<IPlayerStateReadonly>();
        if (GameContract.IsBound && GameContract.State != null)
        {
            foreach (var p in GameContract.State.Players)
            {
                if (p == null || p.Role == PlayerRole.None) continue;
                players.Add(p);
            }
        }

        // 分数高的在上，同分按名字
        players.Sort((a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.PlayerName, b.PlayerName);
        });
        
        if (players.Count > 0)
        {
            foreach (var player in players)
                CreateScoreItem(player);
        }
        
        Debug.Log($"📊 得分列表已更新，共 {rowsByNetId.Count} 名玩家");
        
        // ===== 刷新高度 =====
        if (heightLimiter != null && isExpanded)
        {
            heightLimiter.Refresh();
            Debug.Log("📏 刷新得分列表高度");
        }
    }

    void RefreshAllScoreTexts()
    {
        if (!GameContract.IsBound || GameContract.State == null) return;
        foreach (var p in GameContract.State.Players)
        {
            if (p == null) continue;
            ApplyScoreText(p.NetId, p.Score);
        }
    }

    void ApplyScoreText(uint netId, int score)
    {
        if (!rowsByNetId.TryGetValue(netId, out ScoreRow row)) return;
        if (row.scoreText == null) return;
        if (row.lastScore == score && row.scoreText.text.StartsWith(score.ToString())) return;

        row.scoreText.text = $"{score} pts";
        row.lastScore = score;
        rowsByNetId[netId] = row;
    }
    
    void ClearScoreItems()
    {
        foreach (var kv in rowsByNetId)
        {
            if (kv.Value.root != null)
                Destroy(kv.Value.root);
        }
        rowsByNetId.Clear();
        rowOrder.Clear();
        
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
            scoreText.text = $"{player.Score} pts";

        rowsByNetId[player.NetId] = new ScoreRow
        {
            root = item,
            nameText = nameText,
            roleText = roleText,
            scoreText = scoreText,
            lastScore = player.Score,
        };
        rowOrder.Add(player.NetId);
    }
    
    string GetRoleDisplayName(PlayerRole role)
    {
        switch (role)
        {
            case PlayerRole.Hider: return "Hider";
            case PlayerRole.Seeker: return "Hunter";
            default: return "None";
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
