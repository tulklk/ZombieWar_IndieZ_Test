using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A gate blocking the corridor between two wave zones. While closed, a solid Collider
/// blocks the Player (and, if a NavMeshObstacle is assigned, carves zombie NavMesh paths
/// too). Once ZombieWaveManager calls ArmForPlayerApproach() (the wave is done, but the
/// barrier stays solid), walking into the barrier's own approach trigger — a second,
/// bigger BoxCollider auto-added around the blocking one — is what actually opens it:
/// the barrier slides straight down into the ground over slideDownDuration and its
/// colliders disable immediately so the Player isn't blocked mid-animation. NavMesh is
/// never rebaked — obstacle carving is the only supported way to change zombie pathing
/// at runtime.
/// </summary>
public class WaveBarrier : MonoBehaviour
{
    [Header("Visual & Collision")]
    [SerializeField] private GameObject barrierVisual;
    [SerializeField] private Collider blockingCollider;
    [SerializeField] private NavMeshObstacle navMeshObstacle;

    [Header("Approach Trigger")]
    [Tooltip("Auto-added (sized around blockingCollider + approachTriggerPadding) if left empty. The Player must enter this zone, after ArmForPlayerApproach() has been called, for the barrier to actually open.")]
    [SerializeField] private BoxCollider approachTrigger;
    [Tooltip("Extra world-space distance, on every side, the approach trigger extends past blockingCollider's own bounds.")]
    [SerializeField] private float approachTriggerPadding = 3f;

    [Header("Slide-Down Animation")]
    [Tooltip("Extra distance added on top of the barrier's own measured height, so it fully disappears below the floor instead of leaving the top sticking out — a fixed slide distance looked barely-moved on tall/scaled-up barriers.")]
    [SerializeField] private float slideDownExtraMargin = 1f;
    [SerializeField] private float slideDownDuration = 0.8f;

    [Header("Animation (optional, for a barrier with its own Animator)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string openTriggerName = "Open";
    [SerializeField] private string closeTriggerName = "Close";

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;

    [Header("Settings")]
    [SerializeField] private bool startClosed = true;

    private int openTriggerHash;
    private int closeTriggerHash;
    private Coroutine openRoutine;
    private Vector3 closedPosition;
    private bool armedForApproach;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        closedPosition = transform.position;

        if (!string.IsNullOrEmpty(openTriggerName))
        {
            openTriggerHash = Animator.StringToHash(openTriggerName);
        }

        if (!string.IsNullOrEmpty(closeTriggerName))
        {
            closeTriggerHash = Animator.StringToHash(closeTriggerName);
        }

        EnsureApproachTrigger();

        // Applied instantly (no animation/audio/coroutine) — this establishes the scene's
        // starting state, not a runtime open/close event.
        IsOpen = !startClosed;

        if (barrierVisual != null)
        {
            barrierVisual.SetActive(true);
        }

        if (blockingCollider != null)
        {
            blockingCollider.enabled = startClosed;
        }

        if (navMeshObstacle != null)
        {
            navMeshObstacle.enabled = startClosed;
        }

        if (approachTrigger != null)
        {
            approachTrigger.enabled = false;
        }
    }

    /// <summary>
    /// blockingCollider is expected to be a BoxCollider (every barrier this project builds is
    /// a primitive Cube) — the trigger is sized to match it plus approachTriggerPadding
    /// world units on every side, converted into local space via lossyScale so it comes out
    /// right regardless of how this specific barrier was scaled/rotated by hand.
    /// </summary>
    private void EnsureApproachTrigger()
    {
        if (approachTrigger != null)
        {
            approachTrigger.isTrigger = true;
            return;
        }

        if (!(blockingCollider is BoxCollider blockingBox))
        {
            Debug.LogWarning($"[WaveBarrier] '{name}' has no BoxCollider approachTrigger assigned and blockingCollider isn't a BoxCollider either — cannot auto-create the approach trigger. Assign one by hand.", this);
            return;
        }

        approachTrigger = gameObject.AddComponent<BoxCollider>();
        approachTrigger.isTrigger = true;

        Vector3 scale = transform.lossyScale;
        Vector3 paddingInLocalSpace = new Vector3(
            Mathf.Abs(scale.x) > 0.0001f ? approachTriggerPadding / scale.x : approachTriggerPadding,
            Mathf.Abs(scale.y) > 0.0001f ? approachTriggerPadding / scale.y : approachTriggerPadding,
            Mathf.Abs(scale.z) > 0.0001f ? approachTriggerPadding / scale.z : approachTriggerPadding);

        approachTrigger.center = blockingBox.center;
        approachTrigger.size = blockingBox.size + paddingInLocalSpace * 2f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!armedForApproach || IsOpen || !other.CompareTag("Player"))
        {
            return;
        }

        OpenBarrier();
    }

