// ============================================================
// 程序 1：可调查物体标记（场景预摆 + 放置物）
// 契约约定：可调查物必须挂 NetworkIdentity；本组件供权威裁定查找目标。
// hiderNetId：可选关联躲藏者（当前放置物为诱饵，一般为 InvalidNetId；
// 伪装本体躲藏者由 NetworkGameState 直接扫描 RoomPlayer，不依赖本字段）。
// ============================================================

using System.Collections;
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

    void Awake()
    {
        CollisionLayers.ConfigurePlacedItem(gameObject);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        CollisionLayers.ConfigurePlacedItem(gameObject);
        StartCoroutine(IgnoreOverlappingHidersBriefly());
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        CollisionLayers.ConfigurePlacedItem(gameObject);
        StartCoroutine(IgnoreOverlappingHidersBriefly());
    }

    [Server]
    public void ServerInit(int placedItemId, uint linkedHiderNetId = GameConstants.InvalidNetId)
    {
        itemId = placedItemId;
        hiderNetId = linkedHiderNetId;
        CollisionLayers.ConfigurePlacedItem(gameObject);
    }

    /// <summary>
    /// 放置点常与躲藏者重叠；短暂忽略以免生成瞬间把人弹飞。
    /// 在 server / 各 client 各自执行（Physics2D.IgnoreCollision 不联网）。
    /// </summary>
    IEnumerator IgnoreOverlappingHidersBriefly()
    {
        Collider2D selfCol = GetComponent<Collider2D>();
        if (selfCol == null)
            yield break;

        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(LayerMask.GetMask(CollisionLayers.Hider));
        filter.useLayerMask = true;

        Collider2D[] hits = new Collider2D[8];
        int count = selfCol.OverlapCollider(filter, hits);
        for (int i = 0; i < count; i++)
        {
            Collider2D other = hits[i];
            if (other == null || other == selfCol)
                continue;
            Physics2D.IgnoreCollision(selfCol, other, true);
        }

        yield return new WaitForSeconds(CollisionLayers.PlaceOverlapIgnoreSeconds);

        for (int i = 0; i < count; i++)
        {
            Collider2D other = hits[i];
            if (other == null || other == selfCol)
                continue;
            Physics2D.IgnoreCollision(selfCol, other, false);
        }
    }
}
