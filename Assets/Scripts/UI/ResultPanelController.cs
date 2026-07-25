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
    
    private List<GameObject> scoreItems = new List<GameObject>();
    private int gameNumber = 0;
    
    void Start()
    {
        if (mainMenuBtn != null)
            mainMenuBtn.onClick.AddListener(OnMainMenuClicked);
        
        if (lobbyBtn != null)
            lobbyBtn.onClick.AddListener(OnLobbyClicked);
        
        SubscribeEvents();
    }
    
    void SubscribeEvents()
    {
        try
        {
            if (GameContract.IsBound)
            {
                GameContract.Events.OnGameEnded += OnGameEnded;
                Debug.Log("✅ ResultPanelController 订阅 OnGameEnded 成功");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅事件失败：{e.Message}");
        }
    }
    
    void OnDestroy()
    {
        try
        {
            if (GameContract.IsBound)
            {
                GameContract.Events.OnGameEnded -= OnGameEnded;
            }
        }
        catch { }
    }
    
    void OnGameEnded(MatchResult result)
    {
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
        gameObject.SetActive(true);
        
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
            int total = players?.Count ?? 0;
            foreach (var p in players)
            {
                if (p != null && p.Role == PlayerRole.Hider && p.HiderState == HiderState.Captured)
                    captured++;
            }
            capturedText.text = $"CAPTURED: {captured}/{total}";
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
        
        // ===== 阵营标题 (HIDER / HUNTER) =====
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
        
        // ===== 输赢标题 (WIN / FAIL) =====
        if (titleResult != null)
        {
            if (result.result == GameResult.HidersWin)
            {
                titleResult.text = "WIN";
                titleResult.color = new Color(0.2f, 0.8f, 0.2f);
            }
            else if (result.result == GameResult.SeekersWin)
            {
                titleResult.text = "FAIL";
                titleResult.color = new Color(0.9f, 0.3f, 0.2f);
            }
            else
            {
                titleResult.text = "DRAW";
                titleResult.color = Color.yellow;
            }
        }
        
        UpdateScoreList(players);
    }
    
    void UpdateScoreList(List<IPlayerStateReadonly> players)
    {
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
                    Destroy(child.gameObject);
            }
        }
        
        if (players == null || players.Count == 0) return;
        
        List<IPlayerStateReadonly> aliveList = new List<IPlayerStateReadonly>();
        List<IPlayerStateReadonly> lostList = new List<IPlayerStateReadonly>();
        
        foreach (var player in players)
        {
            if (player == null) continue;
            bool isAlive = player.Role == PlayerRole.Hider && player.HiderState != HiderState.Captured;
            if (isAlive)
                aliveList.Add(player);
            else
                lostList.Add(player);
        }
        
        foreach (var player in aliveList)
        {
            CreateScoreItem(player, true);
        }
        
        foreach (var player in lostList)
        {
            CreateScoreItem(player, false);
        }
    }
    
    void CreateScoreItem(IPlayerStateReadonly player, bool isAlive)
    {
        GameObject item = Instantiate(scoreItemPrefab, scoreListParent);
        
        Image iconImage = item.transform.Find("IconImage")?.GetComponent<Image>();
        TextMeshProUGUI nameText = item.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI teamText = item.transform.Find("TeamText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI resultText = item.transform.Find("ResultText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI timeText = item.transform.Find("TimeText")?.GetComponent<TextMeshProUGUI>();
        
        if (iconImage != null)
        {
            if (isAlive)
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
        
        // ===== 阵营：HIDER / HUNTER（从契约读取 PlayerRole） =====
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
        
        // ===== 输赢：WIN / LOST（根据存活状态） =====
        if (resultText != null)
        {
            if (isAlive)
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
    
    void OnMainMenuClicked()
    {
        Debug.Log("🏠 返回主菜单");
        
        gameObject.SetActive(false);
        
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
        
        gameObject.SetActive(false);
        
        if (gameUI != null)
            gameUI.SetActive(true);
    }
}