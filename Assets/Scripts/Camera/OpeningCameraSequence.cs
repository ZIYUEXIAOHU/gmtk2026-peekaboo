using UnityEngine;
using System.Collections;

/// <summary>
/// 主菜单开场聚焦 - 一次性镜头动画（带可视化聚焦点）
/// </summary>
public class OpeningCameraSequence : MonoBehaviour
{
    [Header("聚焦目标（可拖拽）")]
    public Transform focusTarget;              // 拖拽一个空物体或任何物体作为聚焦点
    
    [Header("聚焦偏移")]
    public Vector2 focusOffset = new Vector2(0, 1f);  // 相机相对于聚焦点的偏移
    
    [Header("时间")]
    public float startDelay = 0.5f;     // 延迟开始
    public float focusDuration = 1.5f;  // 聚焦时长
    public float holdDuration = 1.5f;   // 停留时长
    public float returnDuration = 1.0f; // 恢复时长
    
    [Header("聚焦缩放")]
    public float focusSize = 2.5f;      // 聚焦时的相机大小
    
    private Camera mainCamera;
    private Vector3 originalPos;
    private float originalSize;
    private bool hasPlayed = false;
    
    void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null) return;

        // 本地已有玩家名时跳过开场聚焦（仅首次引导输入名字时播放）
        string localName = PlayerPrefs.GetString(GameConstants.PlayerNamePrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(localName))
        {
            Debug.Log($"📷 本地已有玩家名「{localName}」，跳过开场摄像头聚焦");
            return;
        }
        
        // 如果没有指定聚焦点，自动创建一个
        if (focusTarget == null)
        {
            GameObject go = new GameObject("FocusPoint");
            focusTarget = go.transform;
            focusTarget.position = new Vector3(0, 0, 0);
            Debug.Log("🔧 自动创建聚焦点 FocusPoint，请在场景中调整位置");
        }
        
        // 保存原始设置
        originalPos = mainCamera.transform.position;
        originalSize = mainCamera.orthographicSize;
        
        // 开始运镜
        if (!hasPlayed)
        {
            hasPlayed = true;
            StartCoroutine(PlaySequence());
        }
    }
    
    IEnumerator PlaySequence()
    {
        yield return new WaitForSeconds(startDelay);
        
        if (focusTarget == null) yield break;
        
        // 目标位置 = 聚焦点 + 偏移
        Vector3 targetPos = new Vector3(
            focusTarget.position.x + focusOffset.x,
            focusTarget.position.y + focusOffset.y,
            originalPos.z
        );
        float targetSize = focusSize;
        
        // 1. 聚焦
        float elapsed = 0f;
        Vector3 startPos = mainCamera.transform.position;
        float startSize = mainCamera.orthographicSize;
        
        while (elapsed < focusDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / focusDuration;
            t = t * t * (3f - 2f * t);
            
            mainCamera.transform.position = Vector3.Lerp(startPos, targetPos, t);
            mainCamera.orthographicSize = Mathf.Lerp(startSize, targetSize, t);
            yield return null;
        }
        
        // 2. 停留
        yield return new WaitForSeconds(holdDuration);
        
        // 3. 恢复
        elapsed = 0f;
        Vector3 returnStartPos = mainCamera.transform.position;
        float returnStartSize = mainCamera.orthographicSize;
        
        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / returnDuration;
            t = t * t * (3f - 2f * t);
            
            mainCamera.transform.position = Vector3.Lerp(returnStartPos, originalPos, t);
            mainCamera.orthographicSize = Mathf.Lerp(returnStartSize, originalSize, t);
            yield return null;
        }
        
        mainCamera.transform.position = originalPos;
        mainCamera.orthographicSize = originalSize;
    }
    
    // ==================== 可视化 ====================
    
    void OnDrawGizmos()
    {
        if (focusTarget == null)
        {
            // 如果没有聚焦点，画一个默认位置
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(new Vector3(0, 0, 0), 0.5f);
            return;
        }
        
        // ===== 画聚焦点 =====
        Gizmos.color = Color.green;
        Vector3 center = focusTarget.position;
        Gizmos.DrawWireSphere(center, 0.4f);
        Gizmos.DrawLine(center - Vector3.right * 0.8f, center + Vector3.right * 0.8f);
        Gizmos.DrawLine(center - Vector3.up * 0.8f, center + Vector3.up * 0.8f);
        
        // ===== 画相机目标位置 =====
        Gizmos.color = Color.yellow;
        Vector3 cameraTarget = new Vector3(center.x + focusOffset.x, center.y + focusOffset.y, 0);
        Gizmos.DrawWireSphere(cameraTarget, 0.3f);
        Gizmos.DrawLine(center, cameraTarget);
        
        // ===== 画框（表示相机聚焦范围） =====
        Gizmos.color = new Color(1, 1, 0, 0.3f);
        float size = focusSize * 1.2f;
        Vector3 topLeft = cameraTarget + new Vector3(-size, size, 0);
        Vector3 topRight = cameraTarget + new Vector3(size, size, 0);
        Vector3 bottomLeft = cameraTarget + new Vector3(-size, -size, 0);
        Vector3 bottomRight = cameraTarget + new Vector3(size, -size, 0);
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        Gizmos.DrawLine(bottomLeft, topLeft);
    }
}