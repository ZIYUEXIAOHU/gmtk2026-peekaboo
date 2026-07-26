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
    const float EdgeThickness = 0.22f; // 相对较短边的比例

    static HiderInvestigateAlert instance;

    bool subscribed;
    IGameEvents boundEvents;
    CanvasGroup canvasGroup;
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

        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

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
        edgeImage.color = new Color(1f, 0.15f, 0.1f, 1f);

        edgeRect = edgeImage.rectTransform;
        edgeRect.anchorMin = new Vector2(0.5f, 0.5f);
        edgeRect.anchorMax = new Vector2(0.5f, 0.5f);
        edgeRect.pivot = new Vector2(1f, 0.5f);
        ResizeEdge();
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
            float a = Mathf.Pow(t, 1.6f);
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

    void ResizeEdge()
    {
        if (edgeRect == null) return;

        RectTransform canvasRt = edgeRect.parent as RectTransform;
        float screenW = canvasRt != null ? canvasRt.rect.width : Screen.width;
        float screenH = canvasRt != null ? canvasRt.rect.height : Screen.height;
        float halfDiag = Mathf.Sqrt(screenW * screenW + screenH * screenH) * 0.5f;
        float thickness = Mathf.Min(screenW, screenH) * EdgeThickness;

        edgeRect.sizeDelta = new Vector2(thickness, halfDiag * 2f);
        edgeRect.pivot = new Vector2(1f, 0.5f);
        float edgeDist = Mathf.Min(screenW, screenH) * 0.5f;
        edgeRect.anchoredPosition = new Vector2(edgeDist, 0f);
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
        ResizeEdge();

        float angle = Mathf.Atan2(worldDir.y, worldDir.x) * Mathf.Rad2Deg;
        if (edgeRect != null)
        {
            RectTransform canvasRt = edgeRect.parent as RectTransform;
            float screenW = canvasRt != null ? canvasRt.rect.width : Screen.width;
            float screenH = canvasRt != null ? canvasRt.rect.height : Screen.height;
            float edgeDist = Mathf.Min(screenW, screenH) * 0.5f;
            float rad = angle * Mathf.Deg2Rad;
            edgeRect.anchoredPosition = new Vector2(Mathf.Cos(rad) * edgeDist, Mathf.Sin(rad) * edgeDist);
            edgeRect.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        alertRemaining = AlertDuration;
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
    }
}
