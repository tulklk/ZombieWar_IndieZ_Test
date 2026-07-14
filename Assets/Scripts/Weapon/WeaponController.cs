using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

public class WeaponController : MonoBehaviour
{
    [Header("Weapons")]
    [SerializeField] private WeaponData[] weapons;
    [SerializeField] private int currentWeaponIndex;

    [Header("References")]
    [FormerlySerializedAs("firePoint")]
    [SerializeField] private Transform handSocket;
    [SerializeField] private Transform backSocket;
    [SerializeField] private PlayerAnimationController animationController;
    [Tooltip("Auto-found via GetComponent if left empty. Laser sight is hidden while this reports IsMoving.")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private TMP_Text weaponNameText;
    [SerializeField] private TMP_Text bulletCountMaxText;
    [SerializeField] private TMP_Text bulletCountCurrentText;
    [SerializeField] private Image gunIconImage;

    [Header("Reload")]
    [SerializeField] private float reloadDuration = 1.5f;

    [Header("Audio")]
    [Tooltip("Auto-added if left empty. Dedicated so each shot restarts (cuts off) the previous one instead of piling up at high fire rates.")]
    [SerializeField] private AudioSource fireAudioSource;
    [Tooltip("Hard cap on how long a single shot's SFX is audible, regardless of the source clip's own length — keeps every shot a short, punchy burst instead of playing a long clip out in full.")]
    [SerializeField] private float fireSfxMaxDuration = 0.25f;
    [Tooltip("Played once whenever a reload starts, shared across all weapons.")]
    [SerializeField] private AudioClip reloadSfx;

    private Coroutine fireSfxStopCoroutine;

    private float nextFireTime;
    private int[] currentMagazineAmmo;
    private int[] currentReserveAmmo;
    private GameObject[] weaponModelInstances;
    private Transform[] leftHandGripTransforms;
    private Transform[] rightHandGripTransforms;
    private Transform[] firePointTransforms;
    private bool isAiming;
    private bool isActionLocked;
    private bool isReloading;
    private Coroutine reloadCoroutine;
    private readonly List<ParticleSystem> activeMuzzleFlashes = new List<ParticleSystem>();
    private LineRenderer laserSightLine;

    public bool IsAiming => isAiming;
    public bool IsActionLocked => isActionLocked;
    public bool IsReloading => isReloading;

    public Transform CurrentLeftHandGrip =>
        (leftHandGripTransforms != null && currentWeaponIndex < leftHandGripTransforms.Length)
            ? leftHandGripTransforms[currentWeaponIndex]
            : null;

    public Transform CurrentRightHandGrip =>
        (rightHandGripTransforms != null && currentWeaponIndex < rightHandGripTransforms.Length)
            ? rightHandGripTransforms[currentWeaponIndex]
            : null;

    public Transform CurrentFirePoint =>
        (firePointTransforms != null && currentWeaponIndex < firePointTransforms.Length)
            ? firePointTransforms[currentWeaponIndex]
            : null;

    private WeaponData CurrentWeapon
    {
        get
        {
            if (weapons == null || weapons.Length == 0)
            {
                return null;
            }

            return weapons[currentWeaponIndex];
        }
    }

    private void Awake()
    {
        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
        }

        if (fireAudioSource == null)
        {
            fireAudioSource = GetComponent<AudioSource>();
        }

        if (fireAudioSource == null)
        {
            fireAudioSource = gameObject.AddComponent<AudioSource>();
        }

        fireAudioSource.playOnAwake = false;
        fireAudioSource.loop = false;
        fireAudioSource.spatialBlend = 0f;
    }

    private void Start()
    {
        InitializeAmmo();
        SpawnWeaponModels();
        UpdateWeaponUI();
    }

