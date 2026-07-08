using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class ZombieAI : MonoBehaviour
{
    private enum ZombieState
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        Dead
    }

    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 8f;
    [SerializeField] private float loseTargetRange = 12f;

    [Header("Patrol")]
    [SerializeField] private float patrolRadius = 6f;
    [SerializeField] private float patrolWalkSpeed = 1.2f;
    [SerializeField] private float idleTimeMin = 1.2f;
    [SerializeField] private float idleTimeMax = 2.5f;

    [Header("Chase")]
    [SerializeField] private float chaseWalkSpeed = 1.6f;
    [SerializeField] private float runSpeed = 3.4f;
    [SerializeField] private float runRampUpTime = 2.5f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 1.6f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private int damage = 10;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private NavMeshAgent agent;
    private PlayerHealth playerHealth;

    private ZombieState currentState;
    private Vector3 spawnPosition;
    private float idleTimer;
    private float currentIdleDuration;
    private float chaseTimer;
    private float lastAttackTime;

    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int AttackHash = Animator.StringToHash("Attack");

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        spawnPosition = transform.position;
    }

    private void Start()
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

    private void Update()
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

    private float GetDistanceToPlayer()
    {
        if (target == null)
        {
            return Mathf.Infinity;
        }

        return Vector3.Distance(transform.position, target.position);
    }

    private void EnterIdleState()
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

    private void UpdateIdleState()
    {
        idleTimer += Time.deltaTime;

        if (idleTimer >= currentIdleDuration)
        {
            EnterPatrolState();
        }
    }

    private void EnterPatrolState()
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

    private void UpdatePatrolState()
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

    private void EnterChaseState()
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

    private void UpdateChaseState()
    {
        if (target == null)
        {
            EnterIdleState();
            return;
        }

        chaseTimer += Time.deltaTime;

        float speedLerp = Mathf.Clamp01(chaseTimer / runRampUpTime);
        agent.speed = Mathf.Lerp(chaseWalkSpeed, runSpeed, speedLerp);

        agent.SetDestination(target.position);
    }

    private void EnterAttackState()
    {
        if (currentState == ZombieState.Dead)
        {
            return;
        }

        currentState = ZombieState.Attack;
        agent.isStopped = true;
        agent.speed = 0f;
    }

    private void UpdateAttackState()
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

            if (animator != null)
            {
                animator.SetTrigger(AttackHash);
            }

            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
            }

            Debug.Log("Zombie attacked Player");
        }
    }

    private void LookAtTarget()
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

    private void UpdateMoveAnimation()
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

    public void SetDead()
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