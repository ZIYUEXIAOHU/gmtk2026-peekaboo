using UnityEngine;

/// <summary>
/// 原身毛团视觉漂浮：只改 Visual_Hider 的 localPosition，绕静止中心做轻微正弦运动。
/// 由 HiderDisguiseVisual 在原型态开启、伪装/被捕时关闭。
/// </summary>
public class HiderPrototypeFloat : MonoBehaviour
{
    [SerializeField] float amplitudeY = 0.06f;
    [SerializeField] float amplitudeX = 0.02f;
    [SerializeField] float period = 1.8f;

    Vector3 restLocalPos;

    void Awake()
    {
        restLocalPos = transform.localPosition;
    }

    void OnDisable()
    {
        transform.localPosition = restLocalPos;
    }

    void LateUpdate()
    {
        float p = Mathf.Max(0.01f, period);
        float t = Time.time * (Mathf.PI * 2f / p);
        transform.localPosition = restLocalPos + new Vector3(
            Mathf.Sin(t * 0.7f) * amplitudeX,
            Mathf.Sin(t) * amplitudeY,
            0f);
    }
}
