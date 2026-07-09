using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
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
    [SerializeField] private TMP_Text weaponNameText;

    private float nextFireTime;
    private GameObject[] weaponModelInstances;
    private Transform[] leftHandGripTransforms;
    private Transform[] rightHandGripTransforms;
    private Transform[] firePointTransforms;
    private bool isAiming;
    private readonly List<GameObject> activeMuzzleFlashes = new List<GameObject>();

    public bool IsAiming => isAiming;

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

    private void Start()
    {
        SpawnWeaponModels();
        UpdateWeaponUI();
    }

    private void LateUpdate()
    {
        SyncHandGripTransforms();
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

    public void StartShooting()
    {
        if (!enabled)
        {
            return;
        }

        isAiming = true;
        EquipWeaponVisual(currentWeaponIndex);

        animationController?.SetShooting(true);
        TryShoot();
    }

    public void StopShooting()
    {
        // Releasing Fire no longer resets the pose - the Shoot pose stays held until the
        // player actually moves (see ResetToIdlePose, called from PlayerMovement).
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
        foreach (GameObject muzzle in activeMuzzleFlashes)
        {
            if (muzzle != null)
            {
                Destroy(muzzle);
            }
        }

        activeMuzzleFlashes.Clear();
    }

    public void TryShoot()
    {
        if (!enabled || CurrentWeapon == null || handSocket == null)
        {
            return;
        }

        if (Time.time < nextFireTime)
        {
            return;
        }

        nextFireTime = Time.time + CurrentWeapon.fireRate;

        animationController?.PlayShootShot();

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

    private void FireHitscan(Vector3 direction)
    {
        Vector3 origin = GetMuzzlePosition();
        Vector3 endPoint = origin + direction * CurrentWeapon.range;

        if (Physics.SphereCast(origin, HitAssistRadius, direction, out RaycastHit hit, CurrentWeapon.range))
        {
            endPoint = hit.point;

            ZombieHealth zombieHealth = hit.collider.GetComponentInParent<ZombieHealth>();

            if (zombieHealth != null)
            {
                zombieHealth.TakeDamage(CurrentWeapon.damage);
            }

            if (CurrentWeapon.hitEffectPrefab != null)
            {
                Instantiate(CurrentWeapon.hitEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            }
        }

        DrawLaser(origin, endPoint);
    }

    private void DrawLaser(Vector3 start, Vector3 end)
    {
        GameObject laserObject = new GameObject("LaserShot");
        LineRenderer line = laserObject.AddComponent<LineRenderer>();

        line.material = GetLaserMaterial();
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 4;
        line.useWorldSpace = true;
        line.startColor = Color.red;
        line.endColor = Color.red;
        line.startWidth = 0.02f;
        line.endWidth = 0.02f;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);

        Destroy(laserObject, 0.05f);
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

        if (CurrentFirePoint != null)
        {
            muzzle = Instantiate(CurrentWeapon.muzzleFlashPrefab, CurrentFirePoint);
            muzzle.transform.localPosition = Vector3.zero;
            muzzle.transform.localEulerAngles = Vector3.zero;
        }
        else
        {
            muzzle = Instantiate(CurrentWeapon.muzzleFlashPrefab, handSocket);
            muzzle.transform.localPosition = CurrentWeapon.muzzleFlashLocalPosition;
            muzzle.transform.localEulerAngles = CurrentWeapon.muzzleFlashLocalEulerAngles;
        }

        muzzle.transform.localScale = CompensateForParentScale(CurrentWeapon.muzzleFlashScale, muzzle.transform.parent);

        ParticleSystem.MainModule main = muzzle.main;
        main.loop = false;
        muzzle.Play(true);

        activeMuzzleFlashes.RemoveAll(item => item == null);
        activeMuzzleFlashes.Add(muzzle.gameObject);

        Destroy(muzzle.gameObject, 0.15f);
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
        if (weapons == null || weapons.Length == 0)
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

    private void UpdateWeaponUI()
    {
        if (weaponNameText != null && CurrentWeapon != null)
        {
            weaponNameText.text = CurrentWeapon.weaponName;
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