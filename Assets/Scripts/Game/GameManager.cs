using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class GameManager : NetworkBehaviour, IGameStateReadonly
{
    [Header("UI")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI hiderCountText;
    public TextMeshProUGUI seekerCountText;
    public Image hiderIcon;
    public Image seekerIcon;
    
    [Header("结算面板")]
    public ResultPanelController resultPanel;
    
    [SyncVar(hook = nameof(OnStateChanged))]
    private GamePhase phase = GamePhase.Waiting;
    
    [SyncVar]
    private float phaseTimeLeft = 0f;
    
    [SyncVar]
    private int aliveHiders = 0;
    
    [SyncVar]
    private int totalHiders = 0;
    
    [SyncVar]
    private bool isPracticeLobby = true;
    
    [SyncVar]
    private int seekerCount = 0;
    
    [SyncVar]
    private int hiderCount = 0;
    
    private MatchResult matchResult = new MatchResult();
    private List<IPlayerStateReadonly> players = new List<IPlayerStateReadonly>();
    private bool isRunning = false;
    private bool gameEnded = false;
    
    // ===== 实现 IGameStateReadonly =====
    public GamePhase Phase => phase;
    public float PhaseTimeLeft => phaseTimeLeft;
    public int AliveHiders => aliveHiders;
    public int TotalHiders => totalHiders;
    public IPlayerStateReadonly LocalPlayer => null;
    public IReadOnlyList<IPlayerStateReadonly> Players => players;
    public bool IsLocalPlayerHost => false;
    public RoleSlots Slots => new RoleSlots 
    { 
        seekerCount = seekerCount,
        seekerMax = 2,
        hiderCount = hiderCount,
        hiderMax = 3
    };
    public bool IsPracticeLobby => isPracticeLobby;
    public MatchResult Result => matchResult;
    
    void Start()
    {
        if (isServer)
        {
            BindToContract();
        }
        
        SubscribeEvents();
    }
    
    void SubscribeEvents()
    {
        try
        {
            if (GameContract.IsBound)
            {
                GameContract.Events.OnGameEnded += OnGameEnded;
                Debug.Log("✅ GameManager 订阅 OnGameEnded 事件成功");
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
        if (gameEnded) return;
        gameEnded = true;
        
        Debug.Log($"🏆 游戏结算！结果: {result.result}, 存活: {result.survivors}, 用时: {result.duration}s");
        
        if (resultPanel != null)
        {
            List<IPlayerStateReadonly> playerList = new List<IPlayerStateReadonly>();
            if (GameContract.State != null)
            {
                playerList.AddRange(GameContract.State.Players);
            }
            
            resultPanel.ShowResult(result, playerList);
        }
        else
        {
            Debug.LogWarning("⚠️ ResultPanel 未绑定！");
        }
        
        if (statusText != null)
            statusText.gameObject.SetActive(false);
        if (timerText != null)
            timerText.gameObject.SetActive(false);
        if (hiderCountText != null)
            hiderCountText.gameObject.SetActive(false);
        if (seekerCountText != null)
            seekerCountText.gameObject.SetActive(false);
    }
    
    void BindToContract()
    {
        try
        {
            if (!GameContract.IsBound)
            {
                Debug.Log("[GameManager] 等待契约绑定...");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"契约绑定失败：{e.Message}");
        }
    }
    
    [Server]
    public void StartGame()
    {
        if (phase != GamePhase.Waiting) return;
        
        phase = GamePhase.Prep;
        phaseTimeLeft = GameConstants.PrepDuration;
        isRunning = true;
        isPracticeLobby = false;
        gameEnded = false;
        
        RpcUpdateUI("躲藏中...", phaseTimeLeft);
    }
    
    void Update()
    {
        if (!isServer) return;
        if (!isRunning) return;
        if (gameEnded) return;
        
        phaseTimeLeft -= Time.deltaTime;
        
        if (phaseTimeLeft <= 0f)
        {
            switch (phase)
            {
                case GamePhase.Prep:
                    phase = GamePhase.Playing;
                    phaseTimeLeft = GameConstants.MatchDuration;
                    RpcUpdateUI("搜寻中...", phaseTimeLeft);
                    break;
                case GamePhase.Playing:
                    EndGame();
                    break;
            }
        }
    }
    
    [Server]
    void EndGame()
    {
        phase = GamePhase.Ended;
        isRunning = false;
        
        int alive = 0;
        var identities = FindObjectsOfType<NetworkIdentity>();
        foreach (var p in identities)
        {
            if (p.gameObject.activeSelf && p.gameObject.CompareTag("Hider"))
                alive++;
        }
        aliveHiders = alive;
        
        matchResult.result = alive > 0 ? GameResult.HidersWin : GameResult.SeekersWin;
        matchResult.survivors = alive;
        matchResult.duration = GameConstants.MatchDuration;
        
        string result = alive > 0 ? $"躲藏者胜利！存活{alive}人" : "搜寻者胜利！";
        RpcUpdateUI(result, 0);
    }
    
    public void UpdatePlayersList(List<IPlayerStateReadonly> newPlayers)
    {
        players = newPlayers;
        
        hiderCount = 0;
        seekerCount = 0;
        foreach (var p in players)
        {
            if (p.Role == PlayerRole.Hider) hiderCount++;
            else if (p.Role == PlayerRole.Seeker) seekerCount++;
        }
        totalHiders = hiderCount;
        
        if (hiderCountText != null)
            hiderCountText.text = hiderCount.ToString();
        if (seekerCountText != null)
            seekerCountText.text = seekerCount.ToString();
    }
    
    public void UpdateAliveHiders(int count)
    {
        aliveHiders = count;
        
        if (hiderCountText != null)
            hiderCountText.text = count.ToString();
        
        if (hiderIcon != null)
        {
            hiderIcon.color = count > 0 ? Color.green : Color.gray;
        }
    }
    
    [ClientRpc]
    void RpcUpdateUI(string status, float time)
    {
        if (statusText != null) statusText.text = status;
        if (timerText != null) timerText.text = Mathf.CeilToInt(time).ToString() + "s";
    }
    
    void OnStateChanged(GamePhase oldVal, GamePhase newVal)
    {
        string stateName = newVal switch
        {
            GamePhase.Prep => "⏳ 躲藏中...",
            GamePhase.Playing => "🔍 搜寻中...",
            GamePhase.Ended => "🏁 游戏结束！",
            _ => "⏳ 等待开始"
        };
        if (statusText != null) statusText.text = stateName;
    }
}