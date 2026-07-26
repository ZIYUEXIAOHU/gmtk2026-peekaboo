using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 程序 2：变身波期间抓捕者视野遮蔽（全黑遮罩）。
/// 由本地 SeekerController 驱动，运行时自建 Overlay Canvas，无需改场景。
/// </summary>
public class SeekerBlackoutOverlay : MonoBehaviour
{
    const float FadeInDuration = 0.15f;
    const float FadeOutDuration = 0.25f;

    static SeekerBlackoutOverlay instance;

    CanvasGroup canvasGroup;
    bool visible;
    float targetAlpha;
    float fadeSpeed;

    public static SeekerBlackoutOverlay Ensure()
    {
        if (instance != null) return instance;

        var go = new GameObject(nameof(SeekerBlackoutOverlay));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SeekerBlackoutOverlay>();
        instance.BuildUi();
        return instance;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("BlackoutCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;

        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        canvasGroup = canvasGo.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var imageGo = new GameObject("Black");
        imageGo.transform.SetParent(canvasGo.transform, false);
        Image image = imageGo.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;

        RectTransform rt = image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (canvasGroup == null) return;

        float current = canvasGroup.alpha;
        if (Mathf.Approximately(current, targetAlpha))
        {
            canvasGroup.alpha = targetAlpha;
            return;
        }

        canvasGroup.alpha = Mathf.MoveTowards(current, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
    }

    /// <summary>开启黑屏（快速淡入）。</summary>
    public void Show()
    {
        visible = true;
        targetAlpha = 1f;
        fadeSpeed = 1f / FadeInDuration;
    }

    /// <summary>关闭黑屏（淡出）。</summary>
    public void Hide()
    {
        if (!visible && canvasGroup != null && canvasGroup.alpha <= 0.01f)
            return;

        visible = false;
        targetAlpha = 0f;
        fadeSpeed = 1f / FadeOutDuration;
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
