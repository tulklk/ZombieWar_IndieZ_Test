using UnityEngine;

public class PlayerAnimationController : MonoBehaviour
{
    [SerializeField] private Animator animator;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int IsShootingHash = Animator.StringToHash("IsShooting");
    private static readonly int DieHash = Animator.StringToHash("Die");

    private float currentMoveSpeed;

    private void Awake()
    {
        // UpperBody defaults to weight 1 in the controller — start it at 0 so the
        // (otherwise static) UpperIdle pose doesn't override Base Layer's own
        // Idle/Walk/Run arm swing before the player has ever fired a shot.
        int upperBodyLayer = animator.GetLayerIndex("UpperBody");

        if (upperBodyLayer >= 0)
        {
            animator.SetLayerWeight(upperBodyLayer, 0f);
        }
    }

    public void SetMoveSpeed(float inputMagnitude, float speed)
    {
        currentMoveSpeed = Mathf.Lerp(currentMoveSpeed, speed, Time.deltaTime * 10f);
        animator.SetFloat(SpeedHash, currentMoveSpeed);
        animator.SetFloat(MotionSpeedHash, inputMagnitude);
    }

    /// <summary>
    /// UpperBody only needs to override the arms while actually aiming/shooting — with
    /// its weight at 0 the rest of the time, Base Layer's own Idle/Walk/Run clips (which
    /// already animate the arms correctly) show through untouched instead of being
    /// frozen into UpperIdle's static pose.
    /// </summary>
    public void SetShooting(bool isShooting)
    {
        animator.SetBool(IsShootingHash, isShooting);

        int upperBodyLayer = animator.GetLayerIndex("UpperBody");

        if (upperBodyLayer >= 0)
        {
            animator.SetLayerWeight(upperBodyLayer, isShooting ? 1f : 0f);
        }
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