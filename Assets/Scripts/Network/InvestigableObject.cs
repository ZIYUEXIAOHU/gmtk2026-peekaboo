// ============================================================
// 程序 1：可调查物体标记（场景预摆 + 放置物）
// 契约约定：可调查物必须挂 NetworkIdentity；本组件供权威裁定查找目标。
// hiderNetId：可选关联躲藏者（当前放置物为诱饵，一般为 InvalidNetId；
// 伪装本体躲藏者由 NetworkGameState 直接扫描 RoomPlayer，不依赖本字段）。
// ============================================================

using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
public class InvestigableObject : NetworkBehaviour
{
    [SyncVar]
    private int itemId = GameConstants.InvalidItemId;

    /// <summary>关联躲藏者 netId；诱饵/场景物为 InvalidNetId。</summary>
    [SyncVar]
    private uint hiderNetId = GameConstants.InvalidNetId;

    public int ItemId => itemId;
    public uint HiderNetId => hiderNetId;

    /// <summary>是否关联到存活伪装躲藏者（非诱饵）。</summary>
    public bool LinksToHider => hiderNetId != GameConstants.InvalidNetId;

    [Server]
    public void ServerInit(int placedItemId, uint linkedHiderNetId = GameConstants.InvalidNetId)
    {
        itemId = placedItemId;
        hiderNetId = linkedHiderNetId;
    }
}
