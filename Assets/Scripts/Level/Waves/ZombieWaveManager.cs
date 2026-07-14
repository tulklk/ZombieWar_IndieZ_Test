using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

public enum WaveState
{
    Waiting,
    Starting,
    Spawning,
    Fighting,
    Completed,
    AllWavesCompleted
}

[System.Serializable]
public class ZombieSpawnEntry
{
    public GameObject zombiePrefab;
    public int amount;
    public float spawnInterval;
}

[System.Serializable]
public class WaveData
{
    public string waveName;
    public WaveTrigger startTrigger;
    public WaveBarrier[] barriers;
    public Transform[] spawnPoints;
    public ZombieSpawnEntry[] zombieEntries;
    public float delayBeforeStart;
    public float delayAfterComplete;
}

/// <summary>
/// Drives Level1's 3-wave combat sequence: each WaveData's own WaveTrigger hands off to
/// TryStartWave, which closes that wave's WaveBarrier, spawns its zombieEntries over time
/// (never all in one frame), tracks aliveZombieCount purely via ZombieHealth.Died events
/// (no scene scans, no per-frame counting), and opens the barrier + enables the next
/// wave's trigger once every spawned zombie has died. Completing the last wave calls
/// CompleteLevel(). The project's existing continuous ZombieSpawner (if present in this
/// scene) is disabled on Awake so the two spawning systems never compete.
/// </summary>
public class ZombieWaveManager : MonoBehaviour
{
    [Header("Waves")]
    [SerializeField] private WaveData[] waves;

    [Header("Player")]
    [Tooltip("Auto-found via GameObject.FindGameObjectWithTag(\"Player\") in Awake if left empty.")]
    [SerializeField] private Transform playerTransform;

    [Header("UI")]
    [Tooltip("Root GameObject holding WaveTitleText/ZombieRemainingText/WaveAnnouncementPanel — kept inactive until the Player triggers the first wave, so no HUD counter shows during pre-combat exploration.")]
    [SerializeField] private GameObject waveHudRoot;
    [SerializeField] private TMP_Text waveTitleText;
    [SerializeField] private TMP_Text zombieRemainingText;
    [SerializeField] private GameObject waveAnnouncementPanel;
    [SerializeField] private CanvasGroup waveAnnouncementCanvasGroup;
    [SerializeField] private TMP_Text waveAnnouncementText;

    [Header("Announcement Timing")]
    [SerializeField] private float announcementFadeDuration = 0.35f;
    [SerializeField] private float announcementHoldDuration = 1f;
    [Tooltip("How many on/off pulses AnnouncementText blinks through while the start-of-wave alarm SFX plays.")]
    [SerializeField] private int announcementBlinkCount = 4;
    [Range(0.05f, 1f)]
    [SerializeField] private float announcementBlinkMinAlpha = 0.15f;

    [Header("Announcement Audio")]
    [Tooltip("Played once when a new wave's start announcement appears (e.g. an alarm/siren sting). AnnouncementText blinks in sync with this clip's length before spawning begins.")]
    [SerializeField] private AudioClip emergencyAnnouncementSfx;
    [Tooltip("Auto-added if left empty.")]
    [SerializeField] private AudioSource announcementAudioSource;
    [Tooltip("The same full-screen red border flash used when Player takes damage — pulsed once per AnnouncementText blink during a wave-START alarm only. Left untouched for wave/level-complete announcements.")]
    [SerializeField] private HitFlashOverlay alarmFlashOverlay;

    [Header("Settings")]
    [SerializeField] private bool startFirstWaveAutomatically = false;
    [SerializeField] private float defaultSpawnInterval = 0.6f;
    [Tooltip("A zombie spawn point closer than this to the Player is pushed outward before spawning — spawn points should still be hand-placed roughly 6-10 units out.")]
    [SerializeField] private float minSpawnDistanceFromPlayer = 6f;
    [SerializeField] private bool showDebugLogs = false;

    [Header("Level Completion")]
    [SerializeField] private GameObject victoryPanel;
    [SerializeField] private UnityEvent onAllWavesCompleted;

