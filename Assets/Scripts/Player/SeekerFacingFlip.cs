using UnityEngine;

/// <summary>
/// 根据 Animator 的 FacingDirection 参数同步 SpriteRenderer.flipX，
/// 使远端客户端也能看到左右朝向（参数由 NetworkAnimator 同步）。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]
public class SeekerFacingFlip : MonoBehaviour
{
    static readonly int FacingDirectionHash = Animator.StringToHash("FacingDirection");

    SpriteRenderer spriteRenderer;
    Animator animator;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
    }

    void LateUpdate()
    {
        if (spriteRenderer == null || animator == null || !animator.isActiveAndEnabled)
            return;

        float facing = animator.GetFloat(FacingDirectionHash);
        if (Mathf.Abs(facing) > 0.01f)
            spriteRenderer.flipX = facing < 0f;
    }
}
