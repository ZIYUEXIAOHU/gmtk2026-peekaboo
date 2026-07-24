using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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
    public int maxSlots = 5;
    public float slotSize = 50f;
    public float slotSpacing = 10f;
    
    [Header("观战 UI")]
    public GameObject observerUI;               // 观战面板
    public TextMeshProUGUI observerNameText;    // 观战者名字
    public TextMeshProUGUI observerStatusText;  // 观战者状态
    public Button observerPrevBtn;              // 上一个
    public Button observerNextBtn;              // 下一个
    
    [Header("颜色")]
    public Color disguisedColor = new Color(0.2f, 0.8f, 0.2f);
    public Color invisibleColor = new Color(1f, 0.8f, 0f);
    public Color ghostColor = new Color(1f, 0.3f, 0.3f);
    public Color capturedColor = new Color(0.5f, 0.5f, 0.5f);
    
    private List<Image> slotIcons = new List<Image>();
    private List<Image> slotHighlights = new List<Image>();
    private List<GameObject> slotObjects = new List<GameObject>();
    private ItemTable itemTable;
    
    // ===== 观战相关 =====
    private List<IPlayerStateReadonly> aliveHiders = new List<IPlayerStateReadonly>();
    private int currentObserverIndex = 0;
    private bool isObserving = false;
    
    void Start()
    {
        itemTable = Resources.Load<ItemTable>("Data/ItemTable");
        if (itemTable == null)
        {
            Debug.LogWarning("⚠️ ItemTable 未找到");
        }
        
        if (placeHintText != null)
            placeHintText.gameObject.SetActive(true);
        
        InitializeInventory();
        
        // ===== 初始化观战 UI =====
        if (observerUI != null)
            observerUI.SetActive(false);
        
        if (observerPrevBtn != null)
            observerPrevBtn.onClick.AddListener(OnPrevObserver);
        if (observerNextBtn != null)
            observerNextBtn.onClick.AddListener(OnNextObserver);
    }
    
    void InitializeInventory()
    {
        foreach (var obj in slotObjects)
        {
            Destroy(obj);
        }
        slotIcons.Clear();
        slotHighlights.Clear();
        slotObjects.Clear();
        
        if (itemSlotPrefab == null || inventoryParent == null) return;
        
        for (int i = 0; i < maxSlots; i++)
        {
            GameObject slot = Instantiate(itemSlotPrefab, inventoryParent);
            slot.name = $"ItemSlot_{i}";
            
            Image icon = slot.transform.Find("ItemIcon")?.GetComponent<Image>();
            Image highlight = slot.transform.Find("Highlight")?.GetComponent<Image>();
            
            RectTransform rect = slot.GetComponent<RectTransform>();
            if (rect != null)
            {
                float spacing = slotSize + slotSpacing;
                float startX = -(maxSlots - 1) * spacing / 2f;
                rect.anchoredPosition = new Vector2(startX + i * spacing, 0);
                rect.sizeDelta = new Vector2(slotSize, slotSize);
            }
            
            slotIcons.Add(icon);
            slotHighlights.Add(highlight);
            slotObjects.Add(slot);
            
            if (highlight != null)
                highlight.gameObject.SetActive(false);
            
            slot.SetActive(false);
        }
    }
    
    public void UpdateInventory(IReadOnlyList<int> itemQueue)
    {
        if (itemQueue == null) return;
        
        int itemCount = itemQueue.Count;
        Debug.Log($"📦 物品数量: {itemCount}");
        
        for (int i = 0; i < maxSlots && i < slotIcons.Count; i++)
        {
            GameObject slot = slotObjects[i];
            if (slot == null) continue;
            
            Image icon = slotIcons[i];
            Image highlight = slotHighlights[i];
            
            if (i < itemCount)
            {
                slot.SetActive(true);
                
                int itemId = itemQueue[i];
                if (itemTable != null && itemTable.IsValid(itemId))
                {
                    ItemTable.Entry entry = itemTable.Get(itemId);
                    if (entry != null && entry.icon != null)
                    {
                        icon.sprite = entry.icon;
                        icon.color = Color.white;
                    }
                    else
                    {
                        icon.color = new Color(1, 1, 1, 0.3f);
                    }
                }
                else
                {
                    icon.color = new Color(1, 1, 1, 0.3f);
                }
                
                if (highlight != null)
                {
                    highlight.gameObject.SetActive(i == 0);
                }
            }
            else
            {
                slot.SetActive(false);
                
                if (highlight != null)
                {
                    highlight.gameObject.SetActive(false);
                }
            }
        }
    }
    
    // ==================== 更新躲藏者 UI（遵循契约） ====================
    public void UpdateHiderUI(IPlayerStateReadonly playerState, IReadOnlyList<IPlayerStateReadonly> allPlayers)
    {
        if (playerState == null) return;
        
        // 检查是否被捕获（遵循契约 HiderState.Captured）
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
        
        // 隐藏正常 UI
        if (placeHintText != null)
            placeHintText.gameObject.SetActive(false);
        if (inventoryParent != null)
            inventoryParent.gameObject.SetActive(false);
        if (transformTimer != null)
            transformTimer.gameObject.SetActive(false);
        
        // 状态文字改为 "⚫ 已捕获 - 观战中"
        if (hiderStateText != null)
        {
            hiderStateText.text = "⚫ 已捕获 - 观战中";
            hiderStateText.color = capturedColor;
        }
        if (disguiseStatusIcon != null)
            disguiseStatusIcon.color = capturedColor;
        
        // 收集存活的躲藏者（遵循契约 PlayerRole.Hider 和 HiderState.Captured）
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
                observerNameText.text = "无存活队友";
            if (observerStatusText != null)
                observerStatusText.text = "游戏结束";
        }
    }
    
    void ExitObserverMode()
    {
        isObserving = false;
        ShowObserverUI(false);
        
        // 恢复正常 UI
        if (placeHintText != null)
            placeHintText.gameObject.SetActive(true);
        if (inventoryParent != null)
            inventoryParent.gameObject.SetActive(true);
        if (transformTimer != null)
            transformTimer.gameObject.SetActive(true);
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
                observerNameText.text = "无存活队友";
            if (observerStatusText != null)
                observerStatusText.text = "游戏结束";
            return;
        }
        
        IPlayerStateReadonly target = aliveHiders[currentObserverIndex];
        if (target == null) return;
        
        if (observerNameText != null)
            observerNameText.text = target.PlayerName;
        
        if (observerStatusText != null)
        {
            bool isAlive = (target.HiderState != HiderState.Captured);
            observerStatusText.text = isAlive ? "🟢 存活" : "🔴 已捕获";
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
    
    // ==================== 更新形态状态（遵循契约） ====================
    void UpdateHiderState(HiderState state)
    {
        if (hiderStateText == null || disguiseStatusIcon == null) return;
        
        string stateText = "";
        Color stateColor = Color.white;
        
        // 遵循契约 HiderState 枚举
        switch (state)
        {
            case HiderState.Disguised:
                stateText = "🟢 伪装中";
                stateColor = disguisedColor;
                break;
            case HiderState.Invisible:
                stateText = "🟡 隐身无敌";
                stateColor = invisibleColor;
                break;
            case HiderState.Ghost:
                stateText = "🔴 鬼魂状态";
                stateColor = ghostColor;
                break;
            case HiderState.Captured:
                stateText = "⚫ 已捕获 - 观战中";
                stateColor = capturedColor;
                break;
            default:
                stateText = "🟢 伪装中";
                stateColor = disguisedColor;
                break;
        }
        
        hiderStateText.text = stateText;
        hiderStateText.color = stateColor;
        disguiseStatusIcon.color = stateColor;
    }
    
    public void UpdateTransformTimer(float timeLeft)
    {
        if (transformTimer == null) return;
        
        // 观战时不显示变身倒计时
        if (isObserving)
        {
            transformTimer.gameObject.SetActive(false);
            return;
        }
        
        if (timeLeft > 0)
        {
            transformTimer.text = $"⏳ 变身: {Mathf.CeilToInt(timeLeft)}s";
            transformTimer.color = Color.white;
            transformTimer.gameObject.SetActive(true);
        }
        else
        {
            transformTimer.text = "⏳ 准备变身...";
            transformTimer.color = Color.yellow;
            transformTimer.gameObject.SetActive(true);
        }
    }
    
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