using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 1.5f;
    [Tooltip("Shapes how joystick tilt maps to actual speed: 1 = linear (old behavior). Higher values keep partial tilt closer to Walk pace and only reach full Run speed near max tilt, matching the Walk/Run animation split instead of a flat ramp.")]
    [SerializeField] private float speedCurveExponent = 1.5f;

    [Header("References")]
    [SerializeField] private Joystick joystick;
    [SerializeField] private PlayerAnimationController animationController;
    [SerializeField] private WeaponController weaponController;

    [Header("Footsteps")]
    [SerializeField] private AudioClip footstepSfx;
    [Tooltip("Auto-added if left empty.")]
    [SerializeField] private AudioSource footstepAudioSource;
    [Tooltip("Seconds between footsteps when moving at full joystick tilt — scales slower as input magnitude drops (Walk vs Run).")]
    [SerializeField] private float stepIntervalAtFullSpeed = 0.35f;
    [Tooltip("Hard cap on how long a single footstep is audible, regardless of the source clip's own length.")]
    [SerializeField] private float footstepMaxDuration = 0.2f;

    private CharacterController characterController;
    private Vector3 verticalVelocity;
    private Camera mainCamera;
    private float footstepTimer;
    private Coroutine footstepStopCoroutine;

    private const float Gravity = -20f;
    private const float MinMoveMagnitudeForFootsteps = 0.1f;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        mainCamera = Camera.main;

        if (animationController == null)
        {
            animationController = GetComponent<PlayerAnimationController>();
        }

        if (weaponController == null)
        {
            weaponController = GetComponent<WeaponController>();
        }

        if (footstepAudioSource == null)
        {
            footstepAudioSource = GetComponent<AudioSource>();
        }

        if (footstepAudioSource == null)
        {
            footstepAudioSource = gameObject.AddComponent<AudioSource>();
        }

        footstepAudioSource.playOnAwake = false;
        footstepAudioSource.loop = false;
        footstepAudioSource.spatialBlend = 0f;
    }

    private void Update()
    {
        Move();
    }

    private void Move()
    {
        bool movementLocked = weaponController != null &&
            (weaponController.IsActionLocked || weaponController.IsReloading);

        float horizontal = movementLocked ? 0f : (joystick != null ? joystick.Horizontal : Input.GetAxisRaw("Horizontal"));
        float vertical = movementLocked ? 0f : (joystick != null ? joystick.Vertical : Input.GetAxisRaw("Vertical"));

        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical);
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        if (mainCamera != null && moveDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 camForward = mainCamera.transform.forward;
            camForward.y = 0f;
            camForward.Normalize();

            Vector3 camRight = mainCamera.transform.right;
            camRight.y = 0f;
            camRight.Normalize();

            moveDirection = Vector3.ClampMagnitude(camForward * vertical + camRight * horizontal, 1f);
        }

        // Curved instead of linear: a medium tilt stays close to Walk pace, and Run's
        // full speed only kicks in near max tilt — matching the Walk/Run animation
        // split instead of speed ramping up evenly across the whole joystick range.
        float rawMagnitude = moveDirection.magnitude;
        float curvedMagnitude = Mathf.Pow(rawMagnitude, speedCurveExponent);

        characterController.Move(moveDirection.normalized * curvedMagnitude * moveSpeed * Time.deltaTime);

        if (moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );

            weaponController?.ResetToIdlePose();
        }

        // Same curved value drives the Animator too, so foot movement in the Walk/Run
        // blend and animation playback rate (MotionSpeed) stay matched to how far the
        // character actually travels — otherwise feet would slide relative to the ground.
        animationController?.SetMoveSpeed(curvedMagnitude, curvedMagnitude * moveSpeed);
        UpdateFootsteps(curvedMagnitude);

        if (characterController.isGrounded && verticalVelocity.y < 0)
        {
            verticalVelocity.y = -2f;
        }

        verticalVelocity.y += Gravity * Time.deltaTime;
        characterController.Move(verticalVelocity * Time.deltaTime);
    }

    /// <summary>
    /// Plays footstepSfx on a cadence tied to move input magnitude — faster tilt (Run)
    /// steps faster than a light tilt (Walk). Timer resets on stopping so the first
    /// step after starting to move again plays immediately instead of waiting out
    /// whatever was left of the previous interval.
    /// </summary>
    private void UpdateFootsteps(float moveMagnitude)
    {
        if (!characterController.isGrounded || moveMagnitude < MinMoveMagnitudeForFootsteps)
        {
            footstepTimer = 0f;
            StopFootstepSfx();
            return;
        }

        footstepTimer -= Time.deltaTime;

        if (footstepTimer <= 0f)
        {
            PlayFootstepSfx();
            footstepTimer = stepIntervalAtFullSpeed / moveMagnitude;
        }
    }

    /// <summary>
    /// Restarts the same AudioSource every step instead of PlayOneShot, and hard-stops
    /// it after footstepMaxDuration — keeps each step a short, punchy sound regardless
    /// of the source clip's own length, and lets UpdateFootsteps cut it off immediately
    /// the moment the player stops moving instead of letting it ring out.
    /// </summary>
    private void PlayFootstepSfx()
    {
        if (footstepSfx == null || footstepAudioSource == null)
        {
            return;
        }

        bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

        if (!sfxEnabled)
        {
            return;
        }

        if (footstepStopCoroutine != null)
        {
            StopCoroutine(footstepStopCoroutine);
        }

        footstepAudioSource.Stop();
        footstepAudioSource.clip = footstepSfx;
        footstepAudioSource.time = 0f;
        footstepAudioSource.Play();

        footstepStopCoroutine = StartCoroutine(StopFootstepSfxAfterDelay(footstepMaxDuration));
    }

    private void StopFootstepSfx()
    {
        if (footstepStopCoroutine != null)
        {
            StopCoroutine(footstepStopCoroutine);
            footstepStopCoroutine = null;
        }

        if (footstepAudioSource != null)
        {
            footstepAudioSource.Stop();
        }
    }

    private IEnumerator StopFootstepSfxAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        footstepAudioSource.Stop();
        footstepStopCoroutine = null;
    }
}