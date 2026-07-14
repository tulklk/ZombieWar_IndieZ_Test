using UnityEngine;

/// <summary>
/// Confines the Player inside this GameObject's BoxCollider by clamping their position back
/// inside every frame, right after PlayerMovement's own CharacterController.Move() has
/// already applied that frame's input — so crossing the boundary is blocked the same frame
/// it would happen, not corrected a frame late (which would show as a brief pop/teleport).
/// Reads the collider's actual center/size in its own local space (not just its
/// axis-aligned world bounds), so it still works correctly if this GameObject is rotated.
/// Only X/Z are clamped — the SafeZone is a horizontal play-area boundary, not a
/// floor/ceiling, so vertical movement (jumping, slopes, stairs) is never touched.
/// The BoxCollider is only ever read for its center/size here — no physics collision
/// events are used — but isTrigger is still forced on so Unity's own physics resolution
/// never ALSO tries to push the Player out of what would otherwise be a solid volume they
/// stand inside of, fighting this script's own clamp.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class SafeZoneBoundary : MonoBehaviour
{
    [Tooltip("Auto-found via GameObject.FindGameObjectWithTag(\"Player\") in Awake if left empty.")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("Auto-found via playerTransform.GetComponent<CharacterController>() in Awake if left empty.")]
    [SerializeField] private CharacterController playerCharacterController;
    [Tooltip("Extra inward margin subtracted from the box's half-extents, so the Player is nudged back slightly before exactly touching the edge instead of sitting right on the boundary line.")]
    [SerializeField] private float boundaryMargin = 0.1f;

    private BoxCollider zoneCollider;

    private void Awake()
    {
        zoneCollider = GetComponent<BoxCollider>();
        zoneCollider.isTrigger = true;

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (player != null)
            {
                playerTransform = player.transform;
            }
        }

        if (playerCharacterController == null && playerTransform != null)
        {
            playerCharacterController = playerTransform.GetComponent<CharacterController>();
        }

        if (playerTransform == null)
        {
            Debug.LogWarning("[SafeZoneBoundary] Could not find the Player — the boundary will not be enforced.", this);
        }
    }

    private void LateUpdate()
    {
        if (playerTransform == null)
        {
            return;
        }

        Vector3 localPoint = transform.InverseTransformPoint(playerTransform.position) - zoneCollider.center;
        Vector3 halfExtents = (zoneCollider.size * 0.5f) - new Vector3(boundaryMargin, 0f, boundaryMargin);

        Vector3 clampedLocalPoint = new Vector3(
            Mathf.Clamp(localPoint.x, -halfExtents.x, halfExtents.x),
            localPoint.y,
            Mathf.Clamp(localPoint.z, -halfExtents.z, halfExtents.z));

        if (clampedLocalPoint == localPoint)
        {
            return;
        }

        Vector3 clampedWorldPoint = transform.TransformPoint(clampedLocalPoint + zoneCollider.center);

        if (playerCharacterController != null)
        {
            // Toggling enabled off/around a direct position set is the standard safe way to
            // reposition a CharacterController — setting transform.position alone while it's
            // enabled can get fought/overridden by its own internal state on the next Move().
            playerCharacterController.enabled = false;
            playerTransform.position = clampedWorldPoint;
            playerCharacterController.enabled = true;
        }
        else
        {
            playerTransform.position = clampedWorldPoint;
        }
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider box = GetComponent<BoxCollider>();

        if (box == null)
        {
            return;
        }

        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
