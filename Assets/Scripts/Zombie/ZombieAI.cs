using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class ZombieAI : MonoBehaviour
{
    protected enum ZombieState
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        Dead
    }

    [Header("Target")]
    [SerializeField] protected Transform target;

    [Header("Detection")]
    [SerializeField] protected float detectionRange = 8f;
    [SerializeField] protected float loseTargetRange = 12f;

    [Header("Patrol")]
    [SerializeField] protected float patrolRadius = 6f;
    [SerializeField] protected float patrolWalkSpeed = 1.2f;
    [SerializeField] protected float idleTimeMin = 1.2f;
    [SerializeField] protected float idleTimeMax = 2.5f;

    [Header("Chase")]
    [SerializeField] protected float chaseWalkSpeed = 1.6f;
    [SerializeField] protected float runSpeed = 3.4f;
    [SerializeField] protected float runRampUpTime = 2.5f;

    [Header("Attack")]
    [SerializeField] protected float attackRange = 1.6f;
    [SerializeField] protected float attackCooldown = 1.2f;
    [SerializeField] protected int damage = 10;

    [Header("Animation")]
    [SerializeField] protected Animator animator;

    protected NavMeshAgent agent;
    protected PlayerHealth playerHealth;
    protected ZombieState currentState;
    protected Vector3 spawnPosition;

    private float idleTimer;
    private float currentIdleDuration;
    private float chaseTimer;
    private float lastAttackTime;

    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    protected static readonly int AttackHash = Animator.StringToHash("Attack");

    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        spawnPosition = transform.position;
    }

    protected virtual void Start()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (player != null)
            {
                target = player.transform;
            }
        }

        if (target != null)
        {
            playerHealth = target.GetComponent<PlayerHealth>();
        }

        EnterIdleState();
    }

    protected virtual void Update()
    {
        if (currentState == ZombieState.Dead)
        {
            return;
        }

        float distanceToPlayer = GetDistanceToPlayer();

        if (target != null && distanceToPlayer <= detectionRange)
        {
            if (distanceToPlayer <= attackRange)
            {
                EnterAttackState();
            }
            else
            {
                EnterChaseState();
            }
        }
        else if (currentState == ZombieState.Chase && distanceToPlayer > loseTargetRange)
        {
            EnterIdleState();
        }

        switch (currentState)
        {
            case ZombieState.Idle:
                UpdateIdleState();
                break;

            case ZombieState.Patrol:
                UpdatePatrolState();
                break;

            case ZombieState.Chase:
                UpdateChaseState();
                break;

            case ZombieState.Attack:
                UpdateAttackState();
                break;
        }

        UpdateMoveAnimation();
    }

    protected float GetDistanceToPlayer()
    {
        if (target == null)
        {
            return Mathf.Infinity;
        }

        return Vector3.Distance(transform.position, target.position);
    }

    protected virtual void EnterIdleState()
    {
        if (currentState == ZombieState.Dead)
        {
            return;
        }

        currentState = ZombieState.Idle;
        agent.isStopped = true;
        agent.speed = 0f;

        idleTimer = 0f;
        currentIdleDuration = Random.Range(idleTimeMin, idleTimeMax);
    }

    protected virtual void UpdateIdleState()
    {
        idleTimer += Time.deltaTime;

        if (idleTimer >= currentIdleDuration)
        {
            EnterPatrolState();
        }
    }

    protected virtual void EnterPatrolState()
    {
        if (currentState == ZombieState.Dead)
        {
            return;
        }

        currentState = ZombieState.Patrol;
        agent.isStopped = false;
        agent.speed = patrolWalkSpeed;

        Vector3 randomPoint = GetRandomPatrolPoint();
        agent.SetDestination(randomPoint);
    }

    protected virtual void UpdatePatrolState()
    {
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
        {
            EnterIdleState();
        }
    }

    private Vector3 GetRandomPatrolPoint()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomDirection = Random.insideUnitSphere * patrolRadius;
            randomDirection.y = 0f;

            Vector3 randomPosition = spawnPosition + randomDirection;

            if (NavMesh.SamplePosition(randomPosition, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        return spawnPosition;
    }

    protected virtual void EnterChaseState()
    {
        if (currentState == ZombieState.Dead)
        {
            return;
        }

        if (currentState != ZombieState.Chase)
        {
            chaseTimer = 0f;
        }

        currentState = ZombieState.Chase;
        agent.isStopped = false;
    }

    protected virtual void UpdateChaseState()
    {
        if (target == null)
        {
            EnterIdleState();
            return;
        }

        chaseTimer += Time.deltaTime;

        agent.speed = CalculateChaseSpeed(chaseTimer);
        agent.SetDestination(target.position);
    }

    protected virtual float CalculateChaseSpeed(float elapsedChaseTime)
    {
        float speedLerp = Mathf.Clamp01(elapsedChaseTime / runRampUpTime);
        return Mathf.Lerp(chaseWalkSpeed, runSpeed, speedLerp);
    }

    protected virtual void EnterAttackState()
    {
        if (currentState == ZombieState.Dead)
        {
            return;
        }

        currentState = ZombieState.Attack;
        agent.isStopped = true;
        agent.speed = 0f;
    }

    protected virtual void UpdateAttackState()
    {
        if (target == null)
        {
            EnterIdleState();
            return;
        }

        float distanceToPlayer = GetDistanceToPlayer();

        if (distanceToPlayer > attackRange + 0.3f)
        {
            EnterChaseState();
            return;
        }

        LookAtTarget();

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            lastAttackTime = Time.time;
            PerformAttack();
        }
    }

    protected virtual void PerformAttack()
    {
        if (animator != null)
        {
            animator.SetTrigger(AttackHash);
        }

        if (playerHealth != null)
        {
            playerHealth.TakeDamage(damage);
        }

        Debug.Log(GetType().Name + " attacked Player");
    }

    protected void LookAtTarget()
    {
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.01f)
        {
            return;
        }

        Quaternion lookRotation = Quaternion.LookRotation(direction);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            lookRotation,
            Time.deltaTime * 10f
        );
    }

    protected virtual void UpdateMoveAnimation()
    {
        if (animator == null)
        {
            return;
        }

        if (currentState == ZombieState.Idle ||
            currentState == ZombieState.Attack ||
            currentState == ZombieState.Dead)
        {
            animator.SetFloat(MoveSpeedHash, 0f);
            return;
        }

        float normalizedSpeed = Mathf.InverseLerp(0f, runSpeed, agent.velocity.magnitude);
        animator.SetFloat(MoveSpeedHash, normalizedSpeed);
    }

    public virtual void SetDead()
    {
        currentState = ZombieState.Dead;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Application.isPlaying ? spawnPosition : transform.position, patrolRadius);
    }
}
