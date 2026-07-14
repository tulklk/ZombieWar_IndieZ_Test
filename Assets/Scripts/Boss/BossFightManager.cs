using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum BossFightState
{
    Waiting,
    Intro,
    ReturningToPlayer,
    Fighting,
    BossDefeated
}

/// <summary>
/// Orchestrates the whole boss-fight sequence end to end: waits for BossIntroTrigger, locks
/// the Player, switches cameras via BossCameraController, plays the Roar animation/SFX,
/// shows BossIntroUI (reading stats straight off the boss's own ZombieAI/ZombieHealth/
/// ZombieData — never a second, duplicate data source), switches to the combat camera,
/// unlocks the Player, enables the boss's ZombieAI, and — driven entirely by
/// ZombieHealth.Died, never a per-frame Update() poll — tears everything back down when the
/// boss dies. Every step is null-safe: missing UI/audio/camera references degrade gracefully
/// (the fight still starts) instead of throwing.
/// </summary>
public class BossFightManager : MonoBehaviour
{
    [Header("Boss")]
    [SerializeField] private GameObject bossObject;
    [SerializeField] private ZombieAI bossAI;
    [SerializeField] private ZombieHealth bossHealth;
    [SerializeField] private Animator bossAnimator;
    [SerializeField] private Renderer[] bossRenderers;
    [SerializeField] private Transform bossCameraTarget;

    [Header("Player")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private PlayerBombController bombController;

    [Header("Cameras")]
    [SerializeField] private BossCameraController bossCameraController;

    [Header("UI")]
    [SerializeField] private BossIntroUI bossIntroUI;
    [SerializeField] private BossHealthUI bossHealthUI;
    [Tooltip("GameCanvas (or any UI root) — every immediate child except BossIntroUI's own GameObject is hidden for the duration of the intro cutscene (joystick/buttons/HP bar/minimap etc. would otherwise clutter a scene the Player has no control over), then restored to exactly whatever was active before. Leave empty to skip this entirely.")]
    [SerializeField] private Transform gameCanvasRoot;

    [Header("Intro Timing")]
    [Tooltip("Beat before the camera even starts swinging to the boss.")]
    [SerializeField] private float delayBeforeBossReveal = 0.3f;
    [Tooltip("How long to wait for the CinemachineBrain blend to (mostly) finish before triggering Roar — should roughly match the Brain's default blend time.")]
    [SerializeField] private float cameraBlendWaitDuration = 1f;
    [Tooltip("Total time BossIntroPanel stays up, roar included.")]
    [SerializeField] private float bossRevealDuration = 3f;
    [Tooltip("Beat after the combat camera is live before the fight actually unlocks the Player.")]
    [SerializeField] private float delayBeforeFight = 0.5f;
    [SerializeField] private string roarTriggerName = "Roar";

    [Header("Audio")]
    [SerializeField] private AudioSource bossAudioSource;
    [SerializeField] private AudioClip bossRoarClip;

    [Header("Barrier (optional)")]
    [Tooltip("Closed the moment the intro starts, opened when the boss dies. Leave empty if this arena has no barrier.")]
    [SerializeField] private WaveBarrier bossArenaBarrier;

    [Header("Phase 2 (boss gets stronger once health drops to/below the threshold)")]
    [SerializeField] private bool enablePhaseTwo = true;
    [Tooltip("Fraction of MaxHealth (at the moment it's crossed) that triggers Phase 2 — matches BossHealthUI's own phase2Threshold by default so the \"PHASE 2\" label and the actual stat boost happen together.")]
    [SerializeField, Range(0.05f, 0.95f)] private float phase2HealthThreshold = 0.5f;
    [SerializeField] private float phase2DamageMultiplier = 1.5f;
    [SerializeField] private float phase2AttackRangeMultiplier = 1.3f;
    [SerializeField] private float phase2SpeedMultiplier = 1.4f;
    [Tooltip("Also grows MaxHealth by this multiplier (currentHealth scales by the same ratio, so a Boss at exactly the threshold stays at that same percentage of the new, bigger pool) — a \"power surge\" rather than a jarring full-refill.")]
    [SerializeField] private float phase2MaxHealthMultiplier = 1.4f;
    [Tooltip("Played (with the same Roar trigger/camera shake as the intro) when Phase 2 starts. Falls back to bossRoarClip if left empty.")]
    [SerializeField] private AudioClip phase2RoarClip;
    [SerializeField] private UnityEvent onPhaseTwoStarted;

    [Header("Completion")]
    [SerializeField] private UnityEvent onBossFightStarted;
    [SerializeField] private UnityEvent onBossDefeated;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    private bool hasStarted;
    private bool hasConfiguredCamera;
    private bool hasEnteredPhaseTwo;
    private Coroutine sequenceRoutine;
    private readonly List<GameObject> hudElementsHiddenForIntro = new List<GameObject>();

    public BossFightState CurrentState { get; private set; } = BossFightState.Waiting;

    /// <summary>Read-only access for Editor tooling (e.g. BossFightSetup) to wire a persistent listener without needing the field itself public.</summary>
    public UnityEvent OnBossDefeatedEvent => onBossDefeated;

    private void Awake()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (player != null)
            {
                playerTransform = player.transform;
            }
        }

