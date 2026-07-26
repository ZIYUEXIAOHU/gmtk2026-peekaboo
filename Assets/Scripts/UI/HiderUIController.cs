using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using Mirror;

public class HiderUIController : MonoBehaviour
{
    [Header("状态")]
    public TextMeshProUGUI hiderStateText;
    public TextMeshProUGUI transformTimer;
    public Image disguiseStatusIcon;
    public TextMeshProUGUI placeHintText;
    
    [Header("物品栏")]
    public Transform inventoryParent;
    public GameObject itemSlotPrefab;
    public int maxSlots = 7;
    public float slotSize = 50f;
    public float slotSpacing = 10f;
    public TextMeshProUGUI inventoryCountText;
    
    [Header("观战 UI")]
    public GameObject observerUI;
    public TextMeshProUGUI observerNameText;
    public TextMeshProUGUI observerStatusText;
    public Button observerPrevBtn;
    public Button observerNextBtn;
    
    [Header("颜色")]
    public Color disguisedColor = new Color(0.2f, 0.8f, 0.2f);
    public Color invisibleColor = new Color(1f, 0.8f, 0f);
    public Color ghostColor = new Color(1f, 0.3f, 0.3f);
    public Color capturedColor = new Color(0.5f, 0.5f, 0.5f);
    
    private List<Image> slotIcons = new List<Image>();
    private List<Image> slotHighlights = new List<Image>();
    private List<GameObject> slotObjects = new List<GameObject>();
    private List<GameObject> usedBgs = new List<GameObject>();
    private List<GameObject> emptyBgs = new List<GameObject>();
    private ItemTable itemTable;
    
    private List<IPlayerStateReadonly> aliveHiders = new List<IPlayerStateReadonly>();
    private int currentObserverIndex = 0;
    private bool isObserving = false;
    private string lastInventorySignature = null;
    private HiderState lastHiderState = (HiderState)(-1);
    private bool hasInitializedUI = false;
    private bool isSubscribed = false;
    /// <summary>本地躲藏者无敌结束时刻（NetworkTime.time）；0 = 无。</summary>
    private double localInvulnerableUntil;
    
    void Start()
    {
        // ===== 加载共享物品表（契约：双方引用同一份资产） =====
        itemTable = Resources.Load<ItemTable>("ItemTable");
        if (itemTable == null)
        {
            Debug.LogWarning("⚠️ ItemTable 未找到，物品栏图标将无法显示");
        }
        
        if (placeHintText != null)
            placeHintText.gameObject.SetActive(true);
        
        InitializeInventory();
        UpdateInventoryCount(0);
        
        if (observerUI != null)
            observerUI.SetActive(false);
        
        if (observerPrevBtn != null)
            observerPrevBtn.onClick.AddListener(OnPrevObserver);
        if (observerNextBtn != null)
            observerNextBtn.onClick.AddListener(OnNextObserver);

        // ===== 订阅契约事件（如果未绑定则等待重试） =====
        SubscribeEvents();
        
        // ===== 延迟初始化 UI（等待契约就绪） =====
        StartCoroutine(DelayedForceUpdateUI());
    }

    void OnDestroy()
    {
        UnsubscribeEvents();
    }

    // ==================== 契约事件订阅 ====================
    
    void SubscribeEvents()
    {
        if (isSubscribed) return;
        
        // ===== 如果契约未绑定，等待重试 =====
        if (!GameContract.IsBound)
        {
            Debug.Log("⏳ HiderUIController: 契约未绑定，稍后重试订阅...");
            StartCoroutine(RetrySubscribeEvents());
            return;
        }
        
        // ===== 契约已绑定，正常订阅 =====
        try
        {
            GameContract.Events.OnPhaseChanged += OnPhaseChanged;
            GameContract.Events.OnHiderTransformed += OnHiderTransformed;
            GameContract.Events.OnCaptured += OnCaptured;
            GameContract.Events.OnHiderRespawned += OnHiderRespawned;
            GameContract.Events.OnHeartbeatPulse += OnHeartbeatPulse;  // ← 新增
            isSubscribed = true;
            Debug.Log("✅ HiderUIController 订阅契约事件成功");
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
            Debug.Log("✅ 契约已绑定，HiderUI 重试订阅事件");
            SubscribeEvents();
        }
        else
        {
            Debug.LogWarning("⚠️ 契约超时未绑定，HiderUI 事件订阅失败");
        }
    }

