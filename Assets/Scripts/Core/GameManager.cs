using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Level Settings")]
    [SerializeField] private float levelDuration = 180f;

    public float RemainingTime { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        RemainingTime = levelDuration;
    }

    private void Update()
    {
        RemainingTime -= Time.deltaTime;

        if (RemainingTime <= 0)
        {
            RemainingTime = 0;
            Debug.Log("Level Complete!");
        }
    }
}