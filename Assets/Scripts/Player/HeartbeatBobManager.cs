using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序 2 表现：订阅 OnHeartbeatPulse，让探测/心跳圈内「躲藏者放置的可调查物」视觉节点跳动。
/// 幅度与空中时长对齐躲藏者跳跃（jumpForce=18 / gravityScale=3）。不带动伪装躲藏者本体。
/// 只动子节点 localPosition，不动根节点/刚体。
/// </summary>
public class HeartbeatBobManager : MonoBehaviour
{
    /// <summary>对齐 HiderController：jumpForce=18、gravityScale=3。</summary>
    const float RefJumpForce = 18f;
    const float RefGravityScale = 3f;

    /// <summary>一次起落时长 ≈ 2 * v / g，与玩家跳跃空中时间接近。</summary>
    static float BobDuration =>
        2f * RefJumpForce / Mathf.Max(0.01f, Mathf.Abs(Physics2D.gravity.y) * RefGravityScale);

    /// <summary>峰值高度 = v² / (2g)，与玩家跳一样高。</summary>
    static float BobHeightWorld =>
        (RefJumpForce * RefJumpForce) / Mathf.Max(0.01f, 2f * Mathf.Abs(Physics2D.gravity.y) * RefGravityScale);

    static HeartbeatBobManager instance;
    bool subscribed;
    IGameEvents boundEvents;

    readonly Dictionary<Transform, BobState> activeBobs = new Dictionary<Transform, BobState>();
    readonly List<Transform> finishedKeys = new List<Transform>();

    struct BobState
    {
        public Vector3 restLocalPos;
        public float localBobHeight;
        public float elapsed;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject(nameof(HeartbeatBobManager));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HeartbeatBobManager>();
    }

    void Update()
    {
        TrySubscribe();
        TickBobs();
    }

    void OnDestroy()
    {
        Unsubscribe();
        if (instance == this)
            instance = null;
    }

    void TrySubscribe()
    {
        if (!GameContract.IsBound || GameContract.Events == null) return;
        if (subscribed && ReferenceEquals(boundEvents, GameContract.Events)) return;

        Unsubscribe();
        boundEvents = GameContract.Events;
        boundEvents.OnHeartbeatPulse += OnHeartbeatPulse;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed) return;
        if (boundEvents != null)
            boundEvents.OnHeartbeatPulse -= OnHeartbeatPulse;
        else if (GameContract.IsBound && GameContract.Events != null)
            GameContract.Events.OnHeartbeatPulse -= OnHeartbeatPulse;
        boundEvents = null;
        subscribed = false;
    }

    void OnHeartbeatPulse(HeartbeatPulse pulse)
    {
        Vector2 center = pulse.center;
        // 跳动范围 = 探测圈（与调查高亮一致）；pulse.radius 异常时回退常量
        float radius = pulse.radius > 0f ? pulse.radius : GameConstants.HeartbeatRadius;
        float investigateAligned = GameConstants.InvestigateRange;
        // 双保险：若服务端仍发旧半径，客户端按探测圈取较大者，避免「圈内不跳」
        radius = Mathf.Max(radius, investigateAligned);

        foreach (InvestigableObject obj in FindObjectsOfType<InvestigableObject>())
        {
            if (obj == null) continue;
            // 只跳放置物；关联躲藏者本体的标记物不在此跳动（本体也不跳）
            if (obj.LinksToHider) continue;
            if (Vector2.Distance(center, obj.transform.position) > radius) continue;
            Transform visual = ResolveItemVisual(obj.transform);
            if (visual != null)
                BeginBob(visual);
        }
    }

    /// <summary>
    /// 优先跳非根节点视觉；根上挂 SpriteRenderer 时创建 BobVisual 子节点并挪走贴图，
    /// 避免改根节点位置带动碰撞箱。
    /// </summary>
    static Transform ResolveItemVisual(Transform root)
    {
        if (root == null) return null;

        Transform bobVisual = root.Find("BobVisual");
        if (bobVisual != null)
            return bobVisual;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null) continue;
            if (sr.transform != root)
                return sr.transform;
        }

        SpriteRenderer rootSr = root.GetComponent<SpriteRenderer>();
        if (rootSr == null)
            return null;

        var go = new GameObject("BobVisual");
        Transform visual = go.transform;
        visual.SetParent(root, false);
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one;

        SpriteRenderer moved = go.AddComponent<SpriteRenderer>();
        CopySpriteRenderer(rootSr, moved);
        rootSr.enabled = false;
        return visual;
    }

    static void CopySpriteRenderer(SpriteRenderer from, SpriteRenderer to)
    {
        to.sprite = from.sprite;
        to.color = from.color;
        to.flipX = from.flipX;
        to.flipY = from.flipY;
        to.sortingLayerID = from.sortingLayerID;
        to.sortingOrder = from.sortingOrder;
        to.drawMode = from.drawMode;
        to.size = from.size;
        to.sharedMaterial = from.sharedMaterial;
    }

    void BeginBob(Transform visual)
    {
        if (visual == null) return;

        float lossyY = Mathf.Abs(visual.lossyScale.y);
        float localHeight = lossyY > 0.0001f ? BobHeightWorld / lossyY : BobHeightWorld;

        if (activeBobs.TryGetValue(visual, out BobState existing))
        {
            // 节拍重叠：从静止位重新起跳
            visual.localPosition = existing.restLocalPos;
            existing.elapsed = 0f;
            existing.localBobHeight = localHeight;
            activeBobs[visual] = existing;
            return;
        }

        activeBobs[visual] = new BobState
        {
            restLocalPos = visual.localPosition,
            localBobHeight = localHeight,
            elapsed = 0f,
        };
    }

    void TickBobs()
    {
        if (activeBobs.Count == 0) return;

        finishedKeys.Clear();
        foreach (Transform visual in activeBobs.Keys)
            finishedKeys.Add(visual);

        for (int i = 0; i < finishedKeys.Count; i++)
        {
            Transform visual = finishedKeys[i];
            if (!activeBobs.TryGetValue(visual, out BobState state))
                continue;

            if (visual == null)
            {
                activeBobs.Remove(visual);
                continue;
            }

            state.elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(state.elapsed / BobDuration);
            float y = Mathf.Sin(t * Mathf.PI) * state.localBobHeight;
            visual.localPosition = state.restLocalPos + new Vector3(0f, y, 0f);

            if (t >= 1f)
            {
                visual.localPosition = state.restLocalPos;
                activeBobs.Remove(visual);
            }
            else
            {
                activeBobs[visual] = state;
            }
        }

        finishedKeys.Clear();
    }
}
