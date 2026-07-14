using UnityEngine;

/// <summary>
/// Optional shared config asset for a zombie type — assign one to ZombieAI/ZombieHealth's
/// zombieData field and its values override whatever is set inline on the prefab in
/// Awake(). Lets you tune (or reuse) a zombie type's stats from one asset instead of
/// editing every prefab by hand, the same way WeaponData already drives WeaponController.
/// </summary>
[CreateAssetMenu(fileName = "NewZombieData", menuName = "Zombie War/Zombie Data")]
public class ZombieData : ScriptableObject
{
    [Header("Boss Display (optional — read by BossIntroUI/BossFightManager when this zombie type is used as a boss; regular zombies can leave these blank)")]
    public string bossName;
    public string bossType;

    [Header("Detection")]
    public float detectionRange = 8f;
    public float loseTargetRange = 12f;

    [Header("Patrol")]
    public float patrolRadius = 6f;
    public float patrolWalkSpeed = 1.2f;
    public float idleTimeMin = 1.2f;
    public float idleTimeMax = 2.5f;

    [Header("Chase")]
    public float chaseWalkSpeed = 1.6f;
    public float runSpeed = 3.4f;
    public float runRampUpTime = 2.5f;

    [Header("Attack")]
    public float attackRange = 1.6f;
    public float attackCooldown = 1.2f;
    public int damage = 10;
    [Tooltip("Only used by ZombieTank — delay between the attack trigger firing and the hit actually landing.")]
    public float attackWindup = 0.4f;

    [Header("AI Performance")]
    [Tooltip("How often (seconds) detection + NavMesh destination refresh while near the player.")]
    public float nearTickInterval = 0.2f;
    [Tooltip("How often (seconds) detection + NavMesh destination refresh while far from the player.")]
    public float farTickInterval = 0.7f;
    [Tooltip("Distance under which the faster (near) tick interval is used.")]
    public float nearDistanceThreshold = 10f;

    [Header("Audio")]
    [Tooltip("Played once, positionally, the moment this zombie spots the player.")]
    public AudioClip attackSfx;

    [Header("Health")]
    public int maxHealth = 50;

    [Header("Score")]
    [Tooltip("Added to the run's total score when this zombie type dies — tune per type (e.g. a Tank should be worth more than a base Zombie).")]
    public int scoreValue = 10;

    [Header("Hit Effect")]
    public Color hitColor = new Color(1f, 0.25f, 0f, 1f);
    public Color bloodStainColor = new Color(0.5f, 0f, 0f, 1f);
    public float flashDuration = 0.3f;

    [Header("Blood VFX")]
    public ParticleSystem floorSplatPrefab;
    public ParticleSystem directionalSplatPrefab;
    public LayerMask groundLayerMask;

    [Header("Death")]
    public float destroyDelay = 2.5f;
}
