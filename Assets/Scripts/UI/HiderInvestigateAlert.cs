using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 程序 2：调查可视化警报。
/// Seeker 调查任意物品/玩家时，存活躲藏者屏幕边缘朝猎人方向红光一闪。
/// </summary>
public class HiderInvestigateAlert : MonoBehaviour
{
    const float AlertDuration = 0.6f;
    const float EdgeThicknessRatio = 0.18f; // 相对较短边

    static HiderInvestigateAlert instance;

    bool subscribed;
    IGameEvents boundEvents;
    CanvasGroup canvasGroup;
    RectTransform canvasRect;
    RectTransform edgeRect;
    Image edgeImage;
    float alertRemaining;
    Texture2D gradientTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject(nameof(HiderInvestigateAlert));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HiderInvestigateAlert>();
        instance.BuildUi();
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("AlertCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 850;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        canvasRect = canvasGo.GetComponent<RectTransform>();

        canvasGroup = canvasGo.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var edgeGo = new GameObject("DirectionalEdge");
        edgeGo.transform.SetParent(canvasGo.transform, false);
        edgeImage = edgeGo.AddComponent<Image>();
        edgeImage.raycastTarget = false;
        edgeImage.sprite = CreateGradientSprite();
        edgeImage.type = Image.Type.Simple;
        edgeImage.color = new Color(1f, 0.12f, 0.08f, 1f);

        edgeRect = edgeImage.rectTransform;
        // 锚点在画布中心；pivot 在条的外侧，贴边后条向屏幕内侧延伸
        edgeRect.anchorMin = new Vector2(0.5f, 0.5f);
        edgeRect.anchorMax = new Vector2(0.5f, 0.5f);
        edgeRect.pivot = new Vector2(1f, 0.5f);
        edgeRect.anchoredPosition = Vector2.zero;
        edgeRect.sizeDelta = new Vector2(100f, 200f);
    }

    Sprite CreateGradientSprite()
    {
        const int w = 64;
        const int h = 8;
        gradientTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        gradientTex.wrapMode = TextureWrapMode.Clamp;
        gradientTex.filterMode = FilterMode.Bilinear;

        for (int x = 0; x < w; x++)
        {
            // pivot 在 x=w（外侧）：外侧不透明，朝中心（x=0）透明
            float t = x / (float)(w - 1);
            float a = Mathf.Pow(t, 1.4f);
            Color c = new Color(1f, 1f, 1f, a);
            for (int y = 0; y < h; y++)
                gradientTex.SetPixel(x, y, c);
        }
        gradientTex.Apply();

        return Sprite.Create(
            gradientTex,
            new Rect(0, 0, w, h),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    void GetCanvasSize(out float width, out float height)
    {
        if (canvasRect != null)
        {
            // Overlay + Scaler 下 pixelRect 始终对应当前屏幕像素，再换算到 canvas 本地单位更稳
            Rect pixel = canvasRect.rect;
            if (pixel.width > 1f && pixel.height > 1f)
            {
                width = pixel.width;
                height = pixel.height;
                return;
            }
        }

        width = Screen.width;
        height = Screen.height;
    }

    /// <summary>
    /// 从画布中心沿 dir 射线，与屏幕矩形边界求交，得到贴边位置。
    /// </summary>
    static Vector2 EdgePointOnRect(Vector2 dir, float halfW, float halfH)
    {
        dir = dir.normalized;
        float sx = Mathf.Abs(dir.x) < 1e-5f ? float.PositiveInfinity : halfW / Mathf.Abs(dir.x);
        float sy = Mathf.Abs(dir.y) < 1e-5f ? float.PositiveInfinity : halfH / Mathf.Abs(dir.y);
        float t = Mathf.Min(sx, sy);
        return dir * t;
    }

    void Update()
    {
        TrySubscribe();

        if (alertRemaining > 0f)
        {
            alertRemaining -= Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(alertRemaining / AlertDuration);
            float alpha = t > 0.7f ? 1f : t / 0.7f;
            if (canvasGroup != null)
                canvasGroup.alpha = alpha;

            if (alertRemaining <= 0f && canvasGroup != null)
                canvasGroup.alpha = 0f;
        }
    }

    void OnDestroy()
    {
        Unsubscribe();
        if (gradientTex != null)
        {
            Destroy(gradientTex);
            gradientTex = null;
        }
        if (instance == this)
            instance = null;
    }

    void TrySubscribe()
    {
        if (!GameContract.IsBound || GameContract.Events == null) return;
        if (subscribed && ReferenceEquals(boundEvents, GameContract.Events)) return;

        Unsubscribe();
        boundEvents = GameContract.Events;
        boundEvents.OnInvestigated += OnInvestigated;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed) return;
        if (boundEvents != null)
            boundEvents.OnInvestigated -= OnInvestigated;
        else if (GameContract.IsBound && GameContract.Events != null)
            GameContract.Events.OnInvestigated -= OnInvestigated;
        boundEvents = null;
        subscribed = false;
    }

    void OnInvestigated(InvestigateInfo info)
    {
        if (!GameContract.IsBound || GameContract.State == null) return;

        IPlayerStateReadonly local = GameContract.State.LocalPlayer;
        if (local == null) return;
        if (local.Role != PlayerRole.Hider) return;
        if (local.HiderState == HiderState.Captured) return;

        Vector2 localPos = ResolveLocalPosition();
        Vector2 seekerPos = ResolveSeekerPosition(info);
        Vector2 dir = seekerPos - localPos;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.right;

        ShowAlert(dir.normalized);
    }

    static Vector2 ResolveLocalPosition()
    {
        NetworkIdentity local = NetworkClient.localPlayer;
        if (local != null)
            return local.transform.position;
        return Vector2.zero;
    }

    static Vector2 ResolveSeekerPosition(InvestigateInfo info)
    {
        if (info.seekerNetId != GameConstants.InvalidNetId
            && NetworkClient.spawned.TryGetValue(info.seekerNetId, out NetworkIdentity identity)
            && identity != null)
        {
            return identity.transform.position;
        }

        return info.noisePosition;
    }

    void ShowAlert(Vector2 worldDir)
    {
        if (edgeRect == null) return;

        GetCanvasSize(out float screenW, out float screenH);
        float halfW = screenW * 0.5f;
        float halfH = screenH * 0.5f;
        Vector2 d = worldDir.normalized;

        // 贴到屏幕矩形真正的边缘（不再用内切圆半径）
        Vector2 edgePos = EdgePointOnRect(d, halfW, halfH);

        float thickness = Mathf.Min(screenW, screenH) * EdgeThicknessRatio;
        // 条沿切线方向要足够长，才能盖住该侧边缘
        float length = Mathf.Max(screenW, screenH) * 1.15f;

        edgeRect.sizeDelta = new Vector2(thickness, length);
        edgeRect.pivot = new Vector2(1f, 0.5f);
        edgeRect.anchoredPosition = edgePos;
        edgeRect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);

        alertRemaining = AlertDuration;
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
    }
}
