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
    const float SyncPosEpsilonSqr = 0.0001f;

    [SyncVar]
    private int itemId = GameConstants.InvalidItemId;

    /// <summary>关联躲藏者 netId；诱饵/场景物为 InvalidNetId。</summary>
    [SyncVar]
    private uint hiderNetId = GameConstants.InvalidNetId;

    [SyncVar(hook = nameof(OnSyncedPositionChanged))]
    Vector2 syncedPosition;

    [SyncVar]
    Vector2 syncedVelocity;

    Rigidbody2D rb;

    public int ItemId => itemId;
    public uint HiderNetId => hiderNetId;

    /// <summary>是否关联到存活伪装躲藏者（非诱饵）。</summary>
    public bool LinksToHider => hiderNetId != GameConstants.InvalidNetId;

    void Awake()
    {
        CollisionLayers.ConfigurePlacedItem(gameObject);
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        CollisionLayers.ConfigurePlacedItem(gameObject);
        rb = GetComponent<Rigidbody2D>();

        // 纯客户端不本地模拟，跟随后端权威位置
        if (!isServer && rb != null)
            rb.bodyType = RigidbodyType2D.Kinematic;

        StartCoroutine(IgnoreOverlappingHidersBriefly());
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        CollisionLayers.ConfigurePlacedItem(gameObject);
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            syncedPosition = rb.position;
            syncedVelocity = rb.velocity;
        }

        StartCoroutine(IgnoreOverlappingHidersBriefly());
    }

    [Server]
    public void ServerInit(int placedItemId, uint linkedHiderNetId = GameConstants.InvalidNetId)
    {
        itemId = placedItemId;
        hiderNetId = linkedHiderNetId;
        CollisionLayers.ConfigurePlacedItem(gameObject);
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            syncedPosition = rb.position;
            syncedVelocity = Vector2.zero;
        }
    }

    void FixedUpdate()
    {
        if (rb == null)
            return;

        if (isServer)
        {
            Vector2 pos = rb.position;
            Vector2 vel = rb.velocity;
            if ((pos - syncedPosition).sqrMagnitude > SyncPosEpsilonSqr ||
                (vel - syncedVelocity).sqrMagnitude > SyncPosEpsilonSqr)
            {
                syncedPosition = pos;
                syncedVelocity = vel;
            }
            return;
        }

        // 客户端：插值跟随权威位姿
        rb.velocity = syncedVelocity;
        rb.MovePosition(Vector2.Lerp(rb.position, syncedPosition, 0.4f));
    }

    void OnSyncedPositionChanged(Vector2 _, Vector2 newPos)
    {
        if (isServer || rb == null)
            return;

        // 瞬移过大时直接贴合，避免插值拖尾
        if ((rb.position - newPos).sqrMagnitude > 4f)
            rb.position = newPos;
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
