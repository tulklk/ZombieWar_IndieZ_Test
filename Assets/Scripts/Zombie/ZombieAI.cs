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

    [Header("Data (optional — overrides every field below if assigned)")]
    [Tooltip("Assign a ZombieData asset (Assets/ScriptableObjects/Zombies) to drive this zombie's stats from one shared, reusable asset instead of editing them inline per prefab.")]
    [SerializeField] protected ZombieData zombieData;

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

    [Header("AI Performance")]
    [Tooltip("How often (seconds) detection + NavMesh destination refresh while near the player.")]
    [SerializeField] protected float nearTickInterval = 0.2f;
    [Tooltip("How often (seconds) detection + NavMesh destination refresh while far from the player.")]
    [SerializeField] protected float farTickInterval = 0.7f;
    [Tooltip("Distance under which the faster (near) tick interval is used.")]
    [SerializeField] protected float nearDistanceThreshold = 10f;

    [Header("Animation")]
    [SerializeField] protected Animator animator;

    [Header("Audio")]
    [Tooltip("Played once, positionally, the moment this zombie spots the player (not on every attack).")]
    [SerializeField] protected AudioClip attackSfx;
    [Tooltip("Auto-added if left empty. 3D-spatialized so the sound fades out with distance.")]
    [SerializeField] protected AudioSource audioSource;

    private const float AlertSfxMinDistance = 3f;
    private const float AlertSfxMaxDistance = 20f;

    protected NavMeshAgent agent;
    protected PlayerHealth playerHealth;
    protected ZombieState currentState;
    protected Vector3 spawnPosition;
    protected bool isAiPaused;

    /// <summary>Read-only stat accessors — e.g. for BossIntroUI to display Damage/Speed/Attack Range without a second, duplicate data source.</summary>
    public int Damage => damage;
    public float AttackRange => attackRange;
    public float DetectionRange => detectionRange;
    public float RunSpeed => runSpeed;
    public ZombieData Data => zombieData;

    private float idleTimer;
    private float currentIdleDuration;
    private float chaseTimer;
    private float lastAttackTime;
    private float nextAiTick;
    private bool hasAlertedPlayer;

    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    protected static readonly int AttackHash = Animator.StringToHash("Attack");

    protected virtual void Awake()
    {
        if (zombieData != null)
        {
            ApplyZombieData(zombieData);
        }

        agent = GetComponent<NavMeshAgent>();

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = AlertSfxMinDistance;
        audioSource.maxDistance = AlertSfxMaxDistance;

        spawnPosition = transform.position;

        // Stagger the first tick so zombies spawned in the same frame don't all
        // re-check detection/destination on the same frame afterward either.
        nextAiTick = Time.time + Random.Range(0f, nearTickInterval);
    }

    /// <summary>Copies zombieData's values onto this instance's own fields — overridden by ZombieTank to also apply attackWindup.</summary>
    protected virtual void ApplyZombieData(ZombieData data)
    {
        detectionRange = data.detectionRange;
        loseTargetRange = data.loseTargetRange;
        patrolRadius = data.patrolRadius;
        patrolWalkSpeed = data.patrolWalkSpeed;
        idleTimeMin = data.idleTimeMin;
        idleTimeMax = data.idleTimeMax;
        chaseWalkSpeed = data.chaseWalkSpeed;
        runSpeed = data.runSpeed;
        runRampUpTime = data.runRampUpTime;
        attackRange = data.attackRange;
        attackCooldown = data.attackCooldown;
        damage = data.damage;
        nearTickInterval = data.nearTickInterval;
        farTickInterval = data.farTickInterval;
        nearDistanceThreshold = data.nearDistanceThreshold;

        if (data.attackSfx != null)
        {
            attackSfx = data.attackSfx;
        }
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
        if (currentState == ZombieState.Dead || isAiPaused)
        {
            return;
        }

        if (Time.time >= nextAiTick)
        {
            RunAiTick();
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

    /// <summary>
    /// Detection and NavMesh destination refresh run on a randomized interval instead of
    /// every frame — SetDestination triggers a NavMesh path query, and re-checking a few
    /// times a second reads as instant in a top-down shooter while cutting most of the cost.
    /// </summary>
    private void RunAiTick()
    {
        float distanceToPlayer = GetDistanceToPlayer();
        float interval = distanceToPlayer <= nearDistanceThreshold ? nearTickInterval : farTickInterval;
        nextAiTick = Time.time + interval + Random.Range(-0.05f, 0.05f);

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

        if (currentState == ZombieState.Chase && target != null && IsAgentUsable)
        {
            agent.SetDestination(target.position);
        }
    }

    /// <summary>
    /// A NavMeshAgent can end up "not on a NavMesh" (spawn point too far from any baked
    /// surface, or SetDead() disabled it) — writing to isStopped/speed/SetDestination on
    /// such an agent throws. Every state-changing call below goes through this guard.
    /// </summary>
    protected bool IsAgentUsable => agent != null && agent.isOnNavMesh;

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
        hasAlertedPlayer = false;

        if (IsAgentUsable)
        {
            agent.isStopped = true;
            agent.speed = 0f;
        }

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

        if (!IsAgentUsable)
        {
            return;
        }

        agent.isStopped = false;
        agent.speed = patrolWalkSpeed;

        Vector3 randomPoint = GetRandomPatrolPoint();
        agent.SetDestination(randomPoint);
    }

    protected virtual void UpdatePatrolState()
    {
        // A zombie whose spawn point isn't near any baked NavMesh never gets placed on one
        // (agent.isOnNavMesh stays false) — EnterPatrolState() still sets currentState to
        // Patrol regardless (matching every other Enter*State method), so this still runs
        // every tick for that zombie. Reading remainingDistance/pathPending on an agent
        // that was never placed on a NavMesh throws, so it must be guarded the same way
        // every other agent-touching Update*State method in this class already is.
        if (!IsAgentUsable)
        {
            return;
        }

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
        PlayAlertSfxOnce();

        if (IsAgentUsable)
        {
            agent.isStopped = false;
        }
    }

    protected virtual void UpdateChaseState()
    {
        if (target == null)
        {
            EnterIdleState();
            return;
        }

        chaseTimer += Time.deltaTime;

        if (!IsAgentUsable)
        {
            return;
        }

        // Destination itself is refreshed by RunAiTick(), not every frame — this only
        // keeps the speed ramp-up smooth while already-set path is being followed.
        agent.speed = CalculateChaseSpeed(chaseTimer);
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
        PlayAlertSfxOnce();

        if (IsAgentUsable)
        {
            agent.isStopped = true;
            agent.speed = 0f;
        }
    }

    protected virtual void UpdateAttackState()
    {
        if (target == null)
        {
            EnterIdleState();
            return;
        }

        float maxAttackDistance = attackRange + 0.3f;
        float sqrDistanceToPlayer = (target.position - transform.position).sqrMagnitude;

        if (sqrDistanceToPlayer > maxAttackDistance * maxAttackDistance)
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

    /// <summary>
    /// Plays attackSfx once per detection episode, positioned on this zombie so it
    /// naturally gets louder as the player approaches and fades out moving away —
    /// reset by EnterIdleState() once the zombie loses the player again.
    /// </summary>
    protected void PlayAlertSfxOnce()
    {
        if (hasAlertedPlayer)
        {
            return;
        }

        hasAlertedPlayer = true;

        bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

        if (attackSfx != null && audioSource != null && sfxEnabled)
        {
            audioSource.PlayOneShot(attackSfx);
        }
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

    /// <summary>
    /// Lets a spawner (e.g. ZombieWaveManager) assign the target explicitly right after
    /// Instantiate, so Start()'s own GameObject.FindGameObjectWithTag("Player") fallback
    /// (which only runs when target is still null) never fires.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        playerHealth = newTarget != null ? newTarget.GetComponent<PlayerHealth>() : null;
    }

    /// <summary>
    /// Overrides attackRange/detectionRange directly on this instance, independent of
    /// zombieData — e.g. for a boss scaled up well beyond its prefab's own authored size,
    /// whose reach needs to grow to match or melee attacks silently miss despite looking
    /// adjacent on screen. Left as a plain setter (not scale-aware itself) so callers decide
    /// how the new values are derived.
    /// </summary>
    public void SetAttackAndDetectionRange(float newAttackRange, float newDetectionRange)
    {
        attackRange = newAttackRange;
        detectionRange = newDetectionRange;
    }

    /// <summary>Overrides damage directly on this instance — e.g. BossFightManager boosting a Boss's damage on entering Phase 2.</summary>
    public void SetDamage(int newDamage)
    {
        damage = newDamage;
    }

    /// <summary>Overrides the chase-speed cap directly on this instance — e.g. BossFightManager boosting a Boss's speed on entering Phase 2. Takes effect immediately since CalculateChaseSpeed reads this field every frame, never a cached copy.</summary>
    public void SetRunSpeed(float newRunSpeed)
    {
        runSpeed = newRunSpeed;
    }

    /// <summary>
    /// Freezes/resumes this zombie's whole state machine (used by BossFightManager during
    /// the boss intro cutscene) without touching currentState the way SetDead() does — so
    /// resuming picks back up exactly where it left off instead of forcing Idle. While
    /// paused the NavMeshAgent is stopped and its velocity zeroed so a boss doesn't keep
    /// drifting on its last heading; MoveSpeed is reset to 0 so it holds an idle pose.
    /// </summary>
    public virtual void SetAIEnabled(bool isEnabled)
    {
        isAiPaused = !isEnabled;

        if (isAiPaused)
        {
            if (IsAgentUsable)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            if (animator != null)
            {
                animator.SetFloat(MoveSpeedHash, 0f);
            }
        }
        else if (IsAgentUsable && currentState != ZombieState.Idle && currentState != ZombieState.Attack)
        {
            agent.isStopped = false;
        }
    }

    public virtual void SetDead()
    {
        currentState = ZombieState.Dead;

        if (IsAgentUsable)
        {
            agent.isStopped = true;
        }

        if (agent != null)
        {
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