        // The boss must not chase/attack before the fight officially begins, however long
        // the Player lingers near it before ever touching BossIntroTrigger.
        if (bossAI != null)
        {
            bossAI.SetAIEnabled(false);
        }

        if (bossHealthUI != null && bossHealth != null)
        {
            bossHealthUI.Bind(bossHealth, ResolveBossName());
        }

        if (bossHealth != null)
        {
            bossHealth.Died += HandleBossHealthDied;
            bossHealth.HealthChanged += HandleBossHealthChangedForPhaseTwo;
        }
    }

    private void OnDestroy()
    {
        if (bossHealth != null)
        {
            bossHealth.Died -= HandleBossHealthDied;
            bossHealth.HealthChanged -= HandleBossHealthChangedForPhaseTwo;
        }
    }

    /// <summary>
    /// BigZombieData's attackRange/detectionRange were tuned for BigZombie_AI.prefab's own
    /// authored scale (6,6,6 — also what the Wave-3 mini-boss uses) — this Boss is often
    /// scaled up further by hand for a more imposing encounter, so its reach needs to grow
    /// to match or melee attacks silently miss despite looking adjacent on screen. Runs in
    /// Start() (never Awake()) specifically because Unity doesn't guarantee Awake() order
    /// across different GameObjects — running here guarantees ZombieAI.Awake()'s own
    /// ApplyZombieData call has already applied the tuned base values before this scales
    /// them, instead of racing it and sometimes getting silently overwritten right back.
    /// A plain scale-ratio wasn't a large enough floor on its own: it scales whatever
    /// attackRange happened to already be, which isn't necessarily related to how physically
    /// big this Boss's own CapsuleCollider now is at its current scale. So the required range
    /// is also floored against the Boss's OWN measured collision radius (world-space, via
    /// lossyScale) plus a melee reach margin (the Player's own radius plus some swing reach)
    /// — guaranteeing a Player standing right at the edge of the Boss's visible/physical body
    /// always reads as "in range," regardless of whatever attackRange was originally tuned to.
    /// Reads everything live off the current instance, so re-scaling the Boss (or swapping
    /// its Collider) and re-testing picks up the change automatically.
    /// </summary>
    private const float ReferencePrefabScale = 6f;
    private const float MeleeReachMargin = 2.5f;
    private const float DetectionOverAttackMargin = 2f;

    private void Start()
    {
        if (bossAI == null || bossObject == null)
        {
            return;
        }

        float scaleMultiplier = Mathf.Max(bossObject.transform.localScale.x / ReferencePrefabScale, 1f);
        float scaledAttackRange = bossAI.AttackRange * scaleMultiplier;
        float scaledDetectionRange = bossAI.DetectionRange * scaleMultiplier;

        float colliderFloor = GetEffectiveColliderRadius(bossObject) + MeleeReachMargin;

        float finalAttackRange = Mathf.Max(scaledAttackRange, colliderFloor);
        float finalDetectionRange = Mathf.Max(scaledDetectionRange, finalAttackRange + DetectionOverAttackMargin);

        bossAI.SetAttackAndDetectionRange(finalAttackRange, finalDetectionRange);

        Log($"Scaled attack/detection range for this Boss's size — attackRange={finalAttackRange:0.0} (collider floor {colliderFloor:0.0}), detectionRange={finalDetectionRange:0.0}.");
    }

    /// <summary>World-space radius of the Boss's own CapsuleCollider (local radius × the larger of its X/Z lossyScale) — 0 if it has none.</summary>
    private static float GetEffectiveColliderRadius(GameObject boss)
    {
        CapsuleCollider capsule = boss.GetComponent<CapsuleCollider>();

        if (capsule == null)
        {
            return 0f;
        }

        Vector3 lossyScale = boss.transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
        return capsule.radius * horizontalScale;
    }

    /// <summary>Wired to BossIntroTrigger.</summary>
    public void StartBossIntro()
    {
        if (hasStarted || CurrentState != BossFightState.Waiting)
        {
            return;
        }

        hasStarted = true;

        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
        }

        sequenceRoutine = StartCoroutine(IntroSequenceRoutine());
    }

    private IEnumerator IntroSequenceRoutine()
    {
        CurrentState = BossFightState.Intro;
        Log("Boss intro starting.");

        bossArenaBarrier?.CloseBarrier();

        SetPlayerControlEnabled(false);
        HideGameplayHudForIntro();

        if (bossAI != null)
        {
            bossAI.SetAIEnabled(false);
        }

        if (!hasConfiguredCamera)
        {
            ConfigureCameraOnce();
        }

        if (delayBeforeBossReveal > 0f)
        {
            yield return new WaitForSecondsRealtime(delayBeforeBossReveal);
        }

        if (bossCameraController != null)
        {
            bossCameraController.SwitchToBossIntroCamera();
        }
        else
        {
            Log("bossCameraController not assigned — skipping the intro camera swing entirely.", true);
        }

        if (cameraBlendWaitDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(cameraBlendWaitDuration);
        }

        // The boss may already be dead by the time the camera arrives (e.g. a stray hit
        // during the blend) — don't roar/show stats for a corpse.
        if (CurrentState != BossFightState.Intro)
        {
            sequenceRoutine = null;
            yield break;
        }

        PlayRoarAnimationAndSfx();
        ShowBossIntroPanel();

        if (bossRevealDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(bossRevealDuration);
        }

        if (CurrentState != BossFightState.Intro)
        {
            sequenceRoutine = null;
            yield break;
        }

        bossIntroUI?.Hide();
        CurrentState = BossFightState.ReturningToPlayer;

        if (bossCameraController != null)
        {
            bossCameraController.SwitchToBossCombatCamera();
        }

        if (cameraBlendWaitDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(cameraBlendWaitDuration);
        }

        if (delayBeforeFight > 0f)
        {
            yield return new WaitForSecondsRealtime(delayBeforeFight);
        }

        if (CurrentState == BossFightState.ReturningToPlayer)
        {
            BeginBossFight();
        }

        sequenceRoutine = null;
    }

    /// <summary>Bounds are measured exactly once per boss fight — never per frame.</summary>
    private void ConfigureCameraOnce()
    {
        hasConfiguredCamera = true;

        if (bossCameraController == null)
        {
            return;
        }

        if (bossRenderers == null || bossRenderers.Length == 0)
        {
            Log("bossRenderers is empty — camera will fall back to its default framing instead of sizing to this boss.", true);
        }

        bossCameraController.ConfigureCameraForBoss(bossRenderers);
    }

    /// <summary>Shared by the intro reveal and the Phase 2 transition — pass a clip override for Phase 2's own roar SFX, or leave null to use bossRoarClip.</summary>
    private void PlayRoarAnimationAndSfx(AudioClip clipOverride = null)
    {
        if (bossAnimator != null && !string.IsNullOrEmpty(roarTriggerName))
        {
            bool hasRoarParameter = false;

            foreach (AnimatorControllerParameter parameter in bossAnimator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == roarTriggerName)
                {
                    hasRoarParameter = true;
                    break;
                }
            }

            if (hasRoarParameter)
            {
                bossAnimator.SetTrigger(roarTriggerName);
            }
            else
            {
                Log($"Boss Animator has no '{roarTriggerName}' trigger parameter — skipping the roar animation (SFX/UI/camera still play). Add a Roar trigger + state to the boss's Animator Controller to fix this.", true);
            }
        }

        AudioClip clipToPlay = clipOverride != null ? clipOverride : bossRoarClip;

        if (clipToPlay != null && bossAudioSource != null)
        {
            bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

            if (sfxEnabled)
            {
                bossAudioSource.PlayOneShot(clipToPlay);
            }
        }
        else if (clipToPlay == null)
        {
            Log("No roar clip assigned — continuing silently.", true);
        }

        bossCameraController?.PlayRoarShake();
    }

    private void ShowBossIntroPanel()
    {
        if (bossIntroUI == null)
        {
            Log("bossIntroUI not assigned — boss fight continues without an intro panel.", true);
            return;
        }

        if (bossAI == null || bossHealth == null)
        {
            Log("bossAI/bossHealth not assigned — cannot read boss stats for the intro panel.", true);
            return;
        }

        string bossType = bossAI.Data != null ? bossAI.Data.bossType : string.Empty;

        bossIntroUI.ShowBossInfo(ResolveBossName(), bossType, bossHealth.MaxHealth, bossAI.Damage, bossAI.RunSpeed, bossAI.AttackRange);
    }

    private string ResolveBossName()
    {
        if (bossAI != null && bossAI.Data != null && !string.IsNullOrEmpty(bossAI.Data.bossName))
        {
            return bossAI.Data.bossName;
        }

        return bossObject != null ? bossObject.name : "BOSS ZOMBIE";
    }

    public void BeginBossFight()
    {
        if (CurrentState == BossFightState.Fighting || CurrentState == BossFightState.BossDefeated)
        {
            return;
        }

        if (bossCameraController != null)
        {
            bossCameraController.SwitchToBossCombatCamera();
        }

        bossHealthUI?.Show();

        SetPlayerControlEnabled(true);
        RestoreGameplayHudAfterIntro();

        if (bossAI != null)
        {
            if (playerTransform != null)
            {
                bossAI.SetTarget(playerTransform);
            }

            bossAI.SetAIEnabled(true);
        }

        CurrentState = BossFightState.Fighting;
        Log("Boss fight started.");

        onBossFightStarted?.Invoke();
    }

    /// <summary>
    /// Locks/unlocks every gameplay input the intro cutscene needs frozen. PlayerMovement
    /// isn't touched directly — its own Move() already zeroes joystick input and rotation
    /// whenever WeaponController.IsActionLocked is true, so locking the weapon freezes
    /// movement too without a second, parallel lock flag to keep in sync.
    /// </summary>
    public void SetPlayerControlEnabled(bool controlEnabled)
    {
        bool locked = !controlEnabled;

        if (weaponController != null)
        {
            weaponController.SetActionLocked(locked);
        }
        else
        {
            Log("weaponController not assigned — Fire button won't be locked during the intro.", true);
        }

        if (bombController != null)
        {
            bombController.SetInputLocked(locked);
        }
        else
        {
            Log("bombController not assigned — Bomb button won't be locked during the intro.", true);
        }

        if (playerMovement == null)
        {
            Log("playerMovement not assigned (only used for a null-reference check — movement itself locks via WeaponController).", true);
        }
    }

    /// <summary>
    /// Hides every immediate child of gameCanvasRoot except BossIntroUI's own GameObject —
    /// only the ones that were actually active get recorded, so RestoreGameplayHudAfterIntro
    /// brings back exactly what was showing before (an already-hidden panel like WinPanel or
    /// BossHealthPanel stays hidden, it's never force-reactivated).
    /// </summary>
    private void HideGameplayHudForIntro()
    {
        if (gameCanvasRoot == null)
        {
            return;
        }

        hudElementsHiddenForIntro.Clear();
        GameObject introPanelObject = bossIntroUI != null ? bossIntroUI.gameObject : null;

        for (int i = 0; i < gameCanvasRoot.childCount; i++)
        {
            GameObject child = gameCanvasRoot.GetChild(i).gameObject;

            if (child == introPanelObject || !child.activeSelf)
            {
                continue;
            }

            child.SetActive(false);
            hudElementsHiddenForIntro.Add(child);
        }
    }

    /// <summary>Safe to call even if nothing was ever hidden (e.g. gameCanvasRoot unassigned, or already restored) — the list is simply empty.</summary>
    private void RestoreGameplayHudAfterIntro()
    {
        foreach (GameObject hudElement in hudElementsHiddenForIntro)
        {
            if (hudElement != null)
            {
                hudElement.SetActive(true);
            }
        }

        hudElementsHiddenForIntro.Clear();
    }

    private void HandleBossHealthDied(ZombieHealth deadBossHealth)
    {
        HandleBossDefeated();
    }

    /// <summary>
    /// Watches every HealthChanged tick (not a per-frame poll) for the moment the Boss first
    /// drops to/below phase2HealthThreshold, only while the fight is actually live — never
    /// mid-intro, never after death. Guards against the same fatal-hit edge case Died itself
    /// guards against: a single hit can carry currentHealth from well above the threshold
    /// straight through to 0, and HealthChanged fires (earlier in ZombieHealth.TakeDamage)
    /// before the Died check even runs — so Phase 2 must not kick in on a Boss that's
    /// simultaneously dying in that same hit.
    /// </summary>
    private void HandleBossHealthChangedForPhaseTwo(int current, int max)
    {
        if (!enablePhaseTwo || hasEnteredPhaseTwo || CurrentState != BossFightState.Fighting)
        {
            return;
        }

        if (current <= 0 || max <= 0)
        {
            return;
        }

        if ((float)current / max <= phase2HealthThreshold)
        {
            EnterPhaseTwo();
        }
    }

    private void EnterPhaseTwo()
    {
        hasEnteredPhaseTwo = true;
        Log("Boss entering Phase 2 — stronger, faster, longer reach, more health.");

        if (bossAI != null)
        {
            bossAI.SetDamage(Mathf.RoundToInt(bossAI.Damage * phase2DamageMultiplier));
            bossAI.SetRunSpeed(bossAI.RunSpeed * phase2SpeedMultiplier);
            bossAI.SetAttackAndDetectionRange(bossAI.AttackRange * phase2AttackRangeMultiplier, bossAI.DetectionRange * phase2AttackRangeMultiplier);
        }

        bossHealth?.ScaleMaxHealth(phase2MaxHealthMultiplier);

        PlayRoarAnimationAndSfx(phase2RoarClip);

        onPhaseTwoStarted?.Invoke();
    }

    /// <summary>Also safe to call directly/manually (e.g. from a debug button) — guarded so it only ever runs once.</summary>
    public void HandleBossDefeated()
    {
        if (CurrentState == BossFightState.BossDefeated)
        {
            return;
        }

        bool wasActiveFight = CurrentState == BossFightState.Fighting
            || CurrentState == BossFightState.Intro
            || CurrentState == BossFightState.ReturningToPlayer;

        CurrentState = BossFightState.BossDefeated;

        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }

        if (bossAI != null)
        {
            bossAI.SetAIEnabled(false);
        }

        bossIntroUI?.Hide();
        bossHealthUI?.Hide();

        if (bossCameraController != null)
        {
            bossCameraController.SwitchToPlayerCamera();
        }

        bossArenaBarrier?.OpenBarrier();

        SetPlayerControlEnabled(true);
        RestoreGameplayHudAfterIntro();

        Log("Boss defeated.");

        if (wasActiveFight)
        {
            onBossDefeated?.Invoke();
        }
    }

    private void Log(string message, bool isWarning = false)
    {
        if (isWarning)
        {
            Debug.LogWarning($"[BossFightManager] {message}", this);
        }
        else if (showDebugLogs)
        {
            Debug.Log($"[BossFightManager] {message}", this);
        }
    }
}
