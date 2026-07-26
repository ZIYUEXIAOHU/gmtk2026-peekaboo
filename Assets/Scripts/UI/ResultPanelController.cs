using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mirror;

public class ResultPanelController : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI gameNumberText;   // GAME #07
    public TextMeshProUGUI titleTeam;        // HIDER / HUNTER
    public TextMeshProUGUI titleResult;      // WIN / FAIL
    public TextMeshProUGUI capturedText;     // CAPTURED: 3/4
    public TextMeshProUGUI brokenText;       // BROKEN: 12 (3/9)
    public TextMeshProUGUI durationText;     // TOTAL TIME: 5:00
    public Transform scoreListParent;        // Content
    public GameObject scoreItemPrefab;
    
    [Header("按钮")]
    public Button mainMenuBtn;
    public Button lobbyBtn;
    
    [Header("游戏UI")]
    public GameObject gameUI;
    
    [Header("状态图标")]
    public Sprite aliveSprite;
    public Sprite lostSprite;
    
    [Header("结算列表（Win/Lost 面板）")]
    public ResultListController resultListController;
    
    private List<GameObject> scoreItems = new List<GameObject>();
    private int gameNumber = 0;
    private bool eventsSubscribed;
    private bool resultShown;
    private IGameEvents boundEvents;
    private CanvasGroup canvasGroup;
    
    void Awake()
    {
        EnsureCanvasGroup();
        SetPanelVisible(false);
        
        // ===== 自动查找 ResultListController =====
        if (resultListController == null)
        {
            resultListController = GetComponentInChildren<ResultListController>(true);
            if (resultListController != null)
            {
                Debug.Log("✅ 自动找到 ResultListController");
            }
            else
            {
                Debug.LogWarning("⚠️ 未找到 ResultListController，请手动绑定");
            }
        }
        
        // ===== 初始隐藏 ResultListController =====
        if (resultListController != null)
        {
            resultListController.gameObject.SetActive(false);
        }
    }
    
    void Start()
    {
        if (mainMenuBtn != null)
            mainMenuBtn.onClick.AddListener(OnMainMenuClicked);
        
        if (lobbyBtn != null)
            lobbyBtn.onClick.AddListener(OnLobbyClicked);
        
        TrySubscribeEvents();
        TryShowFromState();
    }
    
    void Update()
    {
        if (!eventsSubscribed)
            TrySubscribeEvents();
        
        if (!resultShown)
            TryShowFromState();
    }
    
    void TrySubscribeEvents()
    {
        if (eventsSubscribed && ReferenceEquals(boundEvents, GameContract.Events))
            return;
        
        if (!GameContract.IsBound || GameContract.Events == null)
            return;
        
        UnsubscribeEvents();
        boundEvents = GameContract.Events;
        boundEvents.OnGameEnded += OnGameEnded;
        eventsSubscribed = true;
        Debug.Log("✅ ResultPanelController 订阅 OnGameEnded 成功");
        
        TryShowFromState();
    }
    
    void UnsubscribeEvents()
    {
        if (!eventsSubscribed) return;
        
        if (boundEvents != null)
            boundEvents.OnGameEnded -= OnGameEnded;
        else if (GameContract.IsBound && GameContract.Events != null)
            GameContract.Events.OnGameEnded -= OnGameEnded;
        
        boundEvents = null;
        eventsSubscribed = false;
    }
    
    void OnDestroy()
    {
        UnsubscribeEvents();
    }
    
    void TryShowFromState()
    {
        if (resultShown) return;
        if (!GameContract.IsBound || GameContract.State == null) return;
        if (GameContract.State.Phase != GamePhase.Ended) return;
        
        OnGameEnded(GameContract.State.Result);
    }
    
    void OnGameEnded(MatchResult result)
    {
        if (resultShown) return;
        
        Debug.Log($"🏆 收到结算事件: {result.result}, 存活: {result.survivors}, 用时: {result.duration}s");
        
        List<IPlayerStateReadonly> players = new List<IPlayerStateReadonly>();
        if (GameContract.State != null)
        {
            foreach (var p in GameContract.State.Players)
            {
                if (p != null)
                    players.Add(p);
            }
        }
        
        ShowResult(result, players);
    }
    
    public void ShowResult(MatchResult result, List<IPlayerStateReadonly> players)
    {
        resultShown = true;
        SetPanelVisible(true);
        
        // ===== 显示 ResultListController =====
        if (resultListController != null)
        {
            resultListController.gameObject.SetActive(true);
            resultListController.Refresh();
            Debug.Log("✅ ResultListController 已显示并刷新");
        }
        else
        {
            resultListController = GetComponentInChildren<ResultListController>(true);
            if (resultListController != null)
            {
                resultListController.gameObject.SetActive(true);
                resultListController.Refresh();
                Debug.Log("✅ 重新找到并显示 ResultListController");
            }
        }
        
        if (gameUI != null)
            gameUI.SetActive(false);
        
        // ===== GAME #07 =====
        if (gameNumberText != null)
        {
            gameNumber++;
            gameNumberText.text = $"GAME #{gameNumber:D2}";
        }
        
        // ===== CAPTURED: 3/4 =====
        if (capturedText != null)
        {
            int captured = 0;
            int totalHiders = 0;
            foreach (var p in players)
            {
                if (p == null || p.Role != PlayerRole.Hider) continue;
                totalHiders++;
                if (p.HiderState == HiderState.Captured)
                    captured++;
            }
            capturedText.text = $"CAPTURED: {captured}/{totalHiders}";
        }
        
        // ===== BROKEN: 12 (3/9) =====
        if (brokenText != null)
        {
            int brokenItems = 0;
            int brokenRounds = 0;
            brokenText.text = $"BROKEN: {brokenItems} ({brokenRounds}/0)";
        }
        
        // ===== TOTAL TIME: 5:00 =====
        if (durationText != null)
        {
            int minutes = Mathf.FloorToInt(result.duration / 60f);
            int seconds = Mathf.FloorToInt(result.duration % 60f);
            durationText.text = $"TOTAL TIME: {minutes:D2}:{seconds:D2}";
        }
        
        // ===== 阵营标题 =====
        if (titleTeam != null)
        {
            if (result.result == GameResult.HidersWin)
            {
                titleTeam.text = "HIDER";
                titleTeam.color = new Color(0.2f, 0.8f, 0.2f);
            }
            else if (result.result == GameResult.SeekersWin)
            {
                titleTeam.text = "HUNTER";
                titleTeam.color = new Color(0.9f, 0.3f, 0.2f);
            }
            else
            {
                titleTeam.text = "DRAW";
                titleTeam.color = Color.yellow;
            }
        }
        
        // ===== 输赢标题 =====
        if (titleResult != null)
        {
            if (result.result == GameResult.HidersWin)
            {
                titleResult.text = "WIN";
                titleResult.color = new Color(0.2f, 0.8f, 0.2f);
            }
            else if (result.result == GameResult.SeekersWin)
            {
                titleResult.text = "WIN";
                titleResult.color = new Color(0.9f, 0.3f, 0.2f);
            }
            else
            {
                titleResult.text = "DRAW";
                titleResult.color = Color.yellow;
            }
        }
        
        UpdateScoreList(players, result);
    }
    
    void UpdateScoreList(List<IPlayerStateReadonly> players, MatchResult result)
    {
        // ===== 清理旧的 ScoreItem =====
        foreach (var item in scoreItems)
        {
            Destroy(item);
        }
        scoreItems.Clear();
        
        if (scoreListParent != null)
        {
            for (int i = scoreListParent.childCount - 1; i >= 0; i--)
            {
                Transform child = scoreListParent.GetChild(i);
                if (child != null)
                {
                    // ===== 保护 Win 和 Lost 面板，不被删除 =====
                    if (child.name == "Win" || child.name == "Lost")
                    {
                        Debug.Log($"🛡️ 保护面板: {child.name}");
                        continue;
                    }
                    
                    // ===== 保护 ResultListController =====
                    if (child.GetComponent<ResultListController>() != null)
                    {
                        Debug.Log($"🛡️ 保护 ResultListController");
                        continue;
                    }
                    
                    Destroy(child.gameObject);
                }
            }
        }
        
        if (players == null || players.Count == 0) return;
        
        foreach (var player in players)
        {
            if (player == null) continue;
            bool won = DidPlayerWin(player, result);
            CreateScoreItem(player, won);
        }
    }
    
    static bool DidPlayerWin(IPlayerStateReadonly player, MatchResult result)
    {
        if (result.result == GameResult.HidersWin)
            return player.Role == PlayerRole.Hider && player.HiderState != HiderState.Captured;
        if (result.result == GameResult.SeekersWin)
            return player.Role == PlayerRole.Seeker;
        return false;
    }
    
    void CreateScoreItem(IPlayerStateReadonly player, bool won)
    {
        if (scoreItemPrefab == null) return;
        
        // ===== 根据胜负选择父物体 =====
        Transform parent = scoreListParent;
        
        if (resultListController != null)
        {
            if (won && resultListController.winPanel != null)
            {
                // ===== 找到 Win 面板下的 Panel 子物体 =====
                Transform container = resultListController.winPanel.Find("Panel");
                if (container != null)
                {
                    parent = container;
                    Debug.Log($"✅ WIN 玩家 {player.PlayerName} → 生成到 Win/Panel");
                }
                else
                {
                    parent = resultListController.winPanel;
                    Debug.Log($"✅ WIN 玩家 {player.PlayerName} → 生成到 Win");
                }
            }
            else if (!won && resultListController.lostPanel != null)
            {
                // ===== 找到 Lost 面板下的 Panel 子物体 =====
                Transform container = resultListController.lostPanel.Find("Panel");
                if (container != null)
                {
                    parent = container;
                    Debug.Log($"❌ LOST 玩家 {player.PlayerName} → 生成到 Lost/Panel");
                }
                else
                {
                    parent = resultListController.lostPanel;
                    Debug.Log($"❌ LOST 玩家 {player.PlayerName} → 生成到 Lost");
                }
            }
        }
        
        GameObject item = Instantiate(scoreItemPrefab, parent);
        
        Image iconImage = item.transform.Find("IconImage")?.GetComponent<Image>();
        TextMeshProUGUI nameText = item.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI teamText = item.transform.Find("TeamText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI resultText = item.transform.Find("ResultText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI timeText = item.transform.Find("TimeText")?.GetComponent<TextMeshProUGUI>();
        
        // ===== 设置图标 =====
        if (iconImage != null)
        {
            if (won)
            {
                iconImage.sprite = aliveSprite;
                iconImage.color = Color.white;
            }
            else
            {
                iconImage.sprite = lostSprite;
                iconImage.color = Color.gray;
            }
        }
        
        if (nameText != null)
            nameText.text = player.PlayerName;
        
        if (teamText != null)
        {
            if (player.Role == PlayerRole.Hider)
            {
                teamText.text = "HIDER";
                teamText.color = new Color(0.2f, 0.8f, 0.2f);
            }
            else if (player.Role == PlayerRole.Seeker)
            {
                teamText.text = "HUNTER";
                teamText.color = new Color(0.9f, 0.3f, 0.2f);
            }
            else
            {
                teamText.text = "NONE";
                teamText.color = Color.gray;
            }
        }
        
        if (resultText != null)
        {
            if (won)
            {
                resultText.text = "WIN";
                resultText.color = new Color(0.2f, 0.8f, 0.2f);
            }
            else
            {
                resultText.text = "LOST";
                resultText.color = Color.red;
            }
        }
        
        if (timeText != null)
        {
            timeText.text = "00:00";
        }
        
        scoreItems.Add(item);
    }
    
    void EnsureCanvasGroup()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }
    
    void SetPanelVisible(bool visible)
    {
        EnsureCanvasGroup();
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
        
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }
    
    void OnMainMenuClicked()
    {
        Debug.Log("🏠 返回主菜单");
        
        SetPanelVisible(false);
        
        if (resultListController != null)
            resultListController.gameObject.SetActive(false);
        
        if (GameContract.IsRoomBound)
        {
            GameContract.RoomCommands.LeaveRoom();
        }
        
        if (NetworkServer.active)
            NetworkManager.singleton.StopHost();
        if (NetworkClient.active)
            NetworkManager.singleton.StopClient();
        
        UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
    }
    
    void OnLobbyClicked()
    {
        Debug.Log("🚪 返回大厅");
        
        SetPanelVisible(false);
        
        if (resultListController != null)
            resultListController.gameObject.SetActive(false);
        
        if (gameUI != null)
            gameUI.SetActive(true);
    }
}