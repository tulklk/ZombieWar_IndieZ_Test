using UnityEngine;

public class BigZombieAI : ZombieAI
{
    private const float AttackRangeTolerance = 0.3f;

    private bool hasDealtDamageThisAttack;

    protected override void PerformAttack()
    {
        hasDealtDamageThisAttack = false;

        if (animator != null)
        {
            animator.SetTrigger(AttackHash);
        }
    }

    /// <summary>
    /// Called via Animation Event on BigZombie_Attack, at the frame the hit connects.
    /// Only deals damage once per attack, and only if the target is still in range.
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
}
