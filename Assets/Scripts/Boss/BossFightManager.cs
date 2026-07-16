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
    [Tooltip("Optional — a Boss with a 2-phase kit (BigZombieBoss). Initialized right here in Awake (applies Phase 1 stats/health) so the Intro panel already reads correct, scale-floored values; the phase machine itself stays paused along with bossAI until BeginBossFight().")]
    [SerializeField] private BigBossPhaseController phaseController;

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

    [Header("Completion")]
    [SerializeField] private UnityEvent onBossFightStarted;
    [SerializeField] private UnityEvent onBossDefeated;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    private bool hasStarted;
    private bool hasConfiguredCamera;
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

        // Self-heals a stale reference (e.g. left pointing at a destroyed model's Animator
        // after a model swap replaced the Boss's visual child) instead of silently never
        // playing the Roar animation/SFX again for the rest of the scene's lifetime.
        if (bossAnimator == null && bossObject != null)
        {
            bossAnimator = bossObject.GetComponentInChildren<Animator>(true);
        }

        // The boss must not chase/attack before the fight officially begins, however long the
        // Player lingers near it before ever touching BossIntroTrigger — but it can still
        // freely Idle/Patrol (wander its arena) in the meantime, since only detection (not
        // movement) is disabled here. IntroSequenceRoutine fully freezes it again for the
        // cutscene reveal itself; BeginBossFight re-enables detection once the fight starts.
        if (bossAI != null)
        {
            bossAI.SetAIEnabled(true);
            bossAI.SetDetectionEnabled(false);
        }

        if (bossHealthUI != null && bossHealth != null)
        {
            bossHealthUI.Bind(bossHealth, ResolveBossName());
        }

        if (bossHealth != null)
        {
            bossHealth.Died += HandleBossHealthDied;
        }

        // Applies Phase 1 stats/health (including the scale-floored attack range — see
        // BigBossPhaseController.ApplyStatsToAI) right away, so the Intro panel below already
        // reads correct values. The phase machine's own AI stays paused (via bossAI.SetAIEnabled
        // above) until BeginBossFight() actually unlocks it.
        phaseController?.InitializeBoss();
    }

    private void OnDestroy()
    {
        if (bossHealth != null)
        {
            bossHealth.Died -= HandleBossHealthDied;
        }
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

        // Prefer the Boss's own Collider over Renderer bounds — a SkinnedMeshRenderer's bounds
        // are recomputed live from the CURRENTLY animated bone poses, and a bad/mismatched-scale
        // animation import can balloon them to hundreds of units despite the model looking fine
        // on screen. Collider.bounds comes purely from its own fixed shape/Transform instead.
        Collider bossCollider = bossObject != null ? bossObject.GetComponent<Collider>() : null;

        if (bossCollider == null && (bossRenderers == null || bossRenderers.Length == 0))
        {
            Log("Neither a Boss Collider nor bossRenderers is available — camera will fall back to its default framing instead of sizing to this boss.", true);
        }

        bossCameraController.ConfigureCameraForBoss(bossRenderers, bossCollider);
    }

    private void PlayRoarAnimationAndSfx()
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

        if (bossRoarClip != null && bossAudioSource != null)
        {
            bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

            if (sfxEnabled)
            {
                bossAudioSource.PlayOneShot(bossRoarClip);
            }
        }
        else if (bossRoarClip == null)
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
            bossAI.SetDetectionEnabled(true);
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
