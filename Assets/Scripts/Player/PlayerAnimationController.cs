using UnityEngine;

public class PlayerAnimationController : MonoBehaviour
{
    [SerializeField] private Animator animator;

    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int IsShootingHash = Animator.StringToHash("IsShooting");

    private float currentMoveSpeed;

    public void SetMoveSpeed(float value)
    {
        currentMoveSpeed = Mathf.Lerp(currentMoveSpeed, value, Time.deltaTime * 10f);
        animator.SetFloat(MoveSpeedHash, currentMoveSpeed);
    }

    public void SetShooting(bool isShooting)
    {
        animator.SetBool(IsShootingHash, isShooting);
    }
}