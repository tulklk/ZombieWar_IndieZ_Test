using System.Collections;
using UnityEngine;

public class ZombieTank : ZombieAI
{
    [Header("Tank")]
    [SerializeField] private float attackWindup = 0.4f;

    protected override void PerformAttack()
    {
        if (animator != null)
        {
            animator.SetTrigger(AttackHash);
        }

        StartCoroutine(DelayedHit());
    }

    private IEnumerator DelayedHit()
    {
        yield return new WaitForSeconds(attackWindup);

        if (playerHealth != null && GetDistanceToPlayer() <= attackRange + 0.3f)
        {
            playerHealth.TakeDamage(damage);
        }
    }
}
