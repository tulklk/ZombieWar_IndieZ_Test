using UnityEngine;

public class ZombieSpawner : MonoBehaviour
{
    [Header("Zombie Prefabs")]
    [SerializeField] private GameObject[] zombiePrefabs;

    [Header("Spawn Points")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Spawn Settings")]
    [SerializeField] private float startSpawnInterval = 3f;
    [SerializeField] private float minSpawnInterval = 0.8f;
    [SerializeField] private float difficultyIncreaseTime = 20f;
    [SerializeField] private int maxZombiesAlive = 25;

    private float currentSpawnInterval;
    private float spawnTimer;
    private float difficultyTimer;

    private void Start()
    {
        currentSpawnInterval = startSpawnInterval;
    }

    private void Update()
    {
        spawnTimer += Time.deltaTime;
        difficultyTimer += Time.deltaTime;

        if (spawnTimer >= currentSpawnInterval)
        {
            spawnTimer = 0f;
            SpawnZombie();
        }

        if (difficultyTimer >= difficultyIncreaseTime)
        {
            difficultyTimer = 0f;
            IncreaseDifficulty();
        }
    }

    private void SpawnZombie()
    {
        if (zombiePrefabs == null || zombiePrefabs.Length == 0 || spawnPoints == null || spawnPoints.Length == 0)
        {
            return;
        }

        if (ZombieHealth.ActiveCount >= maxZombiesAlive)
        {
            return;
        }

        GameObject zombiePrefab = zombiePrefabs[Random.Range(0, zombiePrefabs.Length)];
        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];

        GameObject zombie = Instantiate(
            zombiePrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        zombie.tag = "Zombie";
    }

    private void IncreaseDifficulty()
    {
        currentSpawnInterval -= 0.3f;
        currentSpawnInterval = Mathf.Max(currentSpawnInterval, minSpawnInterval);

        Debug.Log("Zombie spawn interval: " + currentSpawnInterval);
    }
}