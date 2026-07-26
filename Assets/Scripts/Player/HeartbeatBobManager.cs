using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 程序 2 表现：订阅 OnHeartbeatPulse，让探测/心跳圈内「躲藏者放置的可调查物」视觉节点跳动。
/// 节奏：跳起 → 落地 → 停 HeartbeatInterval±100ms → 再跳。
/// 用绝对节拍锚点调度，单次可抖 ±100ms，相对理想节拍累计偏差不超过 ±150ms。
/// 高度：半个玩家跳高，空中时间 T=√(8h/g) 与高度自洽。
/// 不带动伪装躲藏者本体。只动子节点 localPosition，不动根节点/刚体。
/// </summary>
public class HeartbeatBobManager : MonoBehaviour
{
    /// <summary>对齐 HiderController：jumpForce / gravityScale。</summary>
    const float RefJumpForce = 18f;
    const float RefGravityScale = 3f;

    /// <summary>
    /// 相对玩家跳高的比例。
    /// 1.0 ≈ 5.5 对放置物过大；按 0.4s 空中时长反推 ≈ 0.59 又过矮；取一半作可读的「跳」。
    /// </summary>
    const float HeightVsPlayerJump = 0.5f;

    /// <summary>超出最近一次脉冲后，仍视为「在圈内」的宽限（避免漏拍断节奏）。</summary>
    const float InRangeGraceMul = 1.5f;

    /// <summary>相对理想起跳时刻的单次随机抖动。</summary>
    const float GroundRestJitterSeconds = 0.1f;

    /// <summary>相对理想节拍的最大允许偏差（防止随机游走越积越大）。</summary>
    const float MaxPhaseDriftSeconds = 0.15f;

    static float EffectiveGravity =>
        Mathf.Abs(Physics2D.gravity.y) * RefGravityScale;

    /// <summary>玩家完整跳跃峰值 h = v²/(2g)。</summary>
    static float PlayerJumpHeight =>
        (RefJumpForce * RefJumpForce) / Mathf.Max(0.01f, 2f * EffectiveGravity);

    /// <summary>物品心跳跳峰值 = 半个玩家跳高。</summary>
    static float BobHeightWorld => PlayerJumpHeight * HeightVsPlayerJump;

    /// <summary>与高度、重力自洽的空中时长：T = √(8h/g)。</summary>
    static float BobDuration =>
        Mathf.Sqrt(8f * BobHeightWorld / Mathf.Max(0.01f, EffectiveGravity));

    /// <summary>理想起跳周期 = 空中时长 + 落地心跳间隔。</summary>
    static float CyclePeriod => BobDuration + GameConstants.HeartbeatInterval;

    static HeartbeatBobManager instance;
    bool subscribed;
    IGameEvents boundEvents;

    readonly Dictionary<Transform, BobState> activeBobs = new Dictionary<Transform, BobState>();
    readonly List<Transform> scratchKeys = new List<Transform>();

    /// <summary>落地后需等到该时刻才允许下一跳。</summary>
    readonly Dictionary<Transform, float> nextJumpAllowedAt = new Dictionary<Transform, float>();

    /// <summary>仍处于心跳影响范围内的截止时刻（由脉冲刷新）。</summary>
    readonly Dictionary<Transform, float> inRangeUntil = new Dictionary<Transform, float>();

    /// <summary>每物品的绝对节拍：首跳锚点 + 下一跳序号。</summary>
    readonly Dictionary<Transform, RhythmState> rhythms = new Dictionary<Transform, RhythmState>();

    struct BobState
    {
        public Vector3 restLocalPos;
        public float localBobHeight;
        public float elapsed;
    }

    struct RhythmState
    {
        public float origin;
        public int nextJumpIndex;
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
        TickScheduledJumps();
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
        float keepAlive = Time.time + GameConstants.HeartbeatInterval * InRangeGraceMul;

