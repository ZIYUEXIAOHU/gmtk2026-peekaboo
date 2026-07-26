using UnityEngine;

/// <summary>按帧播放 Sprite 序列，播完销毁。</summary>
public class OneShotSpriteEffect : MonoBehaviour
{
    [SerializeField] Sprite[] frames;
    [SerializeField] float framesPerSecond = 12f;
    [SerializeField] SpriteRenderer spriteRenderer;

    float elapsed;
    int index;
    bool playing;

    public void Play(Sprite[] sprites, float fps = 12f)
    {
        frames = sprites;
        framesPerSecond = Mathf.Max(1f, fps);
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        elapsed = 0f;
        index = 0;
        playing = frames != null && frames.Length > 0;
        if (playing && spriteRenderer != null)
            spriteRenderer.sprite = frames[0];
        else
            Destroy(gameObject);
    }

    void Update()
    {
        if (!playing) return;

        elapsed += Time.deltaTime;
        float frameDuration = 1f / framesPerSecond;
        int next = Mathf.FloorToInt(elapsed / frameDuration);
        if (next >= frames.Length)
        {
            Destroy(gameObject);
            return;
        }

        if (next != index)
        {
            index = next;
            spriteRenderer.sprite = frames[index];
        }
    }
}