    public void SetBarrierState(bool isOpen)
    {
        if (isOpen)
        {
            OpenBarrier();
        }
        else
        {
            CloseBarrier();
        }
    }

    /// <summary>Wave is done, but the barrier stays solid until the Player actually walks up to it (see OnTriggerEnter).</summary>
    public void ArmForPlayerApproach()
    {
        if (IsOpen)
        {
            return;
        }

        armedForApproach = true;

        if (approachTrigger != null)
        {
            approachTrigger.enabled = true;
        }
    }

    public void CloseBarrier()
    {
        if (openRoutine != null)
        {
            StopCoroutine(openRoutine);
            openRoutine = null;
        }

        IsOpen = false;
        armedForApproach = false;
        transform.position = closedPosition;

        if (barrierVisual != null)
        {
            barrierVisual.SetActive(true);
        }

        if (blockingCollider != null)
        {
            blockingCollider.enabled = true;
        }

        if (navMeshObstacle != null)
        {
            navMeshObstacle.enabled = true;
        }

        if (approachTrigger != null)
        {
            approachTrigger.enabled = false;
        }

        if (animator != null && !string.IsNullOrEmpty(closeTriggerName))
        {
            animator.SetTrigger(closeTriggerHash);
        }

        PlayClip(closeClip);
    }

    public void OpenBarrier()
    {
        if (openRoutine != null)
        {
            StopCoroutine(openRoutine);
        }

        IsOpen = true;
        armedForApproach = false;

        if (animator != null && !string.IsNullOrEmpty(openTriggerName))
        {
            animator.SetTrigger(openTriggerHash);
        }

        PlayClip(openClip);

        if (navMeshObstacle != null)
        {
            navMeshObstacle.enabled = false;
        }

        // Measured from blockingCollider's world-space bounds BEFORE disabling it — a
        // disabled Collider can't be trusted to report bounds correctly. Using the
        // barrier's own actual height (instead of one fixed distance for every barrier)
        // is what fixes tall/hand-scaled barriers only sliding "a little".
        float slideDistance = CalculateSlideDownDistance();

        // Disabled immediately (not after the slide finishes) — the Player only ever
        // triggers this by walking right up to the barrier, so by the time it's fully
        // sunk they're already moving through where it used to block.
        if (blockingCollider != null)
        {
            blockingCollider.enabled = false;
        }

        if (approachTrigger != null)
        {
            approachTrigger.enabled = false;
        }

        openRoutine = StartCoroutine(SlideDownRoutine(slideDistance));
    }

    private float CalculateSlideDownDistance()
    {
        float barrierHeight = blockingCollider != null
            ? blockingCollider.bounds.size.y
            : transform.lossyScale.y;

        return barrierHeight + slideDownExtraMargin;
    }

    private IEnumerator SlideDownRoutine(float slideDistance)
    {
        Vector3 startPosition = transform.position;
        Vector3 endPosition = startPosition + Vector3.down * slideDistance;
        float elapsed = 0f;

        while (elapsed < slideDownDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startPosition, endPosition, Mathf.Clamp01(elapsed / slideDownDuration));
            yield return null;
        }

        transform.position = endPosition;
        openRoutine = null;
    }

    private void PlayClip(AudioClip clip)
    {
        if (audioSource == null || clip == null)
        {
            return;
        }

        bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

        if (sfxEnabled)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Collider col = blockingCollider != null ? blockingCollider : GetComponent<Collider>();

        if (col != null)
        {
            bool open = Application.isPlaying ? IsOpen : !startClosed;
            Gizmos.color = open ? new Color(0f, 1f, 0f, 0.4f) : new Color(1f, 0f, 0f, 0.4f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }

        if (approachTrigger != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
            Gizmos.DrawCube(approachTrigger.bounds.center, approachTrigger.bounds.size);
        }
    }
}