        foreach (InvestigableObject obj in FindObjectsOfType<InvestigableObject>())
        {
            if (obj == null) continue;
            if (obj.LinksToHider) continue;
            // 与探测椭圆一致（HeartbeatRadiusX/Y = InvestigateRangeX/Y）
            if (!GameConstants.IsInInvestigateRange(center, obj.transform.position))
                continue;

            Transform visual = ResolveItemVisual(obj.transform);
            if (visual == null) continue;

            inRangeUntil[visual] = keepAlive;
            TryStartBobIfReady(visual);
        }
    }

    /// <summary>圈内且已过落地停顿的物品，到点自动再跳（不依赖下一拍脉冲对齐）。</summary>
    void TickScheduledJumps()
    {
        if (inRangeUntil.Count == 0) return;

        scratchKeys.Clear();
        foreach (Transform visual in inRangeUntil.Keys)
            scratchKeys.Add(visual);

        float now = Time.time;
        for (int i = 0; i < scratchKeys.Count; i++)
        {
            Transform visual = scratchKeys[i];
            if (visual == null)
            {
                ClearItemState(visual);
                continue;
            }

            if (!inRangeUntil.TryGetValue(visual, out float until) || now > until)
            {
                inRangeUntil.Remove(visual);
                // 离开圈后清掉节拍锚点，下次进圈重新起拍，避免旧相位硬拽
                rhythms.Remove(visual);
                nextJumpAllowedAt.Remove(visual);
                continue;
            }

            TryStartBobIfReady(visual);
        }

        scratchKeys.Clear();
    }

    void TryStartBobIfReady(Transform visual)
    {
        if (visual == null) return;
        if (activeBobs.ContainsKey(visual)) return;
        if (nextJumpAllowedAt.TryGetValue(visual, out float allowedAt) && Time.time < allowedAt)
            return;

        float lossyY = Mathf.Abs(visual.lossyScale.y);
        float localHeight = lossyY > 0.0001f ? BobHeightWorld / lossyY : BobHeightWorld;

        // 首次起跳锚定绝对节拍；之后只按 origin + n*period 排程，避免抖动累积
        if (!rhythms.ContainsKey(visual))
        {
            rhythms[visual] = new RhythmState
            {
                origin = Time.time,
                nextJumpIndex = 1,
            };
        }

        activeBobs[visual] = new BobState
        {
            restLocalPos = visual.localPosition,
            localBobHeight = localHeight,
            elapsed = 0f,
        };
    }

    void ClearItemState(Transform visual)
    {
        inRangeUntil.Remove(visual);
        nextJumpAllowedAt.Remove(visual);
        rhythms.Remove(visual);
        activeBobs.Remove(visual);
    }

    /// <summary>
    /// 理想起跳 = origin + index * cycle；再加 ±100ms 抖动，并钳在理想±150ms 内。
    /// </summary>
    void ScheduleNextJump(Transform visual)
    {
        if (!rhythms.TryGetValue(visual, out RhythmState rhythm))
        {
            rhythm = new RhythmState
            {
                origin = Time.time - BobDuration,
                nextJumpIndex = 1,
            };
        }

        float ideal = rhythm.origin + rhythm.nextJumpIndex * CyclePeriod;
        float scheduled = ideal + Random.Range(-GroundRestJitterSeconds, GroundRestJitterSeconds);
        scheduled = Mathf.Clamp(scheduled, ideal - MaxPhaseDriftSeconds, ideal + MaxPhaseDriftSeconds);
        // 若已错过窗口，马上跳，下一拍仍按绝对锚点回正
        nextJumpAllowedAt[visual] = Mathf.Max(Time.time, scheduled);

        rhythm.nextJumpIndex++;
        rhythms[visual] = rhythm;
    }

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

    void TickBobs()
    {
        if (activeBobs.Count == 0) return;

        scratchKeys.Clear();
        foreach (Transform visual in activeBobs.Keys)
            scratchKeys.Add(visual);

        for (int i = 0; i < scratchKeys.Count; i++)
        {
            Transform visual = scratchKeys[i];
            if (!activeBobs.TryGetValue(visual, out BobState state))
                continue;

            if (visual == null)
            {
                ClearItemState(visual);
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
                ScheduleNextJump(visual);
            }
            else
            {
                activeBobs[visual] = state;
            }
        }

        scratchKeys.Clear();
    }
}
