using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mirror;  // ← 添加这行

public class ResultPanelController : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI durationText;
    public Transform scoreListParent;
    public GameObject scoreItemPrefab;
    
    [Header("按钮")]
    public Button replayBtn;        // 再来一局
    public Button quitBtn;          // 离开房间（返回主界面）
    
    [Header("游戏UI")]
    public GameObject gameUI;       // 游戏HUD（隐藏）
    
    private List<GameObject> scoreItems = new List<GameObject>();
    
    void Start()
    {
        if (replayBtn != null)
            replayBtn.onClick.AddListener(OnReplayClicked);
        
        if (quitBtn != null)
            quitBtn.onClick.AddListener(OnQuitClicked);
    }
    
    public void ShowResult(MatchResult result, List<IPlayerStateReadonly> players)
    {
        gameObject.SetActive(true);
        
        // ===== 隐藏GameUI =====
        if (gameUI != null)
            gameUI.SetActive(false);
        
        if (resultText != null)
        {
            if (result.result == GameResult.HidersWin)
            {
                resultText.text = "🎉 躲藏者胜利！";
                resultText.color = new Color(0.2f, 0.8f, 0.2f);
            }
            else if (result.result == GameResult.SeekersWin)
            {
                resultText.text = "🔴 抓捕者胜利！";
                resultText.color = new Color(0.9f, 0.3f, 0.2f);
            }
            else
            {
                resultText.text = "⚖️ 平局！";
                resultText.color = Color.yellow;
            }
        }
        
        if (durationText != null)
        {
            durationText.text = $"⏱️ 用时: {Mathf.CeilToInt(result.duration)}s";
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
        
        if (players == null || players.Count == 0) return;
        
        int rank = 1;
        foreach (var player in players)
        {
            if (player == null) continue;
            
            GameObject item = Instantiate(scoreItemPrefab, scoreListParent);
            
            TextMeshProUGUI rankText = item.transform.Find("RankText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI nameText = item.transform.Find("PlayerNameText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI roleText = item.transform.Find("RoleText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI scoreText = item.transform.Find("ScoreText")?.GetComponent<TextMeshProUGUI>();
            
            if (rankText != null) rankText.text = $"#{rank}";
            if (nameText != null) nameText.text = player.PlayerName;
            
            if (roleText != null)
            {
                roleText.text = GetRoleDisplayName(player.Role);
                roleText.color = GetRoleColor(player.Role);
            }
            
            if (scoreText != null) scoreText.text = "0分";
            
            rank++;
            scoreItems.Add(item);
        }
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
            default: return Color.white;
        }
    }
    
    // ==================== 按钮事件 ====================
    
    /// <summary>再来一局：返回等待房间</summary>
    void OnReplayClicked()
    {
        Debug.Log("🔄 再来一局");
        
        // 隐藏结算面板
        gameObject.SetActive(false);
        
        // 显示GameUI
        if (gameUI != null)
            gameUI.SetActive(true);
        
        // 回到等待房间（不离开网络）
        // 由 GameManager 或其他逻辑处理重新开始
    }
    
    /// <summary>离开房间：返回主菜单</summary>
    void OnQuitClicked()
    {
        Debug.Log("🚪 离开房间，返回主菜单");
        
        // 隐藏结算面板
        gameObject.SetActive(false);
        
        // 使用契约离开房间
        if (GameContract.IsRoomBound)
        {
            GameContract.RoomCommands.LeaveRoom();
        }
        
        // 停止网络连接
        if (NetworkServer.active)
            NetworkManager.singleton.StopHost();
        if (NetworkClient.active)
            NetworkManager.singleton.StopClient();
        
        // 切换回大厅场景
        UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
    }
}