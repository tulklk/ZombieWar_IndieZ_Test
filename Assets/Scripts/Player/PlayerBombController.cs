using System.Collections;
using UnityEngine;

public class PlayerBombController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform bombHandSocket;
    [SerializeField] private Transform throwOrigin;
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private BombTrajectoryPreview trajectoryPreview;

    [Header("Throw Settings")]
    [SerializeField] private float forwardForce = 9f;
    [SerializeField] private float upwardForce = 4f;
    [SerializeField] private float cooldown = 4f;
    [SerializeField] private bool lockMovementWhileThrowing = false;
    [SerializeField] private Vector3 heldBombLocalEulerAngles;
    [SerializeField] private float maxThrowStateDuration = 3f;

    [Header("Throw Timing (fallback if the clip has no SpawnBombInHand/ReleaseBomb animation events)")]
    [SerializeField] private float spawnInHandDelay = 0.3f;
    [SerializeField] private float releaseDelay = 0.6f;
    [SerializeField] private float finishDelay = 1.2f;

    [Header("Bomb Settings")]
    [SerializeField] private int damage = 50;
    [SerializeField] private float explosionRadius = 4f;
    [SerializeField] private float fuseTime = 2f;
    [SerializeField] private LayerMask zombieLayer;

    private static readonly int ThrowBombHash = Animator.StringToHash("ThrowBomb");
    private static readonly int IsThrowingBombHash = Animator.StringToHash("IsThrowingBomb");

    private bool isThrowingBomb;
    private bool bombInHandVisible;
    private float lastThrowTime = -Mathf.Infinity;
    private GameObject heldBombInstance;
    private Coroutine failsafeCoroutine;
    private Coroutine throwSequenceCoroutine;

    public bool IsThrowingBomb => isThrowingBomb;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
        }

        if (weaponController == null)
        {
            weaponController = GetComponent<WeaponController>();
        }

        if (playerHealth == null)
        {
            playerHealth = GetComponent<PlayerHealth>();
        }

        if (trajectoryPreview == null)
        {
            trajectoryPreview = GetComponentInChildren<BombTrajectoryPreview>();
        }
    }

    private void Update()
    {
        if (!bombInHandVisible)
        {
            return;
        }

        Vector3 origin = bombHandSocket != null ? bombHandSocket.position : transform.position;
        ShowTrajectoryPreview(origin);
    }

    /// <summary>Called on BombBtn press-down: shows the aiming trajectory before the throw actually starts.</summary>
    public void BeginAim()
    {
        if (isThrowingBomb || !CanThrowBomb())
        {
            return;
        }

        bombInHandVisible = true;
    }

    /// <summary>Called on BombBtn release (or if the button is disabled) without a committed throw.</summary>
    public void CancelAim()
    {
        if (isThrowingBomb)
        {
            return;
        }

        bombInHandVisible = false;
        trajectoryPreview?.HideArc();
    }

    public void TryThrowBomb()
    {
        if (!CanThrowBomb())
        {
            bombInHandVisible = false;
            trajectoryPreview?.HideArc();
            return;
        }

        isThrowingBomb = true;
        lastThrowTime = Time.time;

        animator.SetBool(IsThrowingBombHash, true);
        animator.SetTrigger(ThrowBombHash);

        weaponController?.SetActionLocked(true);

        if (lockMovementWhileThrowing && playerMovement != null)
        {
            playerMovement.enabled = false;
        }

        if (failsafeCoroutine != null)
        {
            StopCoroutine(failsafeCoroutine);
        }

        failsafeCoroutine = StartCoroutine(FailsafeFinishAfterTimeout());

        if (throwSequenceCoroutine != null)
        {
            StopCoroutine(throwSequenceCoroutine);
        }

        throwSequenceCoroutine = StartCoroutine(ThrowSequence());
    }

    private IEnumerator ThrowSequence()
    {
        yield return new WaitForSeconds(spawnInHandDelay);
        SpawnBombInHand();
        bombInHandVisible = true;

        yield return new WaitForSeconds(Mathf.Max(0f, releaseDelay - spawnInHandDelay));
        bombInHandVisible = false;
        ReleaseBomb();

        yield return new WaitForSeconds(Mathf.Max(0f, finishDelay - releaseDelay));
        FinishBombThrow();
    }

    private void ShowTrajectoryPreview(Vector3 origin)
    {
        if (trajectoryPreview == null)
        {
            return;
        }

        Vector3 velocity = (transform.forward * forwardForce) + (Vector3.up * upwardForce);
        trajectoryPreview.ShowArc(origin, velocity);
    }

    private bool CanThrowBomb()
    {
        if (isThrowingBomb)
        {
            return false;
        }

        if (Time.time < lastThrowTime + cooldown)
        {
            return false;
        }

        if (bombPrefab == null || animator == null)
        {
            return false;
        }

        if (playerHealth != null && playerHealth.IsDead)
        {
            return false;
        }

        return true;
    }

    private IEnumerator FailsafeFinishAfterTimeout()
    {
        yield return new WaitForSeconds(maxThrowStateDuration);

        if (isThrowingBomb)
        {
            FinishBombThrow();
        }
    }

    /// <summary>Animation Event: called when the throw animation reaches the point where the bomb should appear in hand.</summary>
    public void SpawnBombInHand()
    {
        if (bombPrefab == null || bombHandSocket == null)
        {
            return;
        }

        if (heldBombInstance != null)
        {
            Destroy(heldBombInstance);
        }

        heldBombInstance = Instantiate(bombPrefab, bombHandSocket);
        heldBombInstance.transform.localPosition = Vector3.zero;
        heldBombInstance.transform.localRotation = Quaternion.Euler(heldBombLocalEulerAngles);

        if (heldBombInstance.TryGetComponent(out Rigidbody heldRigidbody))
        {
            heldRigidbody.isKinematic = true;
            heldRigidbody.detectCollisions = false;
        }

        Collider[] heldColliders = heldBombInstance.GetComponentsInChildren<Collider>();

        for (int i = 0; i < heldColliders.Length; i++)
        {
            heldColliders[i].enabled = false;
        }
    }

    /// <summary>Animation Event: called at the exact release frame of the throw animation.</summary>
    public void ReleaseBomb()
    {
        GameObject bombInstance = heldBombInstance;
        heldBombInstance = null;

        if (bombInstance == null)
        {
            return;
        }

        bombInstance.transform.SetParent(null, true);
        bombInstance.transform.position = throwOrigin != null ? throwOrigin.position : bombInstance.transform.position;

        Rigidbody bombRigidbody = bombInstance.GetComponent<Rigidbody>();

        if (bombRigidbody == null)
        {
            bombRigidbody = bombInstance.AddComponent<Rigidbody>();
        }

        bombRigidbody.isKinematic = false;
        bombRigidbody.detectCollisions = true;

        Collider[] bombColliders = bombInstance.GetComponentsInChildren<Collider>();

        if (bombColliders.Length == 0)
        {
            SphereCollider fallbackCollider = bombInstance.AddComponent<SphereCollider>();
            fallbackCollider.radius = 0.05f;
            bombColliders = new[] { (Collider)fallbackCollider };
        }

        for (int i = 0; i < bombColliders.Length; i++)
        {
            bombColliders[i].enabled = true;
        }

        IgnoreCollisionWithPlayer(bombColliders);

        Vector3 throwDirection = transform.forward;
        bombRigidbody.velocity = (throwDirection * forwardForce) + (Vector3.up * upwardForce);

        BombProjectile bombProjectile = bombInstance.GetComponent<BombProjectile>();

        if (bombProjectile == null)
        {
            bombProjectile = bombInstance.AddComponent<BombProjectile>();
        }

        bombProjectile.Initialize(damage, explosionRadius, fuseTime, zombieLayer);

        if (trajectoryPreview != null)
        {
            trajectoryPreview.HideArc();
            trajectoryPreview.StartTrackingFalling(bombInstance.transform);
            bombProjectile.AddExplodedListener(trajectoryPreview.StopTracking);
        }
    }

    private void IgnoreCollisionWithPlayer(Collider[] bombColliders)
    {
        Collider[] playerColliders = GetComponents<Collider>();

        for (int p = 0; p < playerColliders.Length; p++)
        {
            for (int b = 0; b < bombColliders.Length; b++)
            {
                Physics.IgnoreCollision(bombColliders[b], playerColliders[p], true);
            }
        }
    }

    /// <summary>Animation Event: called at the end of the throw animation.</summary>
    public void FinishBombThrow()
    {
        isThrowingBomb = false;
        bombInHandVisible = false;

        if (animator != null)
        {
            animator.SetBool(IsThrowingBombHash, false);
        }

        weaponController?.SetActionLocked(false);

        if (lockMovementWhileThrowing && playerMovement != null)
        {
            playerMovement.enabled = true;
        }

        if (heldBombInstance != null)
        {
            Destroy(heldBombInstance);
            heldBombInstance = null;
            trajectoryPreview?.Hide();
        }

        if (failsafeCoroutine != null)
        {
            StopCoroutine(failsafeCoroutine);
            failsafeCoroutine = null;
        }

        if (throwSequenceCoroutine != null)
        {
            StopCoroutine(throwSequenceCoroutine);
            throwSequenceCoroutine = null;
        }
    }
}
