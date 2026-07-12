using UnityEngine;

/// <summary>
/// Unity dispatches Animation Events via SendMessage on the Animator's own GameObject only
/// (not parents/children). BigZombieAI lives on the gameplay root (alongside NavMeshAgent),
/// while the Animator lives on the visual model child, so this small relay forwards the
/// "OnAttackHit" event from the model up to BigZombieAI.
/// Attach this to the same GameObject as the Animator.
/// </summary>
public class BigZombieAttackEventRelay : MonoBehaviour
{
    [SerializeField] private BigZombieAI zombieAI;

    private void Awake()
    {
        if (zombieAI == null)
        {
            zombieAI = GetComponentInParent<BigZombieAI>();
        }
    }

    public void OnAttackHit()
    {
        zombieAI?.OnAttackHit();
    }
}
