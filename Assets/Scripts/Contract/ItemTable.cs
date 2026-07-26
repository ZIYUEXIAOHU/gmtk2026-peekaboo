// ============================================================
// 契约文件：共享物品表
// itemId = items 列表索引。双方引用同一份资产（Assets/Resources/ItemTable.asset）。
// 修改规则：条目只增不删不重排，否则已同步的 itemId 会错位。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>物品尺寸档位（候选队列按大/中/小配额抽取）。</summary>
public enum ItemSize
{
    Large = 0,
    Middle = 1,
    Small = 2,
}

[CreateAssetMenu(fileName = "ItemTable", menuName = "Peekaboo/ItemTable")]
public class ItemTable : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string displayName;  // 物品名（UI 展示）
        public GameObject prefab;   // 场景外观（含碰撞体；必须预挂 NetworkIdentity 并注册为网络预制体）
        public Sprite icon;         // 物品栏图标
        public ItemSize size;       // 大 / 中 / 小
        [Tooltip("过高/过宽等不适合伪装时勾选；仍可进入物品栏并放置")]
        public bool excludeFromDisguise;
    }

    [Tooltip("itemId = 列表索引，只增不删不重排")]
    public List<Entry> items = new List<Entry>();

    public int Count => items.Count;
    public bool IsValid(int itemId) => itemId >= 0 && itemId < items.Count;
    public Entry Get(int itemId) => IsValid(itemId) ? items[itemId] : null;
}