    /// <summary>
    /// Restarts the same AudioSource every shot instead of PlayOneShot — at high fire
    /// rates PlayOneShot stacks multiple full-length copies on top of each other and
    /// the result smears into one long noise. Restarting cuts the previous shot's tail
    /// immediately, so each trigger pull reads as one short, punchy report.
    /// Also hard-stopped after fireSfxMaxDuration regardless of the clip's own length —
    /// some source clips (e.g. the shotgun's) contain more than one bang baked into the
    /// same file, which would otherwise play out in full on every slow-fire-rate shot.
    /// </summary>
    private void PlayFireSfx()
    {
        if (CurrentWeapon.fireSfx == null || fireAudioSource == null)
        {
            return;
        }

        bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

        if (!sfxEnabled)
        {
            return;
        }

        if (fireSfxStopCoroutine != null)
        {
            StopCoroutine(fireSfxStopCoroutine);
        }

        fireAudioSource.Stop();
        fireAudioSource.clip = CurrentWeapon.fireSfx;
        fireAudioSource.time = 0f;
        fireAudioSource.Play();

        fireSfxStopCoroutine = StartCoroutine(StopFireSfxAfterDelay(fireSfxMaxDuration));
    }

    private IEnumerator StopFireSfxAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        fireAudioSource.Stop();
        fireSfxStopCoroutine = null;
    }

    private void InitializeAmmo()
    {
        if (weapons == null)
        {
            return;
        }

        currentMagazineAmmo = new int[weapons.Length];
        currentReserveAmmo = new int[weapons.Length];

        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] == null)
            {
                continue;
            }

            currentMagazineAmmo[i] = weapons[i].magazineSize;
            currentReserveAmmo[i] = Mathf.Max(0, weapons[i].maxAmmo - weapons[i].magazineSize);
        }
    }

    private void LateUpdate()
    {
        SyncHandGripTransforms();
        UpdateLaserSight();
    }

    private void OnDestroy()
    {
        if (laserSightLine != null)
        {
            Destroy(laserSightLine.gameObject);
        }
    }

    private void OnDisable()
    {
        if (laserSightLine != null)
        {
            laserSightLine.enabled = false;
        }
    }

    private void SyncHandGripTransforms()
    {
        if (weapons == null)
        {
            return;
        }

        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] == null)
            {
                continue;
            }

            if (leftHandGripTransforms != null && leftHandGripTransforms[i] != null)
            {
                leftHandGripTransforms[i].localPosition = weapons[i].leftHandGripLocalPosition;
                leftHandGripTransforms[i].localEulerAngles = weapons[i].leftHandGripLocalEulerAngles;
            }

            if (rightHandGripTransforms != null && rightHandGripTransforms[i] != null)
            {
                rightHandGripTransforms[i].localPosition = weapons[i].rightHandGripLocalPosition;
                rightHandGripTransforms[i].localEulerAngles = weapons[i].rightHandGripLocalEulerAngles;
            }

            if (firePointTransforms != null && firePointTransforms[i] != null)
            {
                firePointTransforms[i].localPosition = weapons[i].firePointLocalPosition;
                firePointTransforms[i].localEulerAngles = weapons[i].firePointLocalEulerAngles;
            }
        }
    }

    private void SpawnWeaponModels()
    {
        if (weapons == null || weapons.Length == 0)
        {
            return;
        }

        weaponModelInstances = new GameObject[weapons.Length];
        leftHandGripTransforms = new Transform[weapons.Length];
        rightHandGripTransforms = new Transform[weapons.Length];
        firePointTransforms = new Transform[weapons.Length];

        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] == null || weapons[i].weaponModelPrefab == null)
            {
                continue;
            }

            try
            {
                weaponModelInstances[i] = Instantiate(weapons[i].weaponModelPrefab);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to spawn weapon model for {weapons[i].weaponName}: {e.Message}");
                continue;
            }

            GameObject leftGripObject = new GameObject("LeftHandGrip");
            leftGripObject.transform.SetParent(weaponModelInstances[i].transform, false);
            leftGripObject.transform.localPosition = weapons[i].leftHandGripLocalPosition;
            leftGripObject.transform.localEulerAngles = weapons[i].leftHandGripLocalEulerAngles;
            leftHandGripTransforms[i] = leftGripObject.transform;

            GameObject rightGripObject = new GameObject("RightHandGrip");
            rightGripObject.transform.SetParent(weaponModelInstances[i].transform, false);
            rightGripObject.transform.localPosition = weapons[i].rightHandGripLocalPosition;
            rightGripObject.transform.localEulerAngles = weapons[i].rightHandGripLocalEulerAngles;
            rightHandGripTransforms[i] = rightGripObject.transform;

            GameObject firePointObject = new GameObject("FirePoint");
            firePointObject.transform.SetParent(weaponModelInstances[i].transform, false);
            firePointObject.transform.localPosition = weapons[i].firePointLocalPosition;
            firePointObject.transform.localEulerAngles = weapons[i].firePointLocalEulerAngles;
            firePointTransforms[i] = firePointObject.transform;

            HolsterWeaponVisual(i);
        }
    }

    private void EquipWeaponVisual(int index)
    {
        if (weaponModelInstances == null || index < 0 || index >= weaponModelInstances.Length)
        {
            return;
        }

        GameObject model = weaponModelInstances[index];

        if (model == null || handSocket == null)
        {
            return;
        }

        WeaponData data = weapons[index];
        model.transform.SetParent(handSocket, false);
        model.transform.localPosition = data.handLocalPosition;
        model.transform.localEulerAngles = data.handLocalEulerAngles;
        model.transform.localScale = data.modelLocalScale;
    }

    private void HolsterWeaponVisual(int index)
    {
        if (weaponModelInstances == null || index < 0 || index >= weaponModelInstances.Length)
        {
            return;
        }

        GameObject model = weaponModelInstances[index];

        if (model == null || backSocket == null)
        {
            return;
        }

        WeaponData data = weapons[index];
        model.transform.SetParent(backSocket, false);
        model.transform.localPosition = data.backLocalPosition;
        model.transform.localEulerAngles = data.backLocalEulerAngles;
        model.transform.localScale = data.modelLocalScale;
    }

    public void HideWeaponModels()
    {
        if (weaponModelInstances == null)
        {
            return;
        }

        foreach (GameObject model in weaponModelInstances)
        {
            if (model != null)
            {
                model.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Locks/unlocks firing, aiming and weapon switching without disabling the whole component
    /// (so weapon state such as the equipped index and hand-grip rig is preserved).
    /// Used while the player is performing a non-shooting action, e.g. throwing a bomb.
    /// </summary>
    public void SetActionLocked(bool locked)
    {
        isActionLocked = locked;

        if (isActionLocked)
        {
            CancelReload();
            ResetToIdlePose();
        }
    }

    public void StartShooting()
    {
        if (!enabled || isActionLocked)
        {
            return;
        }

        isAiming = true;
        EquipWeaponVisual(currentWeaponIndex);

        animationController?.SetShooting(true);

        // Firing itself is driven entirely by the caller's per-frame hold loop (see
        // FireButtonTest.Update) — calling TryShoot() here too used to fire an extra,
        // immediate shot on top of that loop's own first tick, so a single tap/click
        // (which routinely lasts longer than a fast weapon's fireRate) fired twice.
    }

    public void StopShooting()
    {
        // Releasing Fire no longer holsters the weapon — once drawn, it stays raised
        // (and the player can walk/run freely) until the player explicitly taps
        // BulletBoard, which is wired directly to ResetToIdlePose() in the scene.
    }

    public void ResetToIdlePose()
    {
        if (!isAiming)
        {
            return;
        }

        isAiming = false;
        HolsterWeaponVisual(currentWeaponIndex);
        StopAllMuzzleFlashes();

        animationController?.SetShooting(false);
    }

    private void StopAllMuzzleFlashes()
    {
        foreach (ParticleSystem muzzle in activeMuzzleFlashes)
        {
            if (muzzle != null)
            {
                EffectPoolManager.Instance.ReleaseEarly(muzzle);
            }
        }

        activeMuzzleFlashes.Clear();
    }

    public void TryShoot()
    {
        if (!enabled || isActionLocked || isReloading || CurrentWeapon == null || handSocket == null)
        {
            return;
        }

        if (Time.time < nextFireTime)
        {
            return;
        }

        if (currentMagazineAmmo == null)
        {
            return;
        }

        if (currentMagazineAmmo[currentWeaponIndex] <= 0)
        {
            TryReload();
            return;
        }

        nextFireTime = Time.time + CurrentWeapon.fireRate;

        currentMagazineAmmo[currentWeaponIndex]--;
        UpdateWeaponUI();

        animationController?.PlayShootShot();
        PlayFireSfx();

        if (CurrentWeapon.isShotgun)
        {
            ShootShotgun();
        }
        else
        {
            ShootSingle();
        }

        PlayMuzzleFlash();
        StartCoroutine(PlayRecoil());

        if (currentMagazineAmmo[currentWeaponIndex] <= 0)
        {
            TryReload();
        }
    }

    private const float HitAssistRadius = 0.35f;

    private void ShootSingle()
    {
        FireHitscan(GetMuzzleForward());
    }

    private void ShootShotgun()
    {
        for (int i = 0; i < CurrentWeapon.pelletCount; i++)
        {
            Vector3 direction = GetSpreadDirection();
            FireHitscan(direction);
        }
    }

    private Vector3 GetSpreadDirection()
    {
        float spreadX = Random.Range(-CurrentWeapon.spreadAngle, CurrentWeapon.spreadAngle);
        Quaternion spreadRotation = Quaternion.Euler(0f, spreadX, 0f);

        return spreadRotation * GetMuzzleForward();
    }

    private Vector3 GetMuzzlePosition()
    {
        return CurrentFirePoint != null
            ? CurrentFirePoint.position
            : handSocket.TransformPoint(CurrentWeapon.muzzleFlashLocalPosition);
    }

    private Vector3 GetMuzzleForward()
    {
        return transform.forward;
    }

    private const float MuzzleBacktrack = 0.5f;

    private void FireHitscan(Vector3 direction)
    {
        Vector3 muzzlePosition = GetMuzzlePosition();
        int hitMask = ~(1 << gameObject.layer);

        if (TryHitPointBlank(muzzlePosition, direction, hitMask))
        {
            return;
        }

        // Start the sweep slightly behind the muzzle: SphereCast does not report a hit for
        // colliders that already overlap the sphere at the start of the sweep, so a Zombie
        // standing point-blank against the muzzle would otherwise be missed entirely.
        Vector3 castOrigin = muzzlePosition - direction * MuzzleBacktrack;
        float castDistance = CurrentWeapon.range + MuzzleBacktrack;

        if (Physics.SphereCast(castOrigin, HitAssistRadius, direction, out RaycastHit hit, castDistance, hitMask))
        {
            ApplyHit(hit.collider, hit.point, hit.normal);
        }
    }

    /// <summary>
    /// SphereCast can't detect a collider that already overlaps the sweep's start sphere at
    /// t=0 — a documented Unity limitation, not something the MuzzleBacktrack offset can work
    /// around. ZombieAI only re-checks distance/attack-state on a 0.2-0.7s timer (RunAiTick),
    /// so a fast zombie (ZombieRunner/ZombieTank) can keep closing between ticks and end up
    /// flush against the muzzle before it stops to attack. This explicit overlap check right
    /// at the muzzle catches that point-blank case the sweep structurally cannot.
    /// </summary>
    private bool TryHitPointBlank(Vector3 muzzlePosition, Vector3 direction, int hitMask)
    {
        Collider[] overlaps = Physics.OverlapSphere(muzzlePosition, HitAssistRadius, hitMask);

        foreach (Collider overlapped in overlaps)
        {
            if (overlapped.GetComponentInParent<ZombieHealth>() != null)
            {
                Vector3 hitPoint = overlapped.ClosestPoint(muzzlePosition);
                ApplyHit(overlapped, hitPoint, -direction);
                return true;
            }
        }

        return false;
    }

    private void ApplyHit(Collider hitCollider, Vector3 hitPoint, Vector3 hitNormal)
    {
        ZombieHealth zombieHealth = hitCollider.GetComponentInParent<ZombieHealth>();

        if (zombieHealth != null)
        {
            zombieHealth.TakeDamage(CurrentWeapon.damage, hitPoint, hitNormal);
        }

        if (CurrentWeapon.hitEffectPrefab != null)
        {
            const float hitEffectLifetime = 2f;
            EffectPoolManager.Instance.Spawn(
                CurrentWeapon.hitEffectPrefab, hitPoint, Quaternion.LookRotation(hitNormal), hitEffectLifetime);
        }
    }

    private void UpdateLaserSight()
    {
        bool isMoving = playerMovement != null && playerMovement.IsMoving;

        if (!isAiming || isReloading || isMoving || CurrentWeapon == null || handSocket == null)
        {
            if (laserSightLine != null)
            {
                laserSightLine.enabled = false;
            }

            return;
        }

        if (laserSightLine == null)
        {
            laserSightLine = CreateLaserSightLine();
        }

        Vector3 muzzlePosition = GetMuzzlePosition();
        Vector3 direction = GetMuzzleForward();
        Vector3 endPoint = muzzlePosition + direction * CurrentWeapon.range;
        int hitMask = ~(1 << gameObject.layer);

        Collider[] overlaps = Physics.OverlapSphere(muzzlePosition, HitAssistRadius, hitMask);
        Collider pointBlankCollider = null;

        foreach (Collider overlapped in overlaps)
        {
            if (overlapped.GetComponentInParent<ZombieHealth>() != null)
            {
                pointBlankCollider = overlapped;
                break;
            }
        }

        if (pointBlankCollider != null)
        {
            endPoint = pointBlankCollider.ClosestPoint(muzzlePosition);
        }
        else
        {
            Vector3 castOrigin = muzzlePosition - direction * MuzzleBacktrack;
            float castDistance = CurrentWeapon.range + MuzzleBacktrack;

            if (Physics.SphereCast(castOrigin, HitAssistRadius, direction, out RaycastHit hit, castDistance, hitMask))
            {
                endPoint = hit.point;
            }
        }

        laserSightLine.enabled = true;
        laserSightLine.SetPosition(0, muzzlePosition);
        laserSightLine.SetPosition(1, endPoint);
    }

    private LineRenderer CreateLaserSightLine()
    {
        GameObject laserObject = new GameObject("LaserSight");
        LineRenderer line = laserObject.AddComponent<LineRenderer>();

        line.material = GetLaserMaterial();
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 4;
        line.useWorldSpace = true;
        Color laserColor = new Color(1f, 0f, 0f, 0.35f);
        line.startColor = laserColor;
        line.endColor = laserColor;
        line.startWidth = 0.06f;
        line.endWidth = 0.06f;
        line.positionCount = 2;
        line.sortingOrder = 10;
        line.enabled = false;

        return line;
    }

    private static Vector3 CompensateForParentScale(Vector3 desiredWorldScale, Transform parent)
    {
        if (parent == null)
        {
            return desiredWorldScale;
        }

        Vector3 parentLossyScale = parent.lossyScale;

        return new Vector3(
            desiredWorldScale.x / Mathf.Max(Mathf.Abs(parentLossyScale.x), 0.0001f),
            desiredWorldScale.y / Mathf.Max(Mathf.Abs(parentLossyScale.y), 0.0001f),
            desiredWorldScale.z / Mathf.Max(Mathf.Abs(parentLossyScale.z), 0.0001f));
    }

    private static Material laserMaterial;

    private static Material GetLaserMaterial()
    {
        if (laserMaterial == null)
        {
            laserMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        return laserMaterial;
    }

    private void PlayMuzzleFlash()
    {
        if (CurrentWeapon.muzzleFlashPrefab == null || handSocket == null)
        {
            return;
        }

        ParticleSystem muzzle;
        const float muzzleFlashLifetime = 0.15f;

        if (CurrentFirePoint != null)
        {
            muzzle = EffectPoolManager.Instance.SpawnLocal(
                CurrentWeapon.muzzleFlashPrefab, CurrentFirePoint, Vector3.zero, Vector3.zero, muzzleFlashLifetime);
        }
        else
        {
            muzzle = EffectPoolManager.Instance.SpawnLocal(
                CurrentWeapon.muzzleFlashPrefab, handSocket,
                CurrentWeapon.muzzleFlashLocalPosition, CurrentWeapon.muzzleFlashLocalEulerAngles, muzzleFlashLifetime);
        }

        muzzle.transform.localScale = CompensateForParentScale(CurrentWeapon.muzzleFlashScale, muzzle.transform.parent);

        ParticleSystem.MainModule main = muzzle.main;
        main.loop = false;

        activeMuzzleFlashes.RemoveAll(item => item == null);
        activeMuzzleFlashes.Add(muzzle);
    }

    private IEnumerator PlayRecoil()
    {
        int weaponIndex = currentWeaponIndex;
        WeaponData data = CurrentWeapon;

        GameObject model = (weaponModelInstances != null && weaponIndex < weaponModelInstances.Length)
            ? weaponModelInstances[weaponIndex]
            : null;

        if (model == null || data == null)
        {
            yield break;
        }

        Transform modelTransform = model.transform;
        Vector3 restLocalPosition = data.handLocalPosition;

        modelTransform.localPosition = restLocalPosition - new Vector3(0f, 0f, data.recoilDistance);

        while (weaponIndex == currentWeaponIndex && Vector3.Distance(modelTransform.localPosition, restLocalPosition) > 0.001f)
        {
            modelTransform.localPosition = Vector3.Lerp(
                modelTransform.localPosition,
                restLocalPosition,
                Time.deltaTime * data.recoilReturnSpeed
            );

            yield return null;
        }

        if (weaponIndex == currentWeaponIndex)
        {
            modelTransform.localPosition = restLocalPosition;
        }
    }

    public void SwitchWeapon()
    {
        if (weapons == null || weapons.Length == 0 || isActionLocked || isReloading)
        {
            return;
        }

        int previousWeaponIndex = currentWeaponIndex;

        currentWeaponIndex++;
        currentWeaponIndex %= weapons.Length;

        HolsterWeaponVisual(previousWeaponIndex);

        if (isAiming)
        {
            EquipWeaponVisual(currentWeaponIndex);
        }

        UpdateWeaponUI();

        Debug.Log("Switched weapon: " + CurrentWeapon.weaponName);
    }

    public void TryReload()
    {
        if (weapons == null || weapons.Length == 0 || isActionLocked || isReloading || currentMagazineAmmo == null)
        {
            return;
        }

        int index = currentWeaponIndex;
        int missingAmmo = CurrentWeapon.magazineSize - currentMagazineAmmo[index];

        if (missingAmmo <= 0 || currentReserveAmmo[index] <= 0)
        {
            return;
        }

        isReloading = true;

        isAiming = true;
        EquipWeaponVisual(index);

        animationController?.PlayReload();
        PlayReloadSfx();

        reloadCoroutine = StartCoroutine(ReloadRoutine(index));
    }

    private void PlayReloadSfx()
    {
        if (reloadSfx == null || fireAudioSource == null)
        {
            return;
        }

        bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

        if (sfxEnabled)
        {
            fireAudioSource.PlayOneShot(reloadSfx);
        }
    }

    private IEnumerator ReloadRoutine(int weaponIndex)
    {
        yield return new WaitForSeconds(reloadDuration);

        int missingAmmo = weapons[weaponIndex].magazineSize - currentMagazineAmmo[weaponIndex];
        int amountToLoad = Mathf.Min(missingAmmo, currentReserveAmmo[weaponIndex]);

        currentMagazineAmmo[weaponIndex] += amountToLoad;
        currentReserveAmmo[weaponIndex] -= amountToLoad;

        if (weaponIndex == currentWeaponIndex)
        {
            UpdateWeaponUI();
        }

        isReloading = false;
        reloadCoroutine = null;

        animationController?.EndReload();
    }

    private void CancelReload()
    {
        if (!isReloading)
        {
            return;
        }

        if (reloadCoroutine != null)
        {
            StopCoroutine(reloadCoroutine);
            reloadCoroutine = null;
        }

        isReloading = false;
        animationController?.EndReload();
    }

    private void UpdateWeaponUI()
    {
        if (CurrentWeapon == null)
        {
            return;
        }

        if (weaponNameText != null)
        {
            weaponNameText.text = CurrentWeapon.weaponName;
        }

        if (bulletCountMaxText != null && currentReserveAmmo != null)
        {
            bulletCountMaxText.text = currentReserveAmmo[currentWeaponIndex].ToString();
        }

        if (bulletCountCurrentText != null && currentMagazineAmmo != null)
        {
            bulletCountCurrentText.text = currentMagazineAmmo[currentWeaponIndex].ToString();
        }

        if (gunIconImage != null && CurrentWeapon.hudIcon != null)
        {
            gunIconImage.sprite = CurrentWeapon.hudIcon;
        }
    }

    private void OnDrawGizmos()
    {
        Transform firePointTransform = CurrentFirePoint;

        if (firePointTransform == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(firePointTransform.position, 0.02f);
        Gizmos.DrawLine(firePointTransform.position, firePointTransform.position + firePointTransform.forward * 0.3f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(firePointTransform.position + Vector3.up * 0.03f, "FirePoint (muzzle)");
#endif
    }
}