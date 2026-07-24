using Mirror;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class GameManager : NetworkBehaviour, IGameStateReadonly
{
    [Header("UI")]
    public Text statusText;
    public Text timerText;
    public Text aliveCountText;
    
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
    
    // ===== 实现 IGameStateReadonly =====
    public GamePhase Phase => phase;
    public float PhaseTimeLeft => phaseTimeLeft;
    public int AliveHiders => aliveHiders;
    public int TotalHiders => totalHiders;
    public IPlayerStateReadonly LocalPlayer => null; // 暂未实现
    public IReadOnlyList<IPlayerStateReadonly> Players => players;
    public bool IsLocalPlayerHost => false; // 暂未实现
    public RoleSlots Slots => new RoleSlots 
    { 
        seekerCount = seekerCount,
        seekerMax = 2,
        hiderCount = hiderCount,
        hiderMax = 3
    };
    public bool IsPracticeLobby => isPracticeLobby;
    public MatchResult Result => matchResult;
    
    // ==================== 绑定契约事件 ====================
    void Start()
    {
        if (isServer)
        {
            BindToContract();
        }
    }
    
    void BindToContract()
    {
        try
        {
            if (!GameContract.IsBound)
            {
                // 程序 1 会在启动时绑定，这里只是备用
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
        
        // 通知契约事件
        if (GameContract.IsBound)
        {
            // GameContract.Events.OnPhaseChanged?.Invoke(phase, phaseTimeLeft);
        }
        
        RpcUpdateUI("躲藏中...", phaseTimeLeft);
    }
    
    void Update()
    {
        if (!isServer) return;
        if (!isRunning) return;
        
        phaseTimeLeft -= Time.deltaTime;
        
        if (phaseTimeLeft <= 0f)
        {
            switch (phase)
            {
                case GamePhase.Prep:
                    phase = GamePhase.Playing;
                    phaseTimeLeft = GameConstants.MatchDuration;
                    
                    if (GameContract.IsBound)
                    {
                        // GameContract.Events.OnPhaseChanged?.Invoke(phase, phaseTimeLeft);
                    }
                    
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
        
        // 统计存活躲藏者
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
        
        if (GameContract.IsBound)
        {
            // GameContract.Events.OnGameEnded?.Invoke(matchResult);
        }
        
        string result = alive > 0 ? $"躲藏者胜利！存活{alive}人" : "搜寻者胜利！";
        RpcUpdateUI(result, 0);
    }
    
    // ==================== 更新玩家列表 ====================
    public void UpdatePlayersList(List<IPlayerStateReadonly> newPlayers)
    {
        players = newPlayers;
        
        // 更新计数
        hiderCount = 0;
        seekerCount = 0;
        foreach (var p in players)
        {
            if (p.Role == PlayerRole.Hider) hiderCount++;
            else if (p.Role == PlayerRole.Seeker) seekerCount++;
        }
        totalHiders = hiderCount;
    }
    
    // ==================== 更新存活人数 ====================
    public void UpdateAliveHiders(int count)
    {
        aliveHiders = count;
        if (aliveCountText != null)
            aliveCountText.text = $"👥 存活: {count}人";
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