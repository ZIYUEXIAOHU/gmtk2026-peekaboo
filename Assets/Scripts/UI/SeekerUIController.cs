using UnityEngine;
using TMPro;
using System.Collections;

public class SeekerUIController : MonoBehaviour
{
    [Header("状态")]
    public TextMeshProUGUI seekerStateText;    // 状态文字
    public TextMeshProUGUI caughtCountText;    // 捕获计数
    
    [Header("颜色")]
    public Color seekerColor = new Color(0.9f, 0.3f, 0.2f);
    
    private bool isSubscribed = false;
    
    void Start()
    {
        SubscribeEvents();
        StartCoroutine(DelayedForceUpdateUI());
    }

    void OnDestroy()
    {
        UnsubscribeEvents();
    }

    void Update()
    {
        if (!GameContract.IsBound || GameContract.State == null)
            return;

        IPlayerStateReadonly local = GameContract.State.LocalPlayer;
        if (local == null || local.Role != PlayerRole.Seeker)
            return;

        UpdateSeekerUI();
    }

    // ==================== 契约事件订阅 ====================
    
    void SubscribeEvents()
    {
        if (isSubscribed) return;
        
        if (!GameContract.IsBound)
        {
            Debug.Log("⏳ SeekerUIController: 契约未绑定，稍后重试订阅...");
            StartCoroutine(RetrySubscribeEvents());
            return;
        }
        
        try
        {
            GameContract.Events.OnPhaseChanged += OnPhaseChanged;
            GameContract.Events.OnCaptured += OnCaptured;
            GameContract.Events.OnGameEnded += OnGameEnded;
            isSubscribed = true;
            Debug.Log("✅ SeekerUIController 订阅契约事件成功");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅事件失败：{e.Message}");
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
        {
            Debug.Log("✅ 契约已绑定，SeekerUI 重试订阅事件");
            SubscribeEvents();
        }
        else
        {
            Debug.LogWarning("⚠️ 契约超时未绑定，SeekerUI 事件订阅失败");
        }
    }

    void UnsubscribeEvents()
    {
        if (!isSubscribed) return;
        if (!GameContract.IsBound) return;
        
        try
        {
            GameContract.Events.OnPhaseChanged -= OnPhaseChanged;
            GameContract.Events.OnCaptured -= OnCaptured;
            GameContract.Events.OnGameEnded -= OnGameEnded;
            isSubscribed = false;
        }
        catch { }
    }

    // ==================== UI 更新 ====================
    
    void UpdateSeekerUI()
    {
        if (!GameContract.IsBound || GameContract.State == null) return;
        
        int aliveHiders = GameContract.State.AliveHiders;
        int totalHiders = GameContract.State.TotalHiders;
        int capturedCount = totalHiders - aliveHiders;
        GamePhase phase = GameContract.State.Phase;
        
        // ===== 更新状态文字 =====
        if (seekerStateText != null)
        {
            string stateText = "";
            switch (phase)
            {
                case GamePhase.Waiting:
                    stateText = "WAITING...";
                    break;
                case GamePhase.Prep:
                    stateText = "PREP PHASE";
                    break;
                case GamePhase.Playing:
                    stateText = "SEEKING...";
                    break;
                case GamePhase.Ended:
                    stateText = "GAME OVER";
                    break;
                default:
                    stateText = "SEEKER";
                    break;
            }
            seekerStateText.text = stateText;
            seekerStateText.color = seekerColor;
        }
        
        // ===== 更新捕获计数 =====
        if (caughtCountText != null)
        {
            caughtCountText.text = $"CAPTURED: {capturedCount}/{totalHiders}";
        }
    }

    // ==================== 延迟初始化 ====================
    
    IEnumerator DelayedForceUpdateUI()
    {
        float waited = 0f;
        while (!GameContract.IsBound && waited < 5f)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
        }
        
        ForceUpdateUI();
    }

    void ForceUpdateUI()
    {
        if (!GameContract.IsBound || GameContract.State == null)
        {
            Debug.LogWarning("⚠️ 契约未就绪，SeekerUI 跳过初始更新");
            return;
        }

        IPlayerStateReadonly local = GameContract.State.LocalPlayer;
        if (local == null)
        {
            Debug.LogWarning("⚠️ 本地玩家为空，SeekerUI 跳过初始更新");
            return;
        }

        if (local.Role != PlayerRole.Seeker)
        {
            Debug.Log($"ℹ️ 本地玩家不是抓捕者 (Role={local.Role})");
            return;
        }

        UpdateSeekerUI();
        Debug.Log($"✅ SeekerUI 数据刷新完成，存活躲藏者: {GameContract.State.AliveHiders}/{GameContract.State.TotalHiders}");
    }

    // ==================== 契约事件回调 ====================
    
    void OnPhaseChanged(GamePhase phase, float duration)
    {
        ForceUpdateUI();
        Debug.Log($"📊 SeekerUI 阶段切换: {phase}, 时长: {duration}s");
    }
    
    void OnCaptured(CaptureInfo info)
    {
        ForceUpdateUI();
        Debug.Log($"🔴 捕获: 剩余存活={info.aliveHiders}");
    }
    
    void OnGameEnded(MatchResult result)
    {
        if (seekerStateText != null)
        {
            string resultText = result.result == GameResult.HidersWin 
                ? "HIDERS WIN!" 
                : "SEEKERS WIN!";
            seekerStateText.text = resultText;
        }
        Debug.Log($"🏁 游戏结束: {result.result}");
    }
    
    // ==================== 外部控制 ====================
    
    public void Show()
    {
        gameObject.SetActive(true);
    }
    
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
