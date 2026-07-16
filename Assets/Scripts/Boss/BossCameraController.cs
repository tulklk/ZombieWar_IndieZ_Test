using Cinemachine;
using UnityEngine;

/// <summary>
/// Owns the 3-camera boss-fight rig (CM_PlayerFollow / CM_BossIntro / CM_BossCombat, all
/// Cinemachine 2.x CinemachineVirtualCamera — this project pins com.unity.cinemachine
/// 2.10.7) by only ever changing Priority, never enabling/disabling GameObjects or
/// triggering blends by hand — CinemachineBrain (already on Main Camera) picks the
/// highest-priority vcam and blends to it automatically using its own configured default
/// blend (Ease In Out, 2s, already set on the Brain).
///
/// Boss framing is NOT hard-coded: ConfigureCameraForBoss(Renderer[]) measures the boss's
/// actual combined Renderer bounds ONCE (cached, never recomputed per frame) and uses that
/// to (a) place BossCameraTarget at the boss's real center, (b) position CM_BossIntro as a
/// one-time "hero shot" distance/height computed from boss height, and (c) configure
/// CM_BossCombat's Body as a CinemachineTransposer — the SAME component type and
/// BindingMode (LockToTargetWithWorldUp) as CM_PlayerFollow already uses, Follow set to the
/// Player — so Cinemachine's own native orbit-behind-the-target behavior keeps this camera
/// anchored around the Player exactly like the normal follow camera, with no custom
/// per-frame steering code fighting Cinemachine's internals. The FollowOffset's direction is
/// read live from CM_PlayerFollow's own offset (so tweaking that camera's angle by hand
/// keeps this one in sync) and only its magnitude is scaled up from the Boss's measured
/// size, so the Boss stays fully in frame without the shot ever centering on the Boss
/// instead of the Player.
/// </summary>
public class BossCameraController : MonoBehaviour
{
    [Header("Cameras (Cinemachine 2.x — CinemachineVirtualCamera)")]
    [SerializeField] private CinemachineVirtualCamera playerFollowCamera;
    [SerializeField] private CinemachineVirtualCamera bossIntroCamera;
    [SerializeField] private CinemachineVirtualCamera bossCombatCamera;

    [Header("Priority")]
    [SerializeField] private int lowPriority = 10;
    [SerializeField] private int activePriority = 20;

    [Header("Boss Combat Target Group (used for LookAt/Aim framing only — Body follows the Player)")]
    [SerializeField] private CinemachineTargetGroup bossCombatTargetGroup;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform bossCameraTarget;
    [SerializeField] private float playerGroupRadius = 1.2f;
    [Tooltip("How much more the Player pulls the Aim/composition centroid toward them than the Boss does — a much higher Player weight keeps the shot centered on the Player while the Boss's own radius still nudges Composer to keep it in frame.")]
    [SerializeField] private float playerFramingWeight = 5f;
    [SerializeField] private float bossFramingWeight = 1f;

    [Header("Camera Framing (auto from Boss size — see ConfigureCameraForBoss)")]
    [SerializeField] private float baseCameraHeight = 14f;
    [SerializeField] private float baseCameraDistance = 10f;
    [SerializeField] private float bossHeightMultiplier = 1.3f;
    [SerializeField] private float targetDistanceMultiplier = 0.35f;
    [SerializeField] private float minimumCameraDistance = 10f;
    [SerializeField] private float maximumCameraDistance = 30f;
    [SerializeField] private float minimumFOV = 45f;
    [SerializeField] private float maximumFOV = 72f;
    [SerializeField] private float cameraAdjustmentSpeed = 3f;
    [SerializeField] private float bossHeadPadding = 1.5f;

    [Header("Intro Camera Framing")]
    [SerializeField] private float introDistanceMultiplier = 1.6f;
    [SerializeField] private float introPitchAngleDegrees = 12f;
    [SerializeField] private float introHeightFraction = 0.55f;

    [Header("Roar Camera Shake (optional — needs a CinemachineImpulseSource)")]
    [SerializeField] private CinemachineImpulseSource roarImpulseSource;
    [SerializeField] private float roarImpulseForce = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    private Bounds bossBounds;
    private bool hasBossBounds;

    /// <summary>Combined bounds of every renderer passed in — Bounds.Encapsulate needs one starting bounds, so the first renderer seeds it.</summary>
    public Bounds CalculateBossBounds(Renderer[] renderers) => CalculateBossBounds(renderers, out _);

    /// <summary>
    /// Same as above, but also reports whether any renderer in the array was actually usable
    /// (non-null). A stale array — e.g. left over from a destroyed model after a prefab swap —
    /// can have Length &gt; 0 while every element is a dangling null reference; without this,
    /// the caller would otherwise treat the resulting default Bounds (center at world origin)
    /// as a genuine measurement and reposition BossCameraTarget to world (0,0,0) instead of
    /// leaving it alone.
    /// </summary>
    public Bounds CalculateBossBounds(Renderer[] renderers, out bool foundAnyRenderer)
    {
        foundAnyRenderer = false;

        if (renderers == null || renderers.Length == 0)
        {
            return default;
        }

        Bounds combined = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            if (!foundAnyRenderer)
            {
                combined = renderers[i].bounds;
                foundAnyRenderer = true;
            }
            else
            {
                combined.Encapsulate(renderers[i].bounds);
            }
        }