    void UnsubscribeEvents()
    {
        if (!isSubscribed) return;
        if (!GameContract.IsBound) return;
        
        try
        {
            GameContract.Events.OnPhaseChanged -= OnPhaseChanged;
            GameContract.Events.OnHiderTransformed -= OnHiderTransformed;
            GameContract.Events.OnCaptured -= OnCaptured;
            GameContract.Events.OnHiderRespawned -= OnHiderRespawned;
            GameContract.Events.OnHeartbeatPulse -= OnHeartbeatPulse;  // ← 新增
            isSubscribed = false;
        }
        catch { }
    }

    void Update()
    {
        // ===== 严格遵循契约：通过 GameContract.State 获取数据 =====
        if (!GameContract.IsBound || GameContract.State == null)
            return;

        IPlayerStateReadonly local = GameContract.State.LocalPlayer;
        if (local == null || local.Role != PlayerRole.Hider)
            return;

        // 无敌剩余优先；否则轮询 State.NextTransformTimeLeft
        UpdateInvulnerableOrTransformTimer();

        // ===== 只更新数据，不控制激活状态 =====
        // 激活状态由 LobbyRoomController.UpdateRoleUI() 控制
        
        string signature = BuildInventorySignature(local.ItemQueue);
        if (signature == lastInventorySignature && local.HiderState == lastHiderState && hasInitializedUI)
            return;

        lastInventorySignature = signature;
        lastHiderState = local.HiderState;
        hasInitializedUI = true;
        
        UpdateHiderUI(local, GameContract.State.Players);
    }

    void UpdateInvulnerableOrTransformTimer()
    {
        if (isObserving || transformTimer == null)
            return;

        if (localInvulnerableUntil > 0)
        {
            float invulnLeft = (float)(localInvulnerableUntil - NetworkTime.time);
            if (invulnLeft > 0f)
            {
                transformTimer.text = $"Invincible: {Mathf.CeilToInt(invulnLeft)}s";
                transformTimer.color = invisibleColor;
                transformTimer.gameObject.SetActive(true);
                return;
            }
            localInvulnerableUntil = 0;
        }

        if (!GameContract.IsBound || GameContract.State == null)
        {
            transformTimer.gameObject.SetActive(false);
            return;
        }

        float transformLeft = GameContract.State.NextTransformTimeLeft;
        if (transformLeft > 0f)
        {
            UpdateTransformTimer(transformLeft);
            return;
        }

        transformTimer.gameObject.SetActive(false);
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
            Debug.LogWarning("⚠️ 契约未就绪，跳过初始 UI 更新");
            return;
        }

        IPlayerStateReadonly local = GameContract.State.LocalPlayer;
        if (local == null)
        {
            Debug.LogWarning("⚠️ 本地玩家为空，跳过初始 UI 更新");
            return;
        }

        if (local.Role != PlayerRole.Hider)
        {
            Debug.Log($"ℹ️ 本地玩家不是躲藏者 (Role={local.Role})");
            return;
        }

        // ===== 重置缓存，强制刷新 =====
        lastInventorySignature = null;
        lastHiderState = (HiderState)(-1);
        hasInitializedUI = false;
        
