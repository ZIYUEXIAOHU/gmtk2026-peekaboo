using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class TopBarController : MonoBehaviour
{
    [Header("躲藏者图标")]
    public Transform hiderIconParent;       // 存放图标的父物体
    public GameObject hiderIconPrefab;      // 躲藏者图标预制体
    public Sprite hiderAliveSprite;         // 存活图标
    public Sprite hiderCapturedSprite;      // 被抓图标
    
    [Header("UI")]
    public TextMeshProUGUI timerText;       // 倒计时文本
    // StatusText 已移除
    
    private List<Image> hiderIcons = new List<Image>();
    private int totalHiders = 0;
    
    void Start()
    {
        SubscribeEvents();
    }
    
    void SubscribeEvents()
    {
        try
        {
            if (GameContract.IsBound)
            {
                GameContract.Events.OnPhaseChanged += OnPhaseChanged;
                Debug.Log("✅ TopBarController 订阅事件成功");
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
                GameContract.Events.OnPhaseChanged -= OnPhaseChanged;
            }
        }
        catch { }
    }
    
    void Update()
    {
        if (!GameContract.IsBound || GameContract.State == null)
            return;

        UpdateHiderIcons();

        // 契约无 tick 事件：每帧轮询 PhaseTimeLeft（与 GameMenuController 同模式）
        GamePhase phase = GameContract.State.Phase;
        if (phase == GamePhase.Prep || phase == GamePhase.Playing)
            UpdateTimer(GameContract.State.PhaseTimeLeft);
        else
            UpdateTimerIdle();
    }
    
    public void UpdateHiderIcons()
    {
        if (GameContract.State == null) return;
        
        List<IPlayerStateReadonly> hiders = new List<IPlayerStateReadonly>();
        foreach (var player in GameContract.State.Players)
        {
            if (player != null && player.Role == PlayerRole.Hider)
            {
                hiders.Add(player);
            }
        }
        
        int currentTotal = hiders.Count;
        
        if (currentTotal != totalHiders)
        {
            totalHiders = currentTotal;
            RecreateIcons(totalHiders);
        }
        
        for (int i = 0; i < hiderIcons.Count && i < hiders.Count; i++)
        {
            Image icon = hiderIcons[i];
            IPlayerStateReadonly player = hiders[i];
            
            if (icon == null) continue;
            
            bool isCaptured = (player.HiderState == HiderState.Captured);
            
            if (isCaptured)
            {
                if (hiderCapturedSprite != null)
                    icon.sprite = hiderCapturedSprite;
                icon.color = Color.gray;
            }
            else
            {
                if (hiderAliveSprite != null)
                    icon.sprite = hiderAliveSprite;
                icon.color = Color.white;
            }
        }
    }
    
    void RecreateIcons(int count)
    {
        foreach (var icon in hiderIcons)
        {
            if (icon != null)
                Destroy(icon.gameObject);
        }
        hiderIcons.Clear();
        
        if (hiderIconPrefab == null || hiderIconParent == null) return;
        
        for (int i = 0; i < count; i++)
        {
            GameObject iconObj = Instantiate(hiderIconPrefab, hiderIconParent);
            Image icon = iconObj.GetComponent<Image>();
            if (icon != null)
            {
                hiderIcons.Add(icon);
            }
        }
        
        Debug.Log($"🔄 创建 {count} 个躲藏者图标");
    }
    
    void OnPhaseChanged(GamePhase phase, float duration)
    {
        if (phase != GamePhase.Waiting)
        {
            UpdateHiderIcons();
        }
    }
    
    public void UpdateTimer(float timeLeft)
    {
        if (timerText != null)
            timerText.text = Mathf.CeilToInt(timeLeft).ToString() + "s";
    }

    void UpdateTimerIdle()
    {
        if (timerText != null)
            timerText.text = "--";
    }
}