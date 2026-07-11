using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Zombie War/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Basic")]
    public string weaponName;
    public int damage = 10;
    public float fireRate = 0.2f;
    public float range = 30f;

    [Header("Ammo")]
    public int maxAmmo = 120;
    public int magazineSize = 30;
    public Sprite hudIcon;

    [Header("Projectile")]
    public GameObject bulletPrefab;
    public float bulletSpeed = 25f;
    public float bulletLifeTime = 3f;

    [Header("Shotgun")]
    public bool isShotgun;
    public int pelletCount = 6;
    public float spreadAngle = 8f;

    [Header("Recoil")]
    public float recoilDistance = 0.08f;
    public float recoilReturnSpeed = 12f;

    [Header("VFX")]
    public ParticleSystem muzzleFlashPrefab;
    public Vector3 muzzleFlashLocalPosition;
    public Vector3 muzzleFlashLocalEulerAngles;
    public Vector3 muzzleFlashScale = Vector3.one;
    public ParticleSystem hitEffectPrefab;

    [Header("Weapon Model")]
    public GameObject weaponModelPrefab;
    public Vector3 modelLocalScale = Vector3.one;
    public Vector3 handLocalPosition;
    public Vector3 handLocalEulerAngles;
    public Vector3 backLocalPosition;
    public Vector3 backLocalEulerAngles;

    [Header("Left Hand IK")]
    public Vector3 leftHandGripLocalPosition;
    public Vector3 leftHandGripLocalEulerAngles;

    [Header("Right Hand IK")]
    public Vector3 rightHandGripLocalPosition;
    public Vector3 rightHandGripLocalEulerAngles;

    [Header("Fire Point (muzzle tip, relative to the weapon model)")]
    public Vector3 firePointLocalPosition;
    public Vector3 firePointLocalEulerAngles;
}