        // ===== 直接更新 UI =====
        UpdateHiderUI(local, GameContract.State.Players);
        Debug.Log($"✅ HiderUI 数据刷新完成，物品数量: {local.ItemQueue?.Count ?? 0}");
    }

    // ==================== 物品栏签名 ====================
    
    static string BuildInventorySignature(IReadOnlyList<int> queue)
    {
        if (queue == null || queue.Count == 0)
            return "0";

        var sb = new System.Text.StringBuilder(queue.Count * 4);
        sb.Append(queue.Count);
        for (int i = 0; i < queue.Count; i++)
        {
            sb.Append(':');
            sb.Append(queue[i]);
        }
        return sb.ToString();
    }
    
    // ==================== 物品栏初始化 ====================
    
    void InitializeInventory()
    {
        foreach (var obj in slotObjects)
        {
            Destroy(obj);
        }
        slotIcons.Clear();
        slotHighlights.Clear();
        slotObjects.Clear();
        usedBgs.Clear();
        emptyBgs.Clear();
        
        if (itemSlotPrefab == null || inventoryParent == null) return;
        
        for (int i = 0; i < maxSlots; i++)
        {
            CreateSlot(i);
        }
    }
    
    GameObject CreateSlot(int index)
    {
        GameObject slot = Instantiate(itemSlotPrefab, inventoryParent);
        slot.name = $"ItemSlot_{index}";
        
        Image icon = FindChildImage(slot.transform, "ItemIcon");
        Image highlight = FindChildImage(slot.transform, "Highlight");
        
        GameObject usedBg = slot.transform.Find("UsedBg")?.gameObject;
        GameObject emptyBg = slot.transform.Find("EmptyBg")?.gameObject;
        
        RectTransform rect = slot.GetComponent<RectTransform>();
        if (rect != null)
        {
            float spacing = slotSize + slotSpacing;
            float startX = -(maxSlots - 1) * spacing / 2f;
            rect.anchoredPosition = new Vector2(startX + index * spacing, 0);
            rect.sizeDelta = new Vector2(slotSize, slotSize);
        }
        
        slotIcons.Add(icon);
        slotHighlights.Add(highlight);
        slotObjects.Add(slot);
        usedBgs.Add(usedBg);
        emptyBgs.Add(emptyBg);
        
        if (highlight != null)
            highlight.gameObject.SetActive(false);
        
        if (usedBg != null) usedBg.SetActive(false);
        if (emptyBg != null) emptyBg.SetActive(true);
        
        slot.SetActive(false);
        
        if (inventoryCountText != null)
        {
            int countTextIndex = inventoryCountText.transform.GetSiblingIndex();
            slot.transform.SetSiblingIndex(countTextIndex);
        }
        
        return slot;
    }

    /// <summary>
    /// Find child Image by name; also matches names with leading/trailing whitespace
    /// so older prefab variants like " Highlight" still bind.
    /// </summary>
    static Image FindChildImage(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName))
            return null;

        Transform exact = parent.Find(childName);
        if (exact != null)
            return exact.GetComponent<Image>();

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name.Trim() == childName)
                return child.GetComponent<Image>();
        }

        return null;
    }
    
    void EnsureSlots(int count)
    {
        while (slotObjects.Count < count)
        {
            CreateSlot(slotObjects.Count);
        }
    }
    
    void UpdateInventoryCount(int itemCount)
    {
        if (inventoryCountText == null) return;
        inventoryCountText.text = $"{itemCount}/{maxSlots}";
    }

    Sprite ResolveItemIcon(int itemId)
    {
        if (itemTable == null || !itemTable.IsValid(itemId))
            return null;

        ItemTable.Entry entry = itemTable.Get(itemId);
        if (entry == null)
            return null;

        if (entry.icon != null)
            return entry.icon;

        if (entry.prefab != null)
        {
            var sr = entry.prefab.GetComponent<SpriteRenderer>();
            if (sr != null)
                return sr.sprite;
        }

        return null;
    }
    
    // ==================== 物品栏更新 ====================
    
    public void UpdateInventory(IReadOnlyList<int> itemQueue)
    {
        if (itemQueue == null) return;
        
        int itemCount = itemQueue.Count;
        
        EnsureSlots(Mathf.Max(itemCount, maxSlots));
        
        for (int i = 0; i < slotObjects.Count; i++)
        {
            GameObject slot = slotObjects[i];
            
            if (i < itemCount)
            {
                slot.SetActive(true);
                
                if (i < usedBgs.Count && usedBgs[i] != null)
                    usedBgs[i].SetActive(true);
                if (i < emptyBgs.Count && emptyBgs[i] != null)
                    emptyBgs[i].SetActive(false);
                
                Image icon = i < slotIcons.Count ? slotIcons[i] : null;
                Image highlight = i < slotHighlights.Count ? slotHighlights[i] : null;
                
                int itemId = itemQueue[i];
                Sprite sprite = ResolveItemIcon(itemId);

                if (icon != null)
                {
                    if (sprite != null)
                    {
                        icon.sprite = sprite;
                        icon.color = Color.white;
                    }
                    else
                    {
                        icon.sprite = null;
                        icon.color = new Color(1, 1, 1, 0.3f);
                    }
                }
                
                if (highlight != null)
                    highlight.gameObject.SetActive(i == 0);
            }
            else if (i < maxSlots)
            {
                // Keep empty capacity frames visible; count uses itemQueue.Count only.
                slot.SetActive(true);
                
                if (i < usedBgs.Count && usedBgs[i] != null)
                    usedBgs[i].SetActive(false);
                if (i < emptyBgs.Count && emptyBgs[i] != null)
                    emptyBgs[i].SetActive(true);
                
                Image icon = i < slotIcons.Count ? slotIcons[i] : null;
                if (icon != null)
                {
                    icon.sprite = null;
                    icon.color = new Color(1, 1, 1, 0f);
                }
                
                if (i < slotHighlights.Count && slotHighlights[i] != null)
                    slotHighlights[i].gameObject.SetActive(false);
            }
            else
            {
                slot.SetActive(false);
            }
        }
        
        UpdateInventoryCount(itemCount);
    }
    
    // ==================== Hider UI 更新 ====================
    
    public void UpdateHiderUI(IPlayerStateReadonly playerState, IReadOnlyList<IPlayerStateReadonly> allPlayers)
    {
        if (playerState == null) return;
        
        bool isCaptured = (playerState.HiderState == HiderState.Captured);
        
        if (isCaptured)
        {
            EnterObserverMode(allPlayers);
        }
        else
        {
            ExitObserverMode();
            UpdateHiderState(playerState.HiderState);
            UpdateInventory(playerState.ItemQueue);
        }
    }
    
    // ==================== 观战模式 ====================
    
    void EnterObserverMode(IReadOnlyList<IPlayerStateReadonly> allPlayers)
    {
        isObserving = true;
        
        if (placeHintText != null)
            placeHintText.gameObject.SetActive(false);
        if (inventoryParent != null)
            inventoryParent.gameObject.SetActive(false);
        if (transformTimer != null)
            transformTimer.gameObject.SetActive(false);
        
        if (hiderStateText != null)
        {
            hiderStateText.text = "CAPTURED - SPECTATING";
            hiderStateText.color = capturedColor;
        }
        if (disguiseStatusIcon != null)
            disguiseStatusIcon.color = capturedColor;
        
        aliveHiders.Clear();
        if (allPlayers != null)
        {
            foreach (var player in allPlayers)
            {
                if (player != null && 
                    player.Role == PlayerRole.Hider && 
                    player.HiderState != HiderState.Captured)
                {
                    aliveHiders.Add(player);
                }
            }
        }
        
        if (aliveHiders.Count > 0)
        {
            currentObserverIndex = 0;
            ShowObserverUI(true);
            UpdateObserverTarget();
        }
        else
        {
            ShowObserverUI(false);
            if (observerNameText != null)
                observerNameText.text = "NO TEAMMATES ALIVE";
            if (observerStatusText != null)
                observerStatusText.text = "GAME OVER";
        }
    }
    
    void ExitObserverMode()
    {
        isObserving = false;
        ShowObserverUI(false);
        
        if (placeHintText != null)
            placeHintText.gameObject.SetActive(true);
        if (inventoryParent != null)
            inventoryParent.gameObject.SetActive(true);
        // transformTimer 可见性由 UpdateInvulnerableOrTransformTimer 控制（无 NextTransformTimeLeft 时默认隐藏）
        UpdateInvulnerableOrTransformTimer();
    }
    
    void ShowObserverUI(bool show)
    {
        if (observerUI != null)
            observerUI.SetActive(show);
    }
    
    void UpdateObserverTarget()
    {
        if (aliveHiders.Count == 0 || currentObserverIndex >= aliveHiders.Count)
        {
            if (observerNameText != null)
                observerNameText.text = "NO TEAMMATES ALIVE";
            if (observerStatusText != null)
                observerStatusText.text = "GAME OVER";
            return;
        }
        
        IPlayerStateReadonly target = aliveHiders[currentObserverIndex];
        if (target == null) return;
        
        if (observerNameText != null)
            observerNameText.text = target.PlayerName;
        
        if (observerStatusText != null)
        {
            bool isAlive = (target.HiderState != HiderState.Captured);
            observerStatusText.text = isAlive ? "ALIVE" : "CAPTURED";
            observerStatusText.color = isAlive ? new Color(0.2f, 0.8f, 0.2f) : Color.red;
        }
        
        if (observerPrevBtn != null)
            observerPrevBtn.interactable = (currentObserverIndex > 0);
        if (observerNextBtn != null)
            observerNextBtn.interactable = (currentObserverIndex < aliveHiders.Count - 1);
    }
    
    void OnPrevObserver()
    {
        if (aliveHiders.Count == 0) return;
        currentObserverIndex = (currentObserverIndex - 1 + aliveHiders.Count) % aliveHiders.Count;
        UpdateObserverTarget();
        Debug.Log($"👁️ 切换到上一个观战目标: {aliveHiders[currentObserverIndex].PlayerName}");
    }
    
    void OnNextObserver()
    {
        if (aliveHiders.Count == 0) return;
        currentObserverIndex = (currentObserverIndex + 1) % aliveHiders.Count;
        UpdateObserverTarget();
        Debug.Log($"👁️ 切换到下一个观战目标: {aliveHiders[currentObserverIndex].PlayerName}");
    }
    
    // ==================== 状态显示 ====================
    
    void UpdateHiderState(HiderState state)
    {
        if (hiderStateText == null || disguiseStatusIcon == null) return;
        
        string stateText = "";
        Color stateColor = Color.white;
        
        switch (state)
        {
            case HiderState.Disguised:
                stateText = "Disguised";
                stateColor = disguisedColor;
                break;
            case HiderState.Invisible:
                stateText = "Invisible";
                stateColor = invisibleColor;
                break;
            case HiderState.Ghost:
                stateText = "Ghost";
                stateColor = ghostColor;
                break;
            case HiderState.Captured:
                stateText = "CAPTURED - SPECTATING";
                stateColor = capturedColor;
                break;
            default:
                stateText = "Disguised";
                stateColor = disguisedColor;
                break;
        }
        
        hiderStateText.text = stateText;
        hiderStateText.color = stateColor;
        disguiseStatusIcon.color = stateColor;
    }
    
    /// <summary>显示距下次变身剩余秒数（由 UpdateInvulnerableOrTransformTimer 从 State.NextTransformTimeLeft 驱动）。</summary>
    public void UpdateTransformTimer(float timeLeft)
    {
        if (transformTimer == null) return;
        
        if (isObserving)
        {
            transformTimer.gameObject.SetActive(false);
            return;
        }
        
        if (timeLeft > 0)
        {
            transformTimer.text = $"Transform: {Mathf.CeilToInt(timeLeft)}s";
            transformTimer.color = Color.white;
            transformTimer.gameObject.SetActive(true);
        }
        else
        {
            transformTimer.text = "Ready to transform...";
            transformTimer.color = Color.yellow;
            transformTimer.gameObject.SetActive(true);
        }
    }
    
    // ==================== 心跳脉冲事件 ====================
    
    void OnHeartbeatPulse(HeartbeatPulse pulse)
    {
        if (!GameContract.IsAudioBound) return;

        IPlayerStateReadonly local = GameContract.State?.LocalPlayer;
        if (local == null || local.Role != PlayerRole.Hider) return;

        // 获取本地位置
        Vector2 localPos = transform.position;

        // 如果在心跳椭圆内，播放心跳声
        if (GameConstants.IsInHeartbeatRange(pulse.center, localPos))
        {
            GameContract.Audio.PlayHeartbeat();
        }
    }
    
    // ==================== 契约事件回调 ====================
    
    void OnPhaseChanged(GamePhase phase, float duration)
    {
        ForceUpdateUI();
        Debug.Log($"📊 HiderUI 阶段切换: {phase}, 时长: {duration}s");
    }
    
    void OnHiderTransformed(TransformInfo info)
    {
        if (!GameContract.IsBound || GameContract.State == null) return;
        IPlayerStateReadonly local = GameContract.State.LocalPlayer;
        if (local == null || local.NetId != info.hiderNetId) return;

        localInvulnerableUntil = info.invulnerableUntil;
        ForceUpdateUI();
        Debug.Log($"🔄 躲藏者变换: NetId={info.hiderNetId}, ItemId={info.newItemId}, invulnUntil={info.invulnerableUntil}");
    }
    
    void OnCaptured(CaptureInfo info)
    {
        if (!GameContract.IsBound || GameContract.State == null) return;
        IPlayerStateReadonly local = GameContract.State.LocalPlayer;
        if (local == null || local.NetId != info.hiderNetId) return;
        
        ForceUpdateUI();
        Debug.Log($"🔴 被捕获: NetId={info.hiderNetId}, 剩余存活={info.aliveHiders}");
    }
    
    void OnHiderRespawned(RespawnInfo info)
    {
        if (!GameContract.IsBound || GameContract.State == null) return;
        IPlayerStateReadonly local = GameContract.State.LocalPlayer;
        if (local == null || local.NetId != info.hiderNetId) return;
        
        ForceUpdateUI();
        Debug.Log($"🔄 复活: NetId={info.hiderNetId}, ItemId={info.itemId}");
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
    
    public void SetPlaceHint(bool show)
    {
        if (placeHintText != null && !isObserving)
            placeHintText.gameObject.SetActive(show);
    }
}