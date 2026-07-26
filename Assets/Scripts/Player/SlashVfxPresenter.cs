using Mirror;
using UnityEngine;

/// <summary>
/// 订阅 OnSlashed，在 effectPosition 播鼠标攻击特效。
/// 本地攻击者已预测播放，跳过避免双份。
/// </summary>
public class SlashVfxPresenter : MonoBehaviour
{
    static SlashVfxPresenter instance;
    IGameEvents boundEvents;
    bool subscribed;

    public static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject(nameof(SlashVfxPresenter));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SlashVfxPresenter>();
    }

    void Update()
    {
        TrySubscribe();
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
        boundEvents.OnSlashed += OnSlashed;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed) return;
        if (boundEvents != null)
            boundEvents.OnSlashed -= OnSlashed;
        boundEvents = null;
        subscribed = false;
    }

    void OnSlashed(SlashInfo info)
    {
        if (IsLocalSeekerAttack(info.seekerNetId))
            return;

        SeekerAttackEffect.Spawn(info.effectPosition);
    }

    static bool IsLocalSeekerAttack(uint seekerNetId)
    {
        if (!NetworkClient.active || NetworkClient.localPlayer == null)
            return false;
        var rp = NetworkClient.localPlayer.GetComponent<RoomPlayer>();
        return rp != null && rp.netId == seekerNetId;
    }
}
