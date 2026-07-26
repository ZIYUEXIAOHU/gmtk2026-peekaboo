using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

/// <summary>
/// UI 画面聚焦 - 带速度曲线
/// </summary>
public class UIFocusAnimation : MonoBehaviour
{
    [Header("偏移方向")]
    public Vector2 offsetDirection = new Vector2(-100, 0);

    [Header("背景图片（支持多个）")]
    public List<RectTransform> backgroundImages;

    [Header("缩放")]
    public float focusScale = 1.15f;

    [Header("时间")]
    public float startDelay = 0.5f;
    public float focusDuration = 1.2f;
    public float holdDuration = 1.5f;
    public float returnDuration = 1.0f;

    [Header("速度曲线")]
    public AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private List<Vector3> originalScales = new List<Vector3>();
    private List<Vector3> originalPositions = new List<Vector3>();
    private bool hasPlayed = false;

    void Start()
    {
        if (backgroundImages == null || backgroundImages.Count == 0)
        {
            Debug.LogError("❌ 请拖入至少一个 Background Image");
            return;
        }

        // 本地已有玩家名时跳过聚焦（仅首次引导输入名字时播放）
        string localName = PlayerPrefs.GetString(GameConstants.PlayerNamePrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(localName))
        {
            Debug.Log($"📷 本地已有玩家名「{localName}」，跳过 UI 聚焦动画");
            return;
        }

        originalScales.Clear();
        originalPositions.Clear();

        foreach (var bg in backgroundImages)
        {
            if (bg != null)
            {
                originalScales.Add(bg.localScale);
                originalPositions.Add(bg.localPosition);
            }
            else
            {
                originalScales.Add(Vector3.one);
                originalPositions.Add(Vector3.zero);
            }
        }

        if (!hasPlayed)
        {
            hasPlayed = true;
            StartCoroutine(PlayFocusAnimation());
        }
    }

    IEnumerator PlayFocusAnimation()
    {
        yield return new WaitForSeconds(startDelay);

        if (backgroundImages == null || backgroundImages.Count == 0) yield break;

        Vector3 targetScale = Vector3.one * focusScale;

        // ===== 阶段 1：聚焦 =====
        float elapsed = 0f;

        List<Vector3> startPositions = new List<Vector3>();
        List<Vector3> startScales = new List<Vector3>();

        foreach (var bg in backgroundImages)
        {
            if (bg != null)
            {
                startPositions.Add(bg.localPosition);
                startScales.Add(bg.localScale);
            }
            else
            {
                startPositions.Add(Vector3.zero);
                startScales.Add(Vector3.one);
            }
        }

        while (elapsed < focusDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / focusDuration;
            float curveT = moveCurve.Evaluate(t);  // 使用曲线

            for (int i = 0; i < backgroundImages.Count; i++)
            {
                var bg = backgroundImages[i];
                if (bg == null) continue;

                Vector3 targetPos = originalPositions[i] + new Vector3(offsetDirection.x, offsetDirection.y, 0);
                bg.localPosition = Vector3.Lerp(startPositions[i], targetPos, curveT);
                bg.localScale = Vector3.Lerp(startScales[i], targetScale, curveT);
            }

            yield return null;
        }

        // 确保最终位置
        for (int i = 0; i < backgroundImages.Count; i++)
        {
            var bg = backgroundImages[i];
            if (bg == null) continue;

            bg.localPosition = originalPositions[i] + new Vector3(offsetDirection.x, offsetDirection.y, 0);
            bg.localScale = targetScale;
        }

        yield return new WaitForSeconds(holdDuration);

        // ===== 阶段 3：恢复 =====
        elapsed = 0f;

        List<Vector3> returnStartPositions = new List<Vector3>();
        List<Vector3> returnStartScales = new List<Vector3>();

        foreach (var bg in backgroundImages)
        {
            if (bg != null)
            {
                returnStartPositions.Add(bg.localPosition);
                returnStartScales.Add(bg.localScale);
            }
            else
            {
                returnStartPositions.Add(Vector3.zero);
                returnStartScales.Add(Vector3.one);
            }
        }

        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / returnDuration;
            float curveT = moveCurve.Evaluate(t);  // 使用曲线

            for (int i = 0; i < backgroundImages.Count; i++)
            {
                var bg = backgroundImages[i];
                if (bg == null) continue;

                bg.localPosition = Vector3.Lerp(returnStartPositions[i], originalPositions[i], curveT);
                bg.localScale = Vector3.Lerp(returnStartScales[i], originalScales[i], curveT);
            }

            yield return null;
        }

        // 确保完全恢复
        for (int i = 0; i < backgroundImages.Count; i++)
        {
            var bg = backgroundImages[i];
            if (bg == null) continue;

            bg.localPosition = originalPositions[i];
            bg.localScale = originalScales[i];
        }
    }
}