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

        // When this scene loads directly (Editor Play on Level1), Unity computes skybox-
        // sourced ambient/GI lighting normally. When it's loaded async from another scene
        // (MainMenu -> LoadingScene -> ... -> Level1), that environment lighting pass can
        // be stale/dark until something explicitly refreshes it — this forces the same
        // correct, bright result either way.
        DynamicGI.UpdateEnvironment();
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