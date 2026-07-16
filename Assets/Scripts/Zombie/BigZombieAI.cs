using System.Collections;
using UnityEngine;

public class BigZombieAI : ZombieAI
{
    private const float AttackRangeTolerance = 0.3f;

    [Tooltip("Fallback timing for OnAttackHit if the Animation Event on the current attack clip never fires (seen with some third-party FBX clips, where events added via script don't reliably survive reimport) — matches roughly where the punch/swipe actually connects.")]
    [SerializeField] private float attackHitDelay = 0.5f;

    // "Attack2" is an optional second melee clip (e.g. a swipe, alongside the punch driving
    // "Attack") — harmless if the Animator Controller has no such trigger/state, since
    // SetTrigger on a missing parameter is a silent no-op in Unity.
    private static readonly int Attack2Hash = Animator.StringToHash("Attack2");

    private bool hasDealtDamageThisAttack;
    private Coroutine attackHitRoutine;

    protected override void PerformAttack()
    {
        hasDealtDamageThisAttack = false;

        if (animator != null)
        {
            int variantHash = Random.value < 0.5f ? AttackHash : Attack2Hash;
            animator.SetTrigger(variantHash);
        }

        if (attackHitRoutine != null)
        {
            StopCoroutine(attackHitRoutine);
        }

        attackHitRoutine = StartCoroutine(AttackHitDelayRoutine());
    }

    /// <summary>Guaranteed delivery for OnAttackHit — see attackHitDelay. hasDealtDamageThisAttack guards against a double-hit if the Animation Event also happens to fire.</summary>
    private IEnumerator AttackHitDelayRoutine()
    {
        yield return new WaitForSeconds(attackHitDelay);
        OnAttackHit();
        attackHitRoutine = null;
    }

    /// <summary>
    /// Called via Animation Event on the current attack clip (if it fires) AND, always, from
    /// AttackHitDelayRoutine's own timer — whichever runs first deals the damage, the other is
    /// a no-op via hasDealtDamageThisAttack. Only deals damage once per attack, and only if the
    /// target is still in range.
    /// </summary>
    public void OnAttackHit()
    {
        if (currentState == ZombieState.Dead) return;
        if (currentState != ZombieState.Attack) return;
        if (hasDealtDamageThisAttack) return;
        if (playerHealth == null || target == null) return;
        if (GetDistanceToPlayer() > attackRange + AttackRangeTolerance) return;

        hasDealtDamageThisAttack = true;
        playerHealth.TakeDamage(damage);
    }

    /// <summary>
    /// Overrides the base class's time-based ramp-up with a distance-based one — the Boss
    /// runs at full speed while the Player is still far off (closing the gap quickly reads as
    /// more threatening at range), then eases down to a slower, more deliberate walk as it
    /// closes in near attack range (a menacing final approach right before it can actually
    /// attack, instead of arriving at a full sprint). Ignores elapsedChaseTime entirely.
    /// </summary>
    protected override float CalculateChaseSpeed(float elapsedChaseTime)
    {
        float distance = GetDistanceToPlayer();
        float speedLerp = Mathf.Clamp01(Mathf.InverseLerp(attackRange, detectionRange, distance));
        return Mathf.Lerp(chaseWalkSpeed, runSpeed, speedLerp);
    }
}
