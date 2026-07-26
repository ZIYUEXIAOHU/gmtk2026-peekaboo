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
    
    [Header("Results Panel")]
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
    
    // ===== IGameStateReadonly implementation =====
    public GamePhase Phase => phase;
    public float PhaseTimeLeft => phaseTimeLeft;
    public float NextTransformTimeLeft => 0f;
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
                Debug.Log("✅ GameManager subscribed to OnGameEnded event successfully");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to subscribe to event: {e.Message}");
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
        
        Debug.Log($"🏆 Game results! Result: {result.result}, Survivors: {result.survivors}, Duration: {result.duration}s");
        
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
            Debug.LogWarning("⚠️ ResultPanel is not bound!");
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
                Debug.Log("[GameManager] Waiting for contract binding...");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Contract binding failed: {e.Message}");
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
        
        RpcUpdateUI("Hiding...", phaseTimeLeft);
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
                    RpcUpdateUI("Seeking...", phaseTimeLeft);
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
        
        string result = alive > 0 ? $"Hiders win! {alive} survived" : "Seekers win!";
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
            GamePhase.Prep => "Hiding...",
            GamePhase.Playing => "Seeking...",
            GamePhase.Ended => "Game Over!",
            _ => "Waiting to start"
        };
        if (statusText != null) statusText.text = stateName;
    }
}