using UnityEngine;

/// <summary>在世界坐标生成 Seeker 鼠标攻击特效（Resources/AttackEffect 五帧）。</summary>
public static class SeekerAttackEffect
{
    const string ResourcesPath = "AttackEffect";
    const float FramesPerSecond = 12f;
    /// <summary>原图约 3328px、PPU 512 ≈ 6.5 单位；缩放到约 3 单位宽。</summary>
    const float LocalScale = 0.5f;
    const int SortingOrder = 80;

    /// <summary>
    /// frame_04 不透明像素质心（归一化到 Sprite 矩形，原点左下）。
    /// 不是画布/pivot 中心；用于把视觉逻辑中心对准鼠标。
    /// </summary>
    static readonly Vector2 Frame4LogicalCenterNormalized = new Vector2(0.39921f, 0.31879f);

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
        go.transform.localScale = Vector3.one * LocalScale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = SortingOrder;

        var fx = go.AddComponent<OneShotSpriteEffect>();
        fx.Play(frames, FramesPerSecond);

        // 以第 4 帧内容逻辑中心补偿（非 bounds/画布中心）
        int pivotFrameIndex = Mathf.Min(3, frames.Length - 1);
        Vector3 localCenter = GetLogicalCenterLocal(frames[pivotFrameIndex], Frame4LogicalCenterNormalized);
        go.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f)
            - Vector3.Scale(localCenter, go.transform.localScale);

        // ===== 播放挥刀声（全玩家可听见） =====
        if (GameContract.IsAudioBound)
        {
            GameContract.Audio.PlaySlash(worldPosition);
        }
    }

    /// <summary>把归一化逻辑中心转成本地坐标（相对 Sprite pivot）。</summary>
    static Vector3 GetLogicalCenterLocal(Sprite sprite, Vector2 logicalCenterNormalized)
    {
        if (sprite == null)
            return Vector3.zero;

        float w = sprite.rect.width;
        float h = sprite.rect.height;
        float ppu = sprite.pixelsPerUnit;
        Vector2 pivot = sprite.pivot;
        return new Vector3(
            (logicalCenterNormalized.x * w - pivot.x) / ppu,
            (logicalCenterNormalized.y * h - pivot.y) / ppu,
            0f);
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