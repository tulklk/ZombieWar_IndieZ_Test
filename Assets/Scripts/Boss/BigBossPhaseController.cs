using System.Collections;
using Cinemachine;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

public enum BigBossPhase
{
    PhaseOne,
    Transitioning,
    PhaseTwo,
    Dead
}

/// <summary>
/// Extends BigZombieAI/ZombieAI/ZombieHealth with a 2-phase Boss encounter — never replaces
/// them. Phase 1 is a slow, short-range melee brute; crossing phaseTwoTriggerHealthPercent (or
/// exhausting Phase 1) starts a scripted, invulnerable transition (Roar, SFX, camera shake,
/// VFX, UI) after which Phase 2 applies faster/stronger stats and unlocks a throw attack. The
/// Boss can only ever die once Phase 2's own health pool reaches 0 — HandleBossHealthChanged
/// clamps any lethal hit taken during Phase 1/Transitioning back up to 1 HP, synchronously,
/// before ZombieHealth.TakeDamage's own "currentHealth &lt;= 0 -&gt; Die()" check re-reads the
/// field, so Died correctly never fires until Phase 2's pool is actually exhausted.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class BigBossPhaseController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ZombieHealth bossHealth;
    [SerializeField] private ZombieAI bossAI;
    [SerializeField] private NavMeshAgent navMeshAgent;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform player;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private BossHealthUI bossHealthUI;
    [Tooltip("Not called directly — BossFightManager already subscribes to ZombieHealth.Died on its own. Kept as a reference for Editor tooling/future extension.")]
    [SerializeField] private BossFightManager bossFightManager;

    [Header("Phase One (heavy brute — melee only)")]
    [SerializeField] private int phaseOneMaxHealth = 500;
    [SerializeField] private float phaseOneMoveSpeed = 2.8f;
    [SerializeField] private float phaseOneRunSpeed = 3.5f;
    [SerializeField] private float phaseOneAcceleration = 6f;
    [SerializeField] private int phaseOneMeleeDamage = 25;
    [SerializeField] private float phaseOneAttackRange = 2.8f;
    [SerializeField] private float phaseOneAttackCooldown = 1.8f;
    [SerializeField] private float phaseOneAngularSpeed = 90f;
    [Tooltip("How far the Boss notices the Player from — independent of attackRange (which only floors this as a minimum). A large Boss should read the Player from well outside melee reach, not just once they're already adjacent.")]
    [SerializeField] private float phaseOneDetectionRange = 14f;

    [Header("Phase Transition")]
    [SerializeField, Range(0.05f, 1f)] private float phaseTwoTriggerHealthPercent = 0.35f;
    [SerializeField] private float transitionDuration = 2.5f;
    [Tooltip("No dedicated PhaseTwo animation state exists in this project's Animator Controller — the transition reuses this same Roar trigger the Intro already relies on, rather than adding a new state.")]
    [SerializeField] private string roarTriggerName = "Roar";

    [Header("Phase Two (enraged — faster, stronger, throws objects)")]
    [SerializeField] private int phaseTwoMaxHealth = 850;
    [SerializeField] private bool refillHealthAtPhaseTwo = true;
    [SerializeField] private float phaseTwoMoveSpeed = 4.2f;
    [SerializeField] private float phaseTwoRunSpeed = 5.5f;
    [SerializeField] private float phaseTwoAcceleration = 10f;
    [SerializeField] private int phaseTwoMeleeDamage = 40;
    [SerializeField] private float phaseTwoAttackRange = 4.2f;
    [SerializeField] private float phaseTwoAttackCooldown = 0.8f;
    [SerializeField] private float phaseTwoAngularSpeed = 150f;
    [SerializeField] private float phaseTwoDetectionRange = 18f;

    [Header("Boss Physical Size (never hard-coded for one specific scale)")]
    [Tooltip("Attack ranges above never shrink below the Boss's own measured CapsuleCollider radius plus this margin, so a Boss scaled well beyond the suggested defaults still has a workable melee range.")]
    [SerializeField] private float meleeReachMargin = 2.5f;

    [Header("Throw Attack (Phase 2 only)")]
    [SerializeField] private bool enableThrowAttackInPhaseTwo = true;
    [SerializeField] private GameObject throwableProjectilePrefab;
    [SerializeField] private Transform throwPoint;
    [Tooltip("Used for the throw's line-of-sight raycast — a separate point (e.g. head height) from throwPoint (hand height) so a partial-cover check reads more like \"can the Boss see the Player\" than \"can the Boss's hand see the Player.\"")]
    [SerializeField] private Transform projectileDetectionOrigin;
    [SerializeField] private float minimumThrowDistance = 5f;
    [SerializeField] private float maximumThrowDistance = 15f;
    [SerializeField] private float throwCooldown = 4f;
    [SerializeField] private int throwDamage = 35;
    [SerializeField] private float projectileSpeed = 13f;
    [SerializeField] private float projectileArcHeight = 4f;
    [SerializeField] private float impactRadius = 2.5f;
    [SerializeField] private float throwWindupDuration = 0.7f;
    [Tooltip("No dedicated Throw animation exists in this asset pack — reuses the existing 'Attack' trigger by default. Change this once a real Throw clip/trigger is added to the Animator Controller.")]
    [SerializeField] private string throwTriggerName = "Attack";
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("Optional — leave at 'Nothing' to skip line-of-sight blocking entirely. Assign the Environment/Wall layer(s) to stop the Boss throwing through solid geometry.")]
    [SerializeField] private LayerMask obstacleLayerMask;
    [SerializeField, Range(0f, 1f)] private float throwAttackChance = 0.4f;
    [SerializeField] private int maximumActiveProjectiles = 2;
    [SerializeField] private GameObject impactIndicatorPrefab;

    [Header("Jump Attack (Phase 2 only — a melee gap-closer for the band between melee reach and minimumThrowDistance)")]
    [SerializeField] private bool enableJumpAttackInPhaseTwo = true;
    [SerializeField] private string jumpAttackTriggerName = "JumpAttack";
    [SerializeField] private float jumpAttackMinDistance = 3f;
    [SerializeField] private float jumpAttackMaxDistance = 7f;
    [SerializeField] private float jumpAttackCooldown = 6f;
    [SerializeField] private int jumpAttackDamage = 45;
    [SerializeField] private float jumpAttackImpactRadius = 2.5f;
    [SerializeField] private float jumpAttackWindupDuration = 0.4f;
    [SerializeField] private float jumpAttackDuration = 0.5f;
    [SerializeField] private float jumpAttackArcHeight = 2f;
    [SerializeField] private AudioClip jumpAttackImpactClip;

    [Header("Phase Transition Extras")]
    [Tooltip("Played partway through the transition, after the Roar — an optional muscle-flex beat for extra emphasis. Leave the trigger name empty to skip.")]
    [SerializeField] private string flexTriggerName = "Flex";

    [Header("Phase Two Visual")]
    [SerializeField] private GameObject phaseTwoVFX;
    [SerializeField] private Renderer[] bossRenderers;
    [SerializeField] private Color phaseTwoTint = new Color(1f, 0.25f, 0.15f);
    [SerializeField] private float tintDuration = 0.35f;

    [Header("Audio")]
    [SerializeField] private AudioSource bossAudioSource;
    [SerializeField] private AudioClip phaseTransitionClip;
    [SerializeField] private AudioClip roarClip;
    [SerializeField] private AudioClip throwClip;
    [SerializeField] private AudioClip projectileImpactClip;

    [Header("Camera")]
    [SerializeField] private CinemachineImpulseSource transitionImpulse;
    [SerializeField] private float transitionShakeForce = 0.35f;
    [Tooltip("A second, lighter impulse fired shortly after the first — reads as a heavier 'double punch' roar instead of one flat shake.")]
    [SerializeField] private float secondaryShakeDelay = 0.25f;
    [SerializeField] private float secondaryShakeForceMultiplier = 0.6f;

    [Header("Roar Punch (no new animation asset needed — sells the 'angry' beat with timing/scale/screen effects)")]
    [Tooltip("Briefly scales the Boss's model (never the root — leaves NavMeshAgent/Collider scale untouched) up and back down, in sync with the Roar.")]
    [SerializeField] private float modelScalePunchAmount = 0.12f;
    [SerializeField] private float modelScalePunchDuration = 0.3f;
    [Tooltip("Optional full-screen flash (e.g. a red-tinted Image under GameCanvas) that punches in and fades out at the exact Roar moment. Leave empty to skip.")]
    [SerializeField] private CanvasGroup screenFlashCanvasGroup;
    [SerializeField] private float screenFlashDuration = 0.4f;

    [Header("UI")]
    [SerializeField] private TMP_Text phaseText;
    [SerializeField] private CanvasGroup phaseAnnouncementCanvasGroup;
    [SerializeField] private TMP_Text phaseTitleText;
    [SerializeField] private TMP_Text phaseSubtitleText;
    [SerializeField] private float phaseAnnouncementDuration = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    private const float ThrowDecisionTickInterval = 0.35f;
    private const float ThrowRecoveryDuration = 0.4f;
    private const float MaxPredictionTime = 0.6f;
    private const float JumpAttackRecoveryDuration = 0.2f;

    private static readonly int TintColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Collider[] JumpAttackOverlapBuffer = new Collider[8];

    private bool hasTransitioned;
    private float nextThrowReadyTime;
    private float nextThrowDecisionTick;
    private int activeProjectileCount;
    private bool hasSpawnedThisThrow;
    private Vector3 pendingThrowTarget;
    private Vector3 lastPlayerPosition;
    private Vector3 playerVelocityEstimate;
    private bool hasLastPlayerPosition;
    private float nextJumpAttackReadyTime;
    private Coroutine transitionRoutine;
    private Coroutine throwRoutine;
    private Coroutine jumpAttackRoutine;
    private Coroutine phaseAnnouncementRoutine;
    private Coroutine scalePunchRoutine;
    private Coroutine screenFlashRoutine;
    private GameObject activeIndicatorInstance;
    private MaterialPropertyBlock tintPropertyBlock;
    private Vector3 modelBaseScale = Vector3.one;
    private bool hasCachedModelBaseScale;

    public BigBossPhase CurrentPhase { get; private set; } = BigBossPhase.PhaseOne;
    public bool IsTransitioning => CurrentPhase == BigBossPhase.Transitioning;

    public bool CanUseJumpAttack =>
        enableJumpAttackInPhaseTwo
        && CurrentPhase == BigBossPhase.PhaseTwo
        && jumpAttackRoutine == null
        && throwRoutine == null
        && Time.time >= nextJumpAttackReadyTime;

    public bool CanUseThrowAttack =>
        enableThrowAttackInPhaseTwo
        && CurrentPhase == BigBossPhase.PhaseTwo
        && throwRoutine == null
        && jumpAttackRoutine == null
        && Time.time >= nextThrowReadyTime
        && activeProjectileCount < maximumActiveProjectiles;

    private void Awake()
    {
        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null)
        {
            modelBaseScale = animator.transform.localScale;
            hasCachedModelBaseScale = true;
        }

        if (bossHealth == null)
        {
            bossHealth = GetComponent<ZombieHealth>();
        }

        if (bossAI == null)
        {
            bossAI = GetComponent<ZombieAI>();
        }

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
            {
                player = playerObject.transform;

                if (playerHealth == null)
                {
                    playerHealth = playerObject.GetComponent<PlayerHealth>();
                }
            }
        }

        if (player != null)
        {
            lastPlayerPosition = player.position;
            hasLastPlayerPosition = true;
        }

        if (bossHealth != null)
        {
            bossHealth.HealthChanged += HandleBossHealthChanged;
            bossHealth.Died += HandleBossDied;
        }
    }

    private void OnDestroy()
    {
        if (bossHealth != null)
        {
            bossHealth.HealthChanged -= HandleBossHealthChanged;
            bossHealth.Died -= HandleBossDied;
        }
    }

    private void OnDisable()
    {
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }

        if (throwRoutine != null)
        {
            StopCoroutine(throwRoutine);
            throwRoutine = null;
        }

        if (jumpAttackRoutine != null)
        {
            StopCoroutine(jumpAttackRoutine);
            jumpAttackRoutine = null;

            if (navMeshAgent != null)
            {
                navMeshAgent.enabled = true;
            }
        }

        if (scalePunchRoutine != null)
        {
            StopCoroutine(scalePunchRoutine);
            scalePunchRoutine = null;

            if (animator != null && hasCachedModelBaseScale)
            {
                animator.transform.localScale = modelBaseScale;
            }
        }

        if (screenFlashRoutine != null)
        {
            StopCoroutine(screenFlashRoutine);
            screenFlashRoutine = null;

            if (screenFlashCanvasGroup != null)
            {
                screenFlashCanvasGroup.alpha = 0f;
                screenFlashCanvasGroup.gameObject.SetActive(false);
            }
        }
    }

    private void Update()
    {
        TrackPlayerVelocity();

        if (CurrentPhase != BigBossPhase.PhaseTwo)
        {
            return;
        }

        if (throwRoutine != null || jumpAttackRoutine != null || Time.time < nextThrowDecisionTick)
        {
            return;
        }

        nextThrowDecisionTick = Time.time + ThrowDecisionTickInterval;

        // Jump Attack fills the gap band between melee reach and minimumThrowDistance —
        // checked first so the Boss never just stands there waiting for Throw's own minimum
        // range while the Player sits in that middle zone.
        if (TryUseJumpAttack())
        {
            return;
        }

        if (enableThrowAttackInPhaseTwo)
        {
            TryUseThrowAttack();
        }
    }

    private void TrackPlayerVelocity()
    {
        if (player == null)
        {
            return;
        }

        if (!hasLastPlayerPosition)
        {
            lastPlayerPosition = player.position;
            hasLastPlayerPosition = true;
            return;
        }

        if (Time.deltaTime > 0.0001f)
        {
            playerVelocityEstimate = (player.position - lastPlayerPosition) / Time.deltaTime;
        }

        lastPlayerPosition = player.position;
    }

    /// <summary>Call once the Boss fight actually begins (e.g. from BossFightManager.BeginBossFight) — applies Phase 1 stats/health and resets all phase state for a fresh fight.</summary>
    public void InitializeBoss()
    {
        CurrentPhase = BigBossPhase.PhaseOne;
        hasTransitioned = false;
        activeProjectileCount = 0;
        nextThrowReadyTime = 0f;

        bossHealth?.SetInvulnerable(false);

        ApplyPhaseOneStats();

        Log($"Boss initialized — Phase 1, {phaseOneMaxHealth} HP.");
    }

    public void ApplyPhaseOneStats()
    {
        if (bossHealth != null)
        {
            bossHealth.SetMaxHealth(phaseOneMaxHealth, refill: true);
        }

        ApplyStatsToAI(phaseOneMoveSpeed, phaseOneRunSpeed, phaseOneAcceleration, phaseOneMeleeDamage, phaseOneAttackRange, phaseOneAttackCooldown, phaseOneAngularSpeed, phaseOneDetectionRange);

        SetPhaseLabel("PHASE 1");
    }

    public void ApplyPhaseTwoStats()
    {
        if (bossHealth != null)
        {
            bossHealth.SetMaxHealth(phaseTwoMaxHealth, refillHealthAtPhaseTwo);
        }

        ApplyStatsToAI(phaseTwoMoveSpeed, phaseTwoRunSpeed, phaseTwoAcceleration, phaseTwoMeleeDamage, phaseTwoAttackRange, phaseTwoAttackCooldown, phaseTwoAngularSpeed, phaseTwoDetectionRange);

        SetPhaseLabel("PHASE 2 - ENRAGED");

        nextThrowReadyTime = Time.time + throwCooldown * 0.5f;

        Log($"Phase 2 stats applied — {phaseTwoMaxHealth} HP, damage {phaseTwoMeleeDamage}, range {phaseTwoAttackRange:0.0}.");
    }

    private void ApplyStatsToAI(float moveSpeed, float runSpeed, float acceleration, int damage, float attackRange, float attackCooldown, float angularSpeed, float detectionRange)
    {
        float flooredAttackRange = Mathf.Max(attackRange, GetEffectiveColliderRadius() + meleeReachMargin);

        bossAI?.ApplyCombatStats(moveSpeed, runSpeed, acceleration, damage, flooredAttackRange, attackCooldown, detectionRange);

        if (navMeshAgent != null)
        {
            navMeshAgent.angularSpeed = angularSpeed;
        }
    }

    private float GetEffectiveColliderRadius()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();

        if (capsule == null)
        {
            return 0f;
        }

        Vector3 lossyScale = transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
        return capsule.radius * horizontalScale;
    }

    /// <summary>
    /// Subscribed to ZombieHealth.HealthChanged, which fires BEFORE TakeDamage's own lethal
    /// check. Two jobs: (1) while not yet in Phase 2, clamp any hit that would carry health
    /// below 0 back up to 1, so the Boss can never actually die during Phase 1/Transitioning;
    /// (2) watch for the Phase 2 trigger threshold and fire the transition exactly once.
    /// </summary>
    private void HandleBossHealthChanged(int current, int max)
    {
        if (CurrentPhase == BigBossPhase.Dead || max <= 0)
        {
            return;
        }

        if (CurrentPhase != BigBossPhase.PhaseTwo && current <= 0)
        {
            bossHealth.SetCurrentHealth(1);
            current = 1;
        }

        if (CurrentPhase == BigBossPhase.PhaseOne && !hasTransitioned)
        {
            float ratio = (float)current / max;

            if (ratio <= phaseTwoTriggerHealthPercent)
            {
                TryTriggerPhaseTwo();
            }
        }
    }

    public void TryTriggerPhaseTwo()
    {
        if (hasTransitioned || CurrentPhase != BigBossPhase.PhaseOne)
        {
            return;
        }

        hasTransitioned = true;
        StartPhaseTwoTransition();
    }

    /// <summary>Manual override for testing (e.g. wired to a debug key/button) — skips the health-threshold check entirely.</summary>
    public void ForcePhaseTwoForTesting()
    {
        if (CurrentPhase != BigBossPhase.PhaseOne)
        {
            return;
        }

        hasTransitioned = true;
        StartPhaseTwoTransition();
    }

    public void StartPhaseTwoTransition()
    {
        if (transitionRoutine != null)
        {
            return;
        }

        transitionRoutine = StartCoroutine(PhaseTwoTransitionRoutine());
    }

    private IEnumerator PhaseTwoTransitionRoutine()
    {
        CurrentPhase = BigBossPhase.Transitioning;
        Log("Phase transition starting.");

        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.velocity = Vector3.zero;
        }

        bossAI?.CancelCurrentAttack();
        bossAI?.SetAIEnabled(false);
        bossHealth?.SetInvulnerable(true);

        FaceTarget();

        PlayTrigger(roarTriggerName);
        PlayOneShotSafe(phaseTransitionClip);
        PlayOneShotSafe(roarClip);

        if (phaseTwoVFX != null)
        {
            phaseTwoVFX.SetActive(true);
        }

        ApplyRendererTint(phaseTwoTint);
        PlayRoarPunchEffects();
        ShowPhaseAnnouncement("PHASE 2", "ENRAGED");

        // Roar plays first; Flex (an optional extra emphasis beat, e.g. "mutant flexing
        // muscles") fires roughly halfway through the transition window, once Roar's own
        // Any-State transition has had time to actually finish playing.
        float roarPortion = transitionDuration * 0.5f;
        float flexPortion = transitionDuration - roarPortion;

        if (roarPortion > 0f)
        {
            yield return new WaitForSecondsRealtime(roarPortion);
        }

        PlayTrigger(flexTriggerName);

        if (flexPortion > 0f)
        {
            yield return new WaitForSecondsRealtime(flexPortion);
        }

        ApplyPhaseTwoStats();

        bossAI?.SetAIEnabled(true);
        bossHealth?.SetInvulnerable(false);

        CurrentPhase = BigBossPhase.PhaseTwo;
        Log("Phase 2 active.");

        transitionRoutine = null;
    }

    private void FaceTarget()
    {
        if (player == null)
        {
            return;
        }

        Vector3 lookDirection = player.position - transform.position;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(lookDirection);
        }
    }

    public bool TryUseThrowAttack()
    {
        if (!CanUseThrowAttack || player == null || throwPoint == null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= phaseTwoAttackRange || distance < minimumThrowDistance || distance > maximumThrowDistance)
        {
            return false;
        }

        if (!HasLineOfSightToPlayer())
        {
            return false;
        }

        if (Random.value > throwAttackChance)
        {
            return false;
        }

        throwRoutine = StartCoroutine(ThrowAttackRoutine());
        return true;
    }

    /// <summary>Closes the gap between melee reach and Throw's own minimum range with a short leap — otherwise the Boss would just stand there whenever the Player sits in that middle band.</summary>
    public bool TryUseJumpAttack()
    {
        if (!CanUseJumpAttack || player == null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance < jumpAttackMinDistance || distance > jumpAttackMaxDistance)
        {
            return false;
        }

        jumpAttackRoutine = StartCoroutine(JumpAttackRoutine());
        return true;
    }

    private IEnumerator JumpAttackRoutine()
    {
        nextJumpAttackReadyTime = Time.time + jumpAttackCooldown;

        bossAI?.SetAIEnabled(false);

        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.velocity = Vector3.zero;
        }

        FaceTarget();
        PlayTrigger(jumpAttackTriggerName);

        if (jumpAttackWindupDuration > 0f)
        {
            yield return new WaitForSeconds(jumpAttackWindupDuration);
        }

        Vector3 startPosition = transform.position;
        Vector3 landPosition = ComputeJumpAttackLandingPosition(startPosition);

        bool agentWasEnabled = navMeshAgent != null && navMeshAgent.enabled;

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = false;
        }

        float elapsed = 0f;
        float duration = Mathf.Max(jumpAttackDuration, 0.1f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float arc = Mathf.Sin(t * Mathf.PI) * jumpAttackArcHeight;
            transform.position = Vector3.Lerp(startPosition, landPosition, t) + Vector3.up * arc;
            yield return null;
        }

        transform.position = landPosition;

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = agentWasEnabled;

            if (agentWasEnabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.Warp(landPosition);
            }
        }

        DealJumpAttackDamage(landPosition);

        if (JumpAttackRecoveryDuration > 0f)
        {
            yield return new WaitForSeconds(JumpAttackRecoveryDuration);
        }

        if (CurrentPhase == BigBossPhase.PhaseTwo)
        {
            bossAI?.SetAIEnabled(true);
        }

        jumpAttackRoutine = null;
    }

    /// <summary>Lands just short of the Player's current position (never on top of them) so the impact radius still has to actually catch a Player who doesn't dodge.</summary>
    private Vector3 ComputeJumpAttackLandingPosition(Vector3 startPosition)
    {
        if (player == null)
        {
            return startPosition;
        }

        Vector3 toPlayer = player.position - startPosition;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude <= 0.01f)
        {
            return startPosition;
        }

        float travelDistance = Mathf.Max(toPlayer.magnitude - meleeReachMargin, 0f);
        Vector3 landPosition = startPosition + toPlayer.normalized * travelDistance;
        landPosition.y = startPosition.y;
        return landPosition;
    }

    private void DealJumpAttackDamage(Vector3 impactPosition)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(impactPosition, jumpAttackImpactRadius, JumpAttackOverlapBuffer, playerLayer, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = JumpAttackOverlapBuffer[i];

            if (hitCollider == null)
            {
                continue;
            }

            PlayerHealth hitPlayerHealth = hitCollider.GetComponentInParent<PlayerHealth>();

            if (hitPlayerHealth != null && !hitPlayerHealth.IsDead)
            {
                hitPlayerHealth.TakeDamage(jumpAttackDamage);
            }
        }

        PlayOneShotSafe(jumpAttackImpactClip);
        Log($"Jump attack landed at {impactPosition}.");
    }

    private bool HasLineOfSightToPlayer()
    {
        if (obstacleLayerMask.value == 0 || player == null)
        {
            return true;
        }

        Transform origin = projectileDetectionOrigin != null ? projectileDetectionOrigin : throwPoint;

        if (origin == null)
        {
            return true;
        }

        Vector3 targetPoint = player.position + Vector3.up;
        Vector3 direction = targetPoint - origin.position;
        float distance = direction.magnitude;

        if (distance <= 0.05f)
        {
            return true;
        }

        return !Physics.Raycast(origin.position, direction.normalized, distance, obstacleLayerMask, QueryTriggerInteraction.Ignore);
    }

    private IEnumerator ThrowAttackRoutine()
    {
        nextThrowReadyTime = Time.time + throwCooldown;
        pendingThrowTarget = PredictPlayerPosition();
        hasSpawnedThisThrow = false;

        bossAI?.SetAIEnabled(false);

        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.velocity = Vector3.zero;
        }

        FaceTarget();
        PlayTrigger(throwTriggerName);
        ShowImpactIndicator(pendingThrowTarget);

        Log("Throw attack starting.");

        if (throwWindupDuration > 0f)
        {
            yield return new WaitForSeconds(throwWindupDuration);
        }

        if (CurrentPhase == BigBossPhase.PhaseTwo)
        {
            SpawnThrowableProjectile();
        }

        yield return new WaitForSeconds(ThrowRecoveryDuration);

        if (CurrentPhase == BigBossPhase.PhaseTwo)
        {
            bossAI?.SetAIEnabled(true);
        }

        throwRoutine = null;
    }

    private Vector3 PredictPlayerPosition()
    {
        if (player == null)
        {
            return transform.position;
        }

        Vector3 originPosition = throwPoint != null ? throwPoint.position : transform.position;
        float distance = Vector3.Distance(originPosition, player.position);
        float travelTime = Mathf.Clamp(distance / Mathf.Max(projectileSpeed, 1f), 0f, MaxPredictionTime);

        Vector3 predicted = player.position + playerVelocityEstimate * travelTime;

        if (Physics.Raycast(predicted + Vector3.up * 5f, Vector3.down, out RaycastHit groundHit, 15f, groundLayer, QueryTriggerInteraction.Ignore))
        {
            predicted.y = groundHit.point.y;
        }

        return predicted;
    }

    private void ShowImpactIndicator(Vector3 groundPosition)
    {
        if (impactIndicatorPrefab == null)
        {
            return;
        }

        if (activeIndicatorInstance != null)
        {
            Destroy(activeIndicatorInstance);
        }

        activeIndicatorInstance = Instantiate(impactIndicatorPrefab, groundPosition, Quaternion.identity);
        ThrowImpactIndicator indicator = activeIndicatorInstance.GetComponent<ThrowImpactIndicator>();
        indicator?.PlayFadeIn(throwWindupDuration);

        Destroy(activeIndicatorInstance, throwWindupDuration + 0.5f);
    }

    /// <summary>
    /// Spawns the thrown projectile toward the position predicted at the start of this throw.
    /// Deliberately parameterless so it can ALSO be wired directly as an Animation Event on a
    /// future dedicated Throw clip's exact release frame — hasSpawnedThisThrow guards against
    /// spawning twice if both the Animation Event and this routine's own timeout fire.
    /// </summary>
    public void SpawnThrowableProjectile()
    {
        if (hasSpawnedThisThrow)
        {
            return;
        }

        if (throwableProjectilePrefab == null || throwPoint == null)
        {
            Log("throwableProjectilePrefab or throwPoint not assigned — skipping this throw (melee still works).", true);
            return;
        }

        if (activeProjectileCount >= maximumActiveProjectiles)
        {
            return;
        }

        GameObject projectileObject = Instantiate(throwableProjectilePrefab, throwPoint.position, Quaternion.identity);
        BossThrowableProjectile projectile = projectileObject.GetComponent<BossThrowableProjectile>();

        if (projectile == null)
        {
            Log("throwableProjectilePrefab has no BossThrowableProjectile component — destroying it.", true);
            Destroy(projectileObject);
            return;
        }

        Collider bossCollider = GetComponent<Collider>();
        Collider projectileCollider = projectileObject.GetComponent<Collider>();

        if (bossCollider != null && projectileCollider != null)
        {
            Physics.IgnoreCollision(projectileCollider, bossCollider);
        }

        hasSpawnedThisThrow = true;
        activeProjectileCount++;
        projectile.Resolved += HandleProjectileResolved;
        projectile.Launch(throwPoint.position, pendingThrowTarget, projectileSpeed, projectileArcHeight, throwDamage, impactRadius);

        PlayOneShotSafe(throwClip);

        Log($"Projectile spawned toward {pendingThrowTarget}.");
    }

    private void HandleProjectileResolved(BossThrowableProjectile projectile)
    {
        projectile.Resolved -= HandleProjectileResolved;
        activeProjectileCount = Mathf.Max(0, activeProjectileCount - 1);
    }

    private void HandleBossDied(ZombieHealth deadHealth)
    {
        if (CurrentPhase == BigBossPhase.Dead)
        {
            return;
        }

        CurrentPhase = BigBossPhase.Dead;

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }

        if (throwRoutine != null)
        {
            StopCoroutine(throwRoutine);
            throwRoutine = null;
        }

        if (activeIndicatorInstance != null)
        {
            Destroy(activeIndicatorInstance);
            activeIndicatorInstance = null;
        }

        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
        }

        Invoke(nameof(DisablePhaseTwoVFX), 0.5f);

        Log("Boss died in Phase 2.");
    }

    private void DisablePhaseTwoVFX()
    {
        if (phaseTwoVFX != null)
        {
            phaseTwoVFX.SetActive(false);
        }
    }

    private void SetPhaseLabel(string label)
    {
        if (phaseText != null)
        {
            phaseText.text = label;
        }

        bossHealthUI?.SetPhaseLabel(label);
    }

    private void ShowPhaseAnnouncement(string title, string subtitle)
    {
        if (phaseAnnouncementCanvasGroup == null)
        {
            return;
        }

        if (phaseTitleText != null)
        {
            phaseTitleText.text = title;
        }

        if (phaseSubtitleText != null)
        {
            phaseSubtitleText.text = subtitle;
        }

        if (phaseAnnouncementRoutine != null)
        {
            StopCoroutine(phaseAnnouncementRoutine);
        }

        phaseAnnouncementCanvasGroup.gameObject.SetActive(true);
        phaseAnnouncementRoutine = StartCoroutine(PhaseAnnouncementRoutine());
    }

    private IEnumerator PhaseAnnouncementRoutine()
    {
        const float FadeDuration = 0.35f;

        RectTransform rect = phaseAnnouncementCanvasGroup.transform as RectTransform;
        float elapsed = 0f;

        phaseAnnouncementCanvasGroup.alpha = 0f;

        if (rect != null)
        {
            rect.localScale = Vector3.one * 1.2f;
        }

        while (elapsed < FadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / FadeDuration);
            phaseAnnouncementCanvasGroup.alpha = t;

            if (rect != null)
            {
                rect.localScale = Vector3.one * Mathf.Lerp(1.2f, 1f, t);
            }

            yield return null;
        }

        phaseAnnouncementCanvasGroup.alpha = 1f;

        if (rect != null)
        {
            rect.localScale = Vector3.one;
        }

        yield return new WaitForSecondsRealtime(phaseAnnouncementDuration);

        elapsed = 0f;

        while (elapsed < FadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            phaseAnnouncementCanvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / FadeDuration);
            yield return null;
        }

        phaseAnnouncementCanvasGroup.alpha = 0f;
        phaseAnnouncementCanvasGroup.gameObject.SetActive(false);
        phaseAnnouncementRoutine = null;
    }

    private void ApplyRendererTint(Color tint)
    {
        if (bossRenderers == null || bossRenderers.Length == 0)
        {
            return;
        }

        tintPropertyBlock ??= new MaterialPropertyBlock();

        for (int i = 0; i < bossRenderers.Length; i++)
        {
            Renderer targetRenderer = bossRenderers[i];

            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(tintPropertyBlock);
            tintPropertyBlock.SetColor(TintColorId, tint);
            targetRenderer.SetPropertyBlock(tintPropertyBlock);
        }
    }

    /// <summary>
    /// No dedicated Roar animation asset to swap in — instead sells the "angry" beat with a
    /// double camera impulse (a heavier "punch" than one flat shake), a brief model scale-up
    /// (never the root, so NavMeshAgent/Collider scale is untouched), and an optional
    /// full-screen red flash, all timed to the same moment as the Roar trigger/SFX.
    /// </summary>
    private void PlayRoarPunchEffects()
    {
        transitionImpulse?.GenerateImpulse(transitionShakeForce);

        if (transitionImpulse != null && secondaryShakeDelay > 0f)
        {
            Invoke(nameof(PlaySecondaryShake), secondaryShakeDelay);
        }

        if (animator != null && hasCachedModelBaseScale)
        {
            if (scalePunchRoutine != null)
            {
                StopCoroutine(scalePunchRoutine);
            }

            scalePunchRoutine = StartCoroutine(ModelScalePunchRoutine());
        }

        if (screenFlashCanvasGroup != null)
        {
            if (screenFlashRoutine != null)
            {
                StopCoroutine(screenFlashRoutine);
            }

            screenFlashRoutine = StartCoroutine(ScreenFlashRoutine());
        }
    }

    private void PlaySecondaryShake()
    {
        transitionImpulse?.GenerateImpulse(transitionShakeForce * secondaryShakeForceMultiplier);
    }

    private IEnumerator ModelScalePunchRoutine()
    {
        Transform modelTransform = animator.transform;
        Vector3 punchedScale = modelBaseScale * (1f + modelScalePunchAmount);
        float halfDuration = Mathf.Max(modelScalePunchDuration * 0.5f, 0.05f);
        float elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            modelTransform.localScale = Vector3.Lerp(modelBaseScale, punchedScale, Mathf.Clamp01(elapsed / halfDuration));
            yield return null;
        }

        elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            modelTransform.localScale = Vector3.Lerp(punchedScale, modelBaseScale, Mathf.Clamp01(elapsed / halfDuration));
            yield return null;
        }

        modelTransform.localScale = modelBaseScale;
        scalePunchRoutine = null;
    }

    private IEnumerator ScreenFlashRoutine()
    {
        float halfDuration = Mathf.Max(screenFlashDuration * 0.5f, 0.05f);
        float elapsed = 0f;

        screenFlashCanvasGroup.gameObject.SetActive(true);
        screenFlashCanvasGroup.alpha = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            screenFlashCanvasGroup.alpha = Mathf.Clamp01(elapsed / halfDuration);
            yield return null;
        }

        screenFlashCanvasGroup.alpha = 1f;
        elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            screenFlashCanvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / halfDuration);
            yield return null;
        }

        screenFlashCanvasGroup.alpha = 0f;
        screenFlashCanvasGroup.gameObject.SetActive(false);
        screenFlashRoutine = null;
    }

    private void PlayTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName))
        {
            return;
        }

        bool hasParameter = false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == triggerName)
            {
                hasParameter = true;
                break;
            }
        }

        if (hasParameter)
        {
            animator.SetTrigger(triggerName);
        }
        else
        {
            Log($"Animator has no '{triggerName}' trigger parameter — skipping this animation cue.", true);
        }
    }

    private void PlayOneShotSafe(AudioClip clip)
    {
        if (clip == null || bossAudioSource == null)
        {
            return;
        }

        bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

        if (sfxEnabled)
        {
            bossAudioSource.PlayOneShot(clip);
        }
    }

    private void Log(string message, bool isWarning = false)
    {
        if (isWarning)
        {
            Debug.LogWarning($"[BigBossPhaseController] {message}", this);
        }
        else if (showDebugLogs)
        {
            Debug.Log($"[BigBossPhaseController] {message}", this);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, phaseTwoAttackRange);

        Gizmos.color = new Color(1f, 0.6f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, minimumThrowDistance);

        Gizmos.color = new Color(1f, 0.9f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, maximumThrowDistance);

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, jumpAttackMinDistance);
        Gizmos.DrawWireSphere(transform.position, jumpAttackMaxDistance);

        if (throwPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(throwPoint.position, 0.3f);
        }

        if (player != null && Application.isPlaying)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(PredictPlayerPosition(), impactRadius);
        }
    }
#endif
}
