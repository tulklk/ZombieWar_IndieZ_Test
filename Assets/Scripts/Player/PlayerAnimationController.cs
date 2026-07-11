using UnityEngine;

public class PlayerAnimationController : MonoBehaviour
{
    [SerializeField] private Animator animator;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int IsShootingHash = Animator.StringToHash("IsShooting");
    private static readonly int DieHash = Animator.StringToHash("Die");

    private float currentMoveSpeed;

    public void SetMoveSpeed(float inputMagnitude, float speed)
    {
        currentMoveSpeed = Mathf.Lerp(currentMoveSpeed, speed, Time.deltaTime * 10f);
        animator.SetFloat(SpeedHash, currentMoveSpeed);
        animator.SetFloat(MotionSpeedHash, inputMagnitude);
    }

    public void SetShooting(bool isShooting)
    {
        animator.SetBool(IsShootingHash, isShooting);
    }

    public void PlayShootShot()
    {
        int upperBodyLayer = animator.GetLayerIndex("UpperBody");

        if (upperBodyLayer >= 0)
        {
            animator.Play("Shoot", upperBodyLayer, 0f);
        }
    }

    public void PlayReload()
    {
        currentMoveSpeed = 0f;
        animator.SetFloat(SpeedHash, 0f);
        animator.SetFloat(MotionSpeedHash, 0f);

        int actionLayer = animator.GetLayerIndex("ActionOverride");

        if (actionLayer >= 0)
        {
            animator.Play("Reload", actionLayer, 0f);
        }
    }

    public void EndReload()
    {
        int actionLayer = animator.GetLayerIndex("ActionOverride");

        if (actionLayer >= 0)
        {
            animator.Play("BombIdle", actionLayer, 0f);
        }
    }

    public void PlayDeath()
    {
        animator.SetBool(IsShootingHash, false);
        animator.SetTrigger(DieHash);

        int upperBodyLayer = animator.GetLayerIndex("UpperBody");
        if (upperBodyLayer >= 0)
        {
            animator.SetLayerWeight(upperBodyLayer, 0f);
        }
    }
}