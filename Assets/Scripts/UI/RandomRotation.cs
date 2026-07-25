using UnityEngine;

public class RandomRotation : MonoBehaviour
{
    [Header("旋转设置")]
    public float minAngle = -15f;       // 最小旋转角度
    public float maxAngle = 15f;        // 最大旋转角度
    
    [Header("入场动画")]
    public float animationDuration = 0.6f;  // 动画时长
    public float maxDelay = 0.3f;           // 最大随机延迟
    public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    private Quaternion startRotation;
    private Quaternion targetRotation;
    private float currentAngle = 0f;
    private float animationProgress = 0f;
    private float delay = 0f;
    private bool isAnimating = false;
    private bool isWaiting = true;
    
    void Start()
    {
        // 随机目标角度（-15 ~ +15 度）
        currentAngle = Random.Range(minAngle, maxAngle);
        targetRotation = Quaternion.Euler(0, 0, currentAngle);
        
        // 起始旋转（0度）
        startRotation = Quaternion.identity;
        
        // 初始状态（0度）
        transform.rotation = startRotation;
        
        // 随机延迟（0 ~ maxDelay）
        delay = Random.Range(0f, maxDelay);
        animationProgress = 0f;
        isAnimating = false;
        isWaiting = true;
    }
    
    void Update()
    {
        if (isWaiting)
        {
            delay -= Time.deltaTime;
            if (delay <= 0f)
            {
                isWaiting = false;
                isAnimating = true;
                animationProgress = 0f;
            }
            return;
        }
        
        if (!isAnimating) return;
        
        animationProgress += Time.deltaTime / animationDuration;
        
        if (animationProgress >= 1f)
        {
            animationProgress = 1f;
            isAnimating = false;
        }
        
        float t = curve.Evaluate(animationProgress);
        
        // 旋转插值（从 0 到目标角度）
        transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
    }
    
    /// <summary>
    /// 重新播放动画
    /// </summary>
    public void PlayAnimation()
    {
        currentAngle = Random.Range(minAngle, maxAngle);
        targetRotation = Quaternion.Euler(0, 0, currentAngle);
        
        delay = Random.Range(0f, maxDelay);
        animationProgress = 0f;
        isAnimating = false;
        isWaiting = true;
        transform.rotation = startRotation;
    }
    
    /// <summary>
    /// 应用随机旋转（无动画）
    /// </summary>
    public void ApplyRandomRotation()
    {
        currentAngle = Random.Range(minAngle, maxAngle);
        transform.rotation = Quaternion.Euler(0, 0, currentAngle);
        isAnimating = false;
        isWaiting = false;
    }
    
    /// <summary>
    /// 重置为0度
    /// </summary>
    public void ResetRotation()
    {
        transform.rotation = Quaternion.identity;
        currentAngle = 0f;
        isAnimating = false;
        isWaiting = false;
    }
}