        return combined;
    }

    /// <summary>
    /// Called once (BossFightManager does this right when a boss fight is about to start —
    /// never per-frame). Measures the boss's size, repositions BossCameraTarget to its real
    /// center, and configures both CM_BossIntro (static one-time hero shot) and
    /// CM_BossCombat's CinemachineTransposer (Player-anchored, Boss-sized distance/FOV) to
    /// fit THIS boss's size — no hard-coded Follow Offset that would only look right for one
    /// specific boss model.
    ///
    /// referenceCollider (e.g. the Boss's own CapsuleCollider) is used ahead of Renderer bounds
    /// whenever assigned — a SkinnedMeshRenderer's live bounds are recomputed from the CURRENT
    /// animated bone poses, and a bad/mismatched-scale animation import (seen with some
    /// third-party Creature Pack-style clips, where a bone's baked position curve is off by two
    /// orders of magnitude) can make the mesh's bounds balloon to hundreds of units despite the
    /// character looking fine on screen — Collider.bounds is derived purely from the Collider's
    /// own fixed shape/Transform and is immune to that.
    /// </summary>
    public void ConfigureCameraForBoss(Renderer[] renderers, Collider referenceCollider = null)
    {
        if (referenceCollider != null)
        {
            bossBounds = referenceCollider.bounds;
            hasBossBounds = true;
        }
        else
        {
            bossBounds = CalculateBossBounds(renderers, out hasBossBounds);
        }

        if (!hasBossBounds)
        {
            Debug.LogWarning("[BossCameraController] No boss renderers/collider assigned — falling back to default camera framing (baseCameraDistance/baseCameraHeight).");
        }

        float bossHeight = hasBossBounds ? Mathf.Max(bossBounds.size.y, 0.5f) : baseCameraHeight * 0.15f;
        float bossWidth = hasBossBounds ? Mathf.Max(bossBounds.size.x, bossBounds.size.z) : 2f;
        Vector3 bossCenter = hasBossBounds ? bossBounds.center : (bossCameraTarget != null ? bossCameraTarget.position : transform.position);

        if (bossCameraTarget != null)
        {
            // A little above true center, biased toward the chest/head — never the
            // (possibly below-ground) root pivot, and never the feet.
            bossCameraTarget.position = bossCenter + Vector3.up * (bossHeight * 0.15f);
        }

        float sizeFactor = Mathf.Max(bossHeight, bossWidth);

        ConfigureTargetGroupRadius(sizeFactor);
        ConfigureCombatCamera(bossHeight);
        ConfigureIntroCamera(bossHeight, bossCameraTarget != null ? bossCameraTarget.position : bossCenter);

        if (showDebugLogs)
        {
            Debug.Log($"[BossCameraController] Configured for boss — height={bossHeight:0.0}, width={bossWidth:0.0}, center={bossCenter}.");
        }
    }

    private void ConfigureTargetGroupRadius(float sizeFactor)
    {
        if (bossCombatTargetGroup == null)
        {
            return;
        }

        CinemachineTargetGroup.Target[] targets = bossCombatTargetGroup.m_Targets;

        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (playerTransform != null && targets[i].target == playerTransform)
            {
                targets[i].weight = playerFramingWeight;
                targets[i].radius = playerGroupRadius;
            }
            else if (bossCameraTarget != null && targets[i].target == bossCameraTarget)
            {
                targets[i].weight = bossFramingWeight;
                targets[i].radius = Mathf.Max(sizeFactor * 0.5f, 0.5f);
            }
        }
    }

    private void ConfigureCombatCamera(float bossHeight)
    {
        if (bossCombatCamera == null)
        {
            return;
        }

        bossCombatCamera.Follow = playerTransform;
        bossCombatCamera.LookAt = bossCombatTargetGroup != null ? bossCombatTargetGroup.transform : playerTransform;

        // A stray CinemachineFramingTransposer from an earlier setup would otherwise sit
        // alongside the CinemachineTransposer added below on the same Body stage — removed
        // so exactly one Body component ever drives this vcam.
        CinemachineFramingTransposer staleFramingTransposer = bossCombatCamera.GetCinemachineComponent<CinemachineFramingTransposer>();

        if (staleFramingTransposer != null)
        {
            Destroy(staleFramingTransposer);
        }

        CinemachineTransposer transposer = bossCombatCamera.GetCinemachineComponent<CinemachineTransposer>();

        if (transposer == null)
        {
            transposer = bossCombatCamera.AddCinemachineComponent<CinemachineTransposer>();
        }

        // Same BindingMode as CM_PlayerFollow — the camera orbits to stay behind the Player
        // as they move/turn, natively, with no custom per-frame steering script needed.
        transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetWithWorldUp;

        float requiredDistance = Mathf.Clamp(
            bossHeight * bossHeightMultiplier + baseCameraDistance * targetDistanceMultiplier,
            minimumCameraDistance,
            maximumCameraDistance);

        // Same DIRECTION as CM_PlayerFollow's own offset (read live, so hand-tweaking that
        // camera's angle keeps this one matching) — only the MAGNITUDE is scaled out to
        // requiredDistance, far enough back to fit this specific Boss's measured size.
        Vector3 offsetDirection = GetPlayerFollowOffsetDirection();
        transposer.m_FollowOffset = offsetDirection * requiredDistance;

        float damping = 1f / Mathf.Max(cameraAdjustmentSpeed, 0.1f);
        transposer.m_XDamping = damping;
        transposer.m_YDamping = damping;
        transposer.m_ZDamping = damping;

        // Proper trig, not a guess: wide enough from requiredDistance to fit the Boss's own
        // measured height (plus a little head padding), with ~30% extra so it isn't cropped
        // right at the frame edge.
        float verticalExtent = (bossHeight * 0.5f) + bossHeadPadding;
        float requiredFOV = 2f * Mathf.Atan2(verticalExtent, requiredDistance) * Mathf.Rad2Deg * 1.3f;
        bossCombatCamera.m_Lens.FieldOfView = Mathf.Clamp(requiredFOV, minimumFOV, maximumFOV);
    }

    /// <summary>Reads CM_PlayerFollow's own CinemachineTransposer offset direction (ignoring its magnitude) so the combat camera matches whatever angle the Player camera is actually set to. Falls back to this project's known ~55° pitch ({0,10,-7}, normalized) if unavailable.</summary>
    private Vector3 GetPlayerFollowOffsetDirection()
    {
        if (playerFollowCamera != null)
        {
            CinemachineTransposer playerTransposer = playerFollowCamera.GetCinemachineComponent<CinemachineTransposer>();

            if (playerTransposer != null && playerTransposer.m_FollowOffset.sqrMagnitude > 0.01f)
            {
                return playerTransposer.m_FollowOffset.normalized;
            }
        }

        return new Vector3(0f, 0.819f, -0.574f);
    }

    private void ConfigureIntroCamera(float bossHeight, Vector3 lookTarget)
    {
        if (bossIntroCamera == null)
        {
            return;
        }

        bossIntroCamera.LookAt = bossCameraTarget != null ? bossCameraTarget : null;

        float introDistance = Mathf.Clamp(bossHeight * introDistanceMultiplier, minimumCameraDistance, maximumCameraDistance);
        float pitchRadians = introPitchAngleDegrees * Mathf.Deg2Rad;
        Vector3 backAndUp = new Vector3(0f, Mathf.Sin(pitchRadians), -Mathf.Cos(pitchRadians));

        bossIntroCamera.transform.position = lookTarget + Vector3.up * (bossHeight * introHeightFraction) + backAndUp * introDistance;
        bossIntroCamera.transform.rotation = Quaternion.LookRotation((lookTarget - bossIntroCamera.transform.position).normalized, Vector3.up);
    }

    public void SwitchToPlayerCamera()
    {
        SetPriorities(activePriority, lowPriority, lowPriority);
    }

    public void SwitchToBossIntroCamera()
    {
        SetPriorities(lowPriority, activePriority, lowPriority);
    }

    public void SwitchToBossCombatCamera()
    {
        SetPriorities(lowPriority, lowPriority, activePriority);
    }

    private void SetPriorities(int playerPriority, int introPriority, int combatPriority)
    {
        if (playerFollowCamera != null)
        {
            playerFollowCamera.Priority = playerPriority;
        }

        if (bossIntroCamera != null)
        {
            bossIntroCamera.Priority = introPriority;
        }

        if (bossCombatCamera != null)
        {
            bossCombatCamera.Priority = combatPriority;
        }
    }

    /// <summary>Called once when the boss roars — no-ops harmlessly if no CinemachineImpulseSource is assigned.</summary>
    public void PlayRoarShake()
    {
        if (roarImpulseSource != null)
        {
            roarImpulseSource.GenerateImpulse(roarImpulseForce);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (hasBossBounds)
        {
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.5f);
            Gizmos.DrawWireCube(bossBounds.center, bossBounds.size);
        }

        if (bossCameraTarget != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(bossCameraTarget.position, 0.3f);
        }

        if (bossIntroCamera != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(bossIntroCamera.transform.position, 0.5f);
        }

        if (bossCameraTarget != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            Gizmos.DrawWireSphere(bossCameraTarget.position, minimumCameraDistance);

            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(bossCameraTarget.position, maximumCameraDistance);
        }
    }
}
