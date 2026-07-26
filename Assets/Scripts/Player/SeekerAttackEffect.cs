using UnityEngine;

/// <summary>在世界坐标生成 Seeker 鼠标攻击特效（Resources/AttackEffect 五帧）。</summary>
public static class SeekerAttackEffect
{
    const string ResourcesPath = "AttackEffect";
    const float FramesPerSecond = 12f;
    /// <summary>原图约 3328px、PPU 512 ≈ 6.5 单位；缩放到约 3 单位宽。</summary>
    const float LocalScale = 0.5f;
    const int SortingOrder = 80;

    static Sprite[] cachedFrames;

    public static void Spawn(Vector2 worldPosition)
    {
        Sprite[] frames = GetFrames();
        if (frames == null || frames.Length == 0)
        {
            Debug.LogWarning("[SeekerAttackEffect] Resources/AttackEffect 下未找到 Sprite 帧。");
            return;
        }

        var go = new GameObject("SeekerAttackEffect");
        go.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
        go.transform.localScale = Vector3.one * LocalScale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = SortingOrder;

        var fx = go.AddComponent<OneShotSpriteEffect>();
        fx.Play(frames, FramesPerSecond);
    }

    static Sprite[] GetFrames()
    {
        if (cachedFrames != null && cachedFrames.Length > 0)
            return cachedFrames;

        Sprite[] loaded = Resources.LoadAll<Sprite>(ResourcesPath);
        if (loaded == null || loaded.Length == 0)
            return null;

        System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
        cachedFrames = loaded;
        return cachedFrames;
    }
}