    /// <summary>Read-only access for Editor tooling (e.g. LevelResultSetup) to wire a persistent listener onto onAllWavesCompleted without needing the field itself public.</summary>
    public UnityEvent OnAllWavesCompletedEvent => onAllWavesCompleted;

    [Header("Existing Systems")]
    [Tooltip("The project's existing continuous background spawner (if placed in this scene) is disabled on Awake so it doesn't compete with wave-based spawning.")]
    [SerializeField] private ZombieSpawner backgroundSpawnerToDisable;

    private const float NavMeshSampleRadius = 5f;

    private int currentWaveIndex = -1;
    private int aliveZombieCount;
    private bool finishedSpawning;
    private int lastDisplayedRemaining = -1;
    private readonly HashSet<ZombieHealth> registeredZombies = new HashSet<ZombieHealth>();

    private Coroutine waveRoutine;
    private Coroutine announcementRoutine;

    public WaveState CurrentState { get; private set; } = WaveState.Waiting;

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

        if (backgroundSpawnerToDisable != null)
        {
            backgroundSpawnerToDisable.enabled = false;
        }

        if (waveAnnouncementPanel != null)
        {
            waveAnnouncementPanel.SetActive(false);
        }

        if (waveHudRoot != null)
        {
            waveHudRoot.SetActive(false);
        }

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(false);
        }

        if (announcementAudioSource == null)
        {
            announcementAudioSource = GetComponent<AudioSource>();
        }

        if (announcementAudioSource == null)
        {
            announcementAudioSource = gameObject.AddComponent<AudioSource>();
        }

        announcementAudioSource.playOnAwake = false;
        announcementAudioSource.loop = false;
        announcementAudioSource.spatialBlend = 0f;

        InitializeWaveTriggers();
        UpdateWaveTitleUI();
    }

    private void Start()
    {
        if (startFirstWaveAutomatically && waves != null && waves.Length > 0)
        {
            TryStartWave(0);
        }
    }

    private void OnDestroy()
    {
        // Scene reload / manager destroyed mid-wave: drop every subscription so no stale
        // ZombieHealth.Died handler survives pointing at a manager that no longer exists.
        foreach (ZombieHealth zombieHealth in registeredZombies)
        {
            if (zombieHealth != null)
            {
                zombieHealth.Died -= NotifyZombieDied;
            }
        }

        registeredZombies.Clear();
    }

    private void InitializeWaveTriggers()
    {
        if (waves == null)
        {
            return;
        }

        for (int i = 0; i < waves.Length; i++)
        {
            WaveData wave = waves[i];

            if (wave == null)
            {
                Log($"waves[{i}] is null.", true);
                continue;
            }

            if (wave.startTrigger != null)
            {
                wave.startTrigger.Initialize(this, i);

                // Only the first wave's trigger is live at scene start — later ones enable
                // themselves only once the wave before them actually completes.
                if (i == 0)
                {
                    wave.startTrigger.EnableTrigger();
                }
                else
                {
                    wave.startTrigger.DisableTrigger();
                }
            }
            else
            {
                Log($"Wave '{WaveLabel(wave, i)}' has no startTrigger assigned — TryStartWave({i}) must be triggered manually.", true);
            }

            if (wave.barriers == null || wave.barriers.Length == 0)
            {
                Log($"Wave '{WaveLabel(wave, i)}' has no barriers assigned — the wave will still run, but nothing will block the Player.", true);
            }
        }
    }

    /// <summary>Called by a WaveTrigger the moment the Player enters its zone.</summary>
    public void TryStartWave(int waveIndex)
    {
        if (waves == null || waveIndex < 0 || waveIndex >= waves.Length)
        {
            Log($"TryStartWave({waveIndex}) — index out of range.", true);
            return;
        }

        if (waves[waveIndex] == null)
        {
            Log($"TryStartWave({waveIndex}) — waves[{waveIndex}] is null.", true);
            return;
        }

        if (CurrentState != WaveState.Waiting && CurrentState != WaveState.Completed)
        {
            Log($"TryStartWave({waveIndex}) ignored — a wave is already active (state: {CurrentState}).");
            return;
        }

        if (waveIndex != currentWaveIndex + 1)
        {
            Log($"TryStartWave({waveIndex}) ignored — expected wave {currentWaveIndex + 1} next, waves don't run out of order or twice.", true);
            return;
        }

        currentWaveIndex = waveIndex;
        CurrentState = WaveState.Starting;

        if (waveHudRoot != null && !waveHudRoot.activeSelf)
        {
            waveHudRoot.SetActive(true);
        }

        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
        }

        waveRoutine = StartCoroutine(RunWaveRoutine(waves[waveIndex]));
    }

    /// <summary>Convenience entry point for startFirstWaveAutomatically / a debug "next wave" hook — starts whichever wave comes after the last one that ran.</summary>
    public void StartCurrentWave()
    {
        TryStartWave(currentWaveIndex + 1);
    }

    private IEnumerator RunWaveRoutine(WaveData wave)
    {
        aliveZombieCount = 0;
        finishedSpawning = false;
        lastDisplayedRemaining = -1;
        registeredZombies.Clear();

        CloseAllBarriers(wave);

        Log($"Wave '{WaveLabel(wave, currentWaveIndex)}' starting — {CountTotalZombies(wave)} zombies queued.");

        // Blocking (not fire-and-forget like ShowAnnouncement): spawning must not start until
        // the alarm SFX + text blink have fully played out.
        yield return PlayWaveStartAnnouncement(WaveLabel(wave, currentWaveIndex).ToUpperInvariant());

        if (wave.delayBeforeStart > 0f)
        {
            yield return new WaitForSecondsRealtime(wave.delayBeforeStart);
        }

        CurrentState = WaveState.Spawning;
        UpdateWaveTitleUI();

        yield return SpawnWaveRoutine(wave);

        finishedSpawning = true;
        CurrentState = WaveState.Fighting;
        UpdateRemainingUI();

        // Every zombie may already have died mid-spawn on a very small/fast wave —
        // CheckWaveCompletion() only completes once finishedSpawning is true, so re-check now.
        CheckWaveCompletion();

        waveRoutine = null;
    }

    private IEnumerator SpawnWaveRoutine(WaveData wave)
    {
        if (wave.zombieEntries == null || wave.zombieEntries.Length == 0)
        {
            Log($"Wave '{wave.waveName}' has no zombie entries configured.", true);
            yield break;
        }

        if (wave.spawnPoints == null || wave.spawnPoints.Length == 0)
        {
            Log($"Wave '{wave.waveName}' has no spawn points configured — cannot spawn.", true);
            yield break;
        }

        int spawnPointCursor = 0;

        foreach (ZombieSpawnEntry entry in wave.zombieEntries)
        {
            if (entry == null || entry.zombiePrefab == null || entry.amount <= 0)
            {
                continue;
            }

            float interval = entry.spawnInterval > 0f ? entry.spawnInterval : defaultSpawnInterval;

            for (int i = 0; i < entry.amount; i++)
            {
                Transform spawnPoint = wave.spawnPoints[spawnPointCursor % wave.spawnPoints.Length];
                spawnPointCursor++;

                SpawnZombie(entry.zombiePrefab, spawnPoint);

                yield return new WaitForSeconds(interval);
            }
        }
    }

    private void SpawnZombie(GameObject prefab, Transform spawnPoint)
    {
        if (prefab == null)
        {
            return;
        }

        if (spawnPoint == null)
        {
            Log("SpawnZombie called with a null spawn point.", true);
            return;
        }

        Vector3 candidatePosition = spawnPoint.position;

        if (playerTransform != null)
        {
            Vector3 toSpawn = candidatePosition - playerTransform.position;
            toSpawn.y = 0f;
            float distanceToPlayer = toSpawn.magnitude;

            if (distanceToPlayer < minSpawnDistanceFromPlayer)
            {
                Vector3 pushDirection = distanceToPlayer > 0.01f ? toSpawn.normalized : Vector3.forward;
                candidatePosition = playerTransform.position + pushDirection * minSpawnDistanceFromPlayer;
            }
        }

        if (NavMesh.SamplePosition(candidatePosition, out NavMeshHit navHit, NavMeshSampleRadius, NavMesh.AllAreas))
        {
            candidatePosition = navHit.position;
        }
        else
        {
            Log($"Spawn point '{spawnPoint.name}' is not within {NavMeshSampleRadius} units of the NavMesh — spawning at its raw position anyway.", true);
        }

        GameObject zombieObject = Instantiate(prefab, candidatePosition, spawnPoint.rotation);

        ZombieAI zombieAI = zombieObject.GetComponent<ZombieAI>();

        if (zombieAI != null && playerTransform != null)
        {
            zombieAI.SetTarget(playerTransform);
        }

        RegisterSpawnedZombie(zombieObject.GetComponent<ZombieHealth>());
    }

    public void RegisterSpawnedZombie(ZombieHealth zombieHealth)
    {
        if (zombieHealth == null)
        {
            Log("RegisterSpawnedZombie called with a null ZombieHealth — check the prefab has the component.", true);
            return;
        }

        if (!registeredZombies.Add(zombieHealth))
        {
            // Already registered — e.g. a pooled zombie reused without being deregistered first.
            return;
        }

        aliveZombieCount++;
        zombieHealth.Died += NotifyZombieDied;

        UpdateRemainingUI();
        Log($"Zombie registered ({zombieHealth.name}). Alive: {aliveZombieCount}.");
    }

    public void NotifyZombieDied(ZombieHealth zombieHealth)
    {
        if (zombieHealth == null || !registeredZombies.Remove(zombieHealth))
        {
            return;
        }

        zombieHealth.Died -= NotifyZombieDied;
        aliveZombieCount = Mathf.Max(0, aliveZombieCount - 1);

        UpdateRemainingUI();
        Log($"Zombie died ({zombieHealth.name}). Remaining: {aliveZombieCount}.");

        CheckWaveCompletion();
    }

    private void CheckWaveCompletion()
    {
        if (CurrentState != WaveState.Fighting)
        {
            return;
        }

        if (!finishedSpawning || aliveZombieCount > 0)
        {
            return;
        }

        CompleteCurrentWave();
    }

    public void CompleteCurrentWave()
    {
        if (CurrentState == WaveState.Completed || CurrentState == WaveState.AllWavesCompleted)
        {
            return;
        }

        if (waves == null || currentWaveIndex < 0 || currentWaveIndex >= waves.Length || waves[currentWaveIndex] == null)
        {
            Log("CompleteCurrentWave() called with no valid current wave.", true);
            return;
        }

        CurrentState = WaveState.Completed;
        Log($"Wave '{waves[currentWaveIndex].waveName}' complete.");

        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
        }

        waveRoutine = StartCoroutine(CompleteWaveRoutine(waves[currentWaveIndex]));
    }

    private IEnumerator CompleteWaveRoutine(WaveData wave)
    {
        ShowAnnouncement($"{WaveLabel(wave, currentWaveIndex).ToUpperInvariant()} COMPLETE");

        if (wave.delayAfterComplete > 0f)
        {
            yield return new WaitForSecondsRealtime(wave.delayAfterComplete);
        }

        OpenAllBarriers(wave);

        int nextWaveIndex = currentWaveIndex + 1;

        if (nextWaveIndex < waves.Length)
        {
            WaveData nextWave = waves[nextWaveIndex];

            if (nextWave != null && nextWave.startTrigger != null)
            {
                nextWave.startTrigger.EnableTrigger();
            }

            UpdateWaveTitleUI();

            if (startFirstWaveAutomatically)
            {
                TryStartWave(nextWaveIndex);
            }
        }
        else
        {
            CompleteLevel();
        }

        waveRoutine = null;
    }

    public void CompleteLevel()
    {
        CurrentState = WaveState.AllWavesCompleted;

        ShowAnnouncement("LEVEL COMPLETE");
        Log("All waves completed — level finished.");

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
        }
        else
        {
            Log("CompleteLevel() called but no victoryPanel is assigned — relying on onAllWavesCompleted only.", true);
        }

        onAllWavesCompleted?.Invoke();
    }

    public int GetAliveZombieCount() => aliveZombieCount;

    private void UpdateWaveTitleUI()
    {
        if (waveTitleText == null || waves == null || waves.Length == 0)
        {
            return;
        }

        int displayIndex = Mathf.Clamp(currentWaveIndex + 1, 1, waves.Length);
        waveTitleText.text = $"PHASE {displayIndex} / {waves.Length}";
    }

    private void UpdateRemainingUI()
    {
        if (zombieRemainingText == null || lastDisplayedRemaining == aliveZombieCount)
        {
            return;
        }

        lastDisplayedRemaining = aliveZombieCount;
        zombieRemainingText.text = $"ZOMBIES: {aliveZombieCount}";
    }

    private void ShowAnnouncement(string text)
    {
        if (waveAnnouncementPanel == null)
        {
            Log($"[Announcement] {text}");
            return;
        }

        if (announcementRoutine != null)
        {
            StopCoroutine(announcementRoutine);
        }

        announcementRoutine = StartCoroutine(AnnouncementRoutine(text));
    }

    private const float AnnouncementStartScale = 0.9f;

    private IEnumerator AnnouncementRoutine(string text)
    {
        if (waveAnnouncementText != null)
        {
            waveAnnouncementText.text = text;
        }

        waveAnnouncementPanel.SetActive(true);

        RectTransform panelRect = waveAnnouncementPanel.transform as RectTransform;

        if (panelRect != null)
        {
            panelRect.localScale = Vector3.one * AnnouncementStartScale;
        }

        if (waveAnnouncementCanvasGroup != null)
        {
            waveAnnouncementCanvasGroup.alpha = 0f;
            yield return FadeAndScale(panelRect, 0f, 1f, AnnouncementStartScale, 1f, announcementFadeDuration);
        }

        yield return new WaitForSecondsRealtime(announcementHoldDuration);

        if (waveAnnouncementCanvasGroup != null)
        {
            yield return FadeAndScale(panelRect, 1f, 0f, 1f, AnnouncementStartScale, announcementFadeDuration);
        }

        waveAnnouncementPanel.SetActive(false);
        announcementRoutine = null;
    }

    /// <summary>
    /// The wave-start alarm: fades the panel in, plays emergencyAnnouncementSfx once, blinks
    /// AnnouncementText a fixed number of times over that clip's length, then fades back out.
    /// Unlike ShowAnnouncement (fire-and-forget, used for "wave complete"/"level complete"),
    /// this is meant to be yielded on directly from RunWaveRoutine so spawning can never start
    /// before the alarm has fully played out.
    /// </summary>
    private IEnumerator PlayWaveStartAnnouncement(string text)
    {
        if (waveAnnouncementPanel == null)
        {
            Log($"[Announcement] {text}");
            yield break;
        }

        if (announcementRoutine != null)
        {
            StopCoroutine(announcementRoutine);
            announcementRoutine = null;
        }

        if (waveAnnouncementText != null)
        {
            waveAnnouncementText.text = text;
            waveAnnouncementText.alpha = 1f;
        }

        waveAnnouncementPanel.SetActive(true);

        RectTransform panelRect = waveAnnouncementPanel.transform as RectTransform;

        if (panelRect != null)
        {
            panelRect.localScale = Vector3.one * AnnouncementStartScale;
        }

        if (waveAnnouncementCanvasGroup != null)
        {
            waveAnnouncementCanvasGroup.alpha = 0f;
            yield return FadeAndScale(panelRect, 0f, 1f, AnnouncementStartScale, 1f, announcementFadeDuration);
        }

        float sfxDuration = 0f;

        if (emergencyAnnouncementSfx != null)
        {
            bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

            if (sfxEnabled && announcementAudioSource != null)
            {
                announcementAudioSource.clip = emergencyAnnouncementSfx;
                announcementAudioSource.Play();
            }

            sfxDuration = emergencyAnnouncementSfx.length;
        }

        yield return BlinkAnnouncementText(sfxDuration > 0f ? sfxDuration : announcementHoldDuration);

        if (waveAnnouncementCanvasGroup != null)
        {
            yield return FadeAndScale(panelRect, 1f, 0f, 1f, AnnouncementStartScale, announcementFadeDuration);
        }

        waveAnnouncementPanel.SetActive(false);
    }

    private IEnumerator BlinkAnnouncementText(float totalDuration)
    {
        if (waveAnnouncementText == null || totalDuration <= 0f || announcementBlinkCount <= 0)
        {
            yield break;
        }

        float halfCycleDuration = totalDuration / (announcementBlinkCount * 2f);

        for (int i = 0; i < announcementBlinkCount; i++)
        {
            alarmFlashOverlay?.PlayFlash();
            yield return LerpTextAlpha(1f, announcementBlinkMinAlpha, halfCycleDuration);
            yield return LerpTextAlpha(announcementBlinkMinAlpha, 1f, halfCycleDuration);
        }

        waveAnnouncementText.alpha = 1f;
    }

    private IEnumerator LerpTextAlpha(float fromAlpha, float toAlpha, float duration)
    {
        if (duration <= 0f)
        {
            waveAnnouncementText.alpha = toAlpha;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            waveAnnouncementText.alpha = Mathf.Lerp(fromAlpha, toAlpha, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        waveAnnouncementText.alpha = toAlpha;
    }

    private IEnumerator FadeAndScale(RectTransform rect, float fromAlpha, float toAlpha, float fromScale, float toScale, float duration)
    {
        if (duration <= 0f)
        {
            waveAnnouncementCanvasGroup.alpha = toAlpha;

            if (rect != null)
            {
                rect.localScale = Vector3.one * toScale;
            }

            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseInOutCubic(Mathf.Clamp01(elapsed / duration));

            waveAnnouncementCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);

            if (rect != null)
            {
                rect.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, t);
            }

            yield return null;
        }

        waveAnnouncementCanvasGroup.alpha = toAlpha;

        if (rect != null)
        {
            rect.localScale = Vector3.one * toScale;
        }
    }

    private float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    /// <summary>A wave can block a zone with more than one gate (e.g. two side corridors) — every non-null entry in barriers[] closes/opens together.</summary>
    private void CloseAllBarriers(WaveData wave)
    {
        if (wave.barriers == null)
        {
            return;
        }

        foreach (WaveBarrier barrier in wave.barriers)
        {
            if (barrier != null)
            {
                barrier.CloseBarrier();
            }
        }
    }

    /// <summary>
    /// Doesn't open the barriers immediately — arms each one so it stays solid until the
    /// Player actually walks up to it, at which point WaveBarrier's own approach trigger
    /// plays the slide-down-into-the-ground animation and opens the path.
    /// </summary>
    private void OpenAllBarriers(WaveData wave)
    {
        if (wave.barriers == null || wave.barriers.Length == 0)
        {
            Log($"Wave '{wave.waveName}' has no barriers to open.", true);
            return;
        }

        foreach (WaveBarrier barrier in wave.barriers)
        {
            if (barrier != null)
            {
                barrier.ArmForPlayerApproach();
            }
        }
    }

    private static string WaveLabel(WaveData wave, int index)
    {
        return !string.IsNullOrEmpty(wave?.waveName) ? wave.waveName : $"Phase {index + 1}";
    }

    private static int CountTotalZombies(WaveData wave)
    {
        if (wave.zombieEntries == null)
        {
            return 0;
        }

        int total = 0;

        foreach (ZombieSpawnEntry entry in wave.zombieEntries)
        {
            if (entry != null)
            {
                total += Mathf.Max(0, entry.amount);
            }
        }

        return total;
    }

    private void Log(string message, bool isWarning = false)
    {
        if (isWarning)
        {
            Debug.LogWarning($"[ZombieWaveManager] {message}");
        }
        else if (showDebugLogs)
        {
            Debug.Log($"[ZombieWaveManager] {message}");
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (waves != null)
        {
            Gizmos.color = Color.yellow;

            foreach (WaveData wave in waves)
            {
                if (wave?.spawnPoints == null)
                {
                    continue;
                }

                foreach (Transform point in wave.spawnPoints)
                {
                    if (point != null)
                    {
                        Gizmos.DrawWireSphere(point.position, 1f);
                    }
                }
            }
        }

        Transform playerForGizmo = playerTransform != null
            ? playerTransform
            : GameObject.FindGameObjectWithTag("Player")?.transform;

        if (playerForGizmo != null)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawWireSphere(playerForGizmo.position, minSpawnDistanceFromPlayer);
        }
    }
#endif
}
