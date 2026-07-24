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
    public Transform inventoryParent;      // 物品栏父物体（直接放格子）
    public GameObject itemSlotPrefab;      // ItemSlotPrefab 预制体
    public int maxSlots = 5;
    public float slotSize = 50f;
    public float slotSpacing = 10f;
    
    [Header("颜色")]
    public Color disguisedColor = new Color(0.2f, 0.8f, 0.2f);
    public Color invisibleColor = new Color(1f, 0.8f, 0f);
    public Color ghostColor = new Color(1f, 0.3f, 0.3f);
    public Color capturedColor = new Color(0.5f, 0.5f, 0.5f);
    
    private List<Image> slotIcons = new List<Image>();
    private List<Image> slotHighlights = new List<Image>();
    private List<GameObject> slotObjects = new List<GameObject>();
    private ItemTable itemTable;
    
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
    
    // ==================== 更新躲藏者 UI ====================
    public void UpdateHiderUI(IPlayerStateReadonly playerState)
    {
        if (playerState == null) return;
        UpdateHiderState(playerState.HiderState);
        UpdateInventory(playerState.ItemQueue);
    }
    
    void UpdateHiderState(HiderState state)
    {
        if (hiderStateText == null || disguiseStatusIcon == null) return;
        
        string stateText = "";
        Color stateColor = Color.white;
        
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
                stateText = "⚫ 已捕获";
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
        
        if (timeLeft > 0)
        {
            transformTimer.text = $"⏳ 变身: {Mathf.CeilToInt(timeLeft)}s";
            transformTimer.color = Color.white;
        }
        else
        {
            transformTimer.text = "⏳ 准备变身...";
            transformTimer.color = Color.yellow;
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
        if (placeHintText != null)
            placeHintText.gameObject.SetActive(show);
    }
}