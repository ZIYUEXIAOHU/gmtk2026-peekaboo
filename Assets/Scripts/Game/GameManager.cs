using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class GameManager : NetworkBehaviour
{
    [Header("UI")]
    public Text statusText;
    public Text timerText;
    public Text aliveCountText;
    
    [SyncVar(hook = nameof(OnStateChanged))]
    public GameState currentState = GameState.Waiting;
    
    [SyncVar]
    public float currentTimer = 0f;
    
    private bool isRunning = false;
    
    public enum GameState
    {
        Waiting,   // 等待开始
        Hiding,    // 躲藏阶段
        Seeking,   // 搜寻阶段
        Ended      // 游戏结束
    }
    
    [Server]
    public void StartGame()
    {
        if (currentState != GameState.Waiting) return;
        
        currentState = GameState.Hiding;
        currentTimer = 30f;
        isRunning = true;
        
        RpcUpdateUI("躲藏中...", currentTimer);
    }
    
    void Update()
    {
        if (!isServer) return;
        if (!isRunning) return;
        
        currentTimer -= Time.deltaTime;
        
        if (currentTimer <= 0f)
        {
            switch (currentState)
            {
                case GameState.Hiding:
                    currentState = GameState.Seeking;
                    currentTimer = 60f;
                    RpcUpdateUI("搜寻中...", currentTimer);
                    break;
                case GameState.Seeking:
                    EndGame();
                    break;
            }
        }
    }
    
    [Server]
    void EndGame()
    {
        currentState = GameState.Ended;
        isRunning = false;
        
        // 统计存活躲藏者
        int alive = 0;
        var players = FindObjectsOfType<NetworkIdentity>();
        foreach (var p in players)
        {
            // 简单统计
            if (p.gameObject.activeSelf)
                alive++;
        }
        
        string result = alive > 0 ? $"躲藏者胜利！存活{alive}人" : "搜寻者胜利！";
        RpcUpdateUI(result, 0);
    }
    
    [ClientRpc]
    void RpcUpdateUI(string status, float time)
    {
        if (statusText != null) statusText.text = status;
        if (timerText != null) timerText.text = Mathf.CeilToInt(time).ToString() + "s";
    }
    
    void OnStateChanged(GameState oldVal, GameState newVal)
    {
        string stateName = "";
        switch (newVal)
        {
            case GameState.Hiding: stateName = "躲藏中..."; break;
            case GameState.Seeking: stateName = "搜寻中..."; break;
            case GameState.Ended: stateName = "游戏结束！"; break;
            default: stateName = "等待开始"; break;
        }
        if (statusText != null) statusText.text = stateName;
    }
}