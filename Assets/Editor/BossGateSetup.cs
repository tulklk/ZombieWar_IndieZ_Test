using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Connects the two, until-now-independent systems: closes a WaveBarrier across BossArena's
/// entrance (reusing the exact same WaveBarrier/NavMeshObstacle setup every other wave gate
/// already uses) and adds it to the LAST wave's own barriers[] — so it opens automatically
/// the moment that wave completes, via ZombieWaveManager's existing OpenAllBarriers logic, no
/// new code required. Also moves the "level complete" trigger: ZombieWaveManager finishing
/// all waves used to show the Win panel directly (wired by LevelResultSetup, before the Boss
/// existed) — that persistent listener is removed here, since BossFightManager.onBossDefeated
/// is now what should call LevelResultManager.ShowWinPanel (already wired by BossFightSetup).
/// Idempotent — safe to run more than once; only a freshly-created barrier's placeholder
/// position/size is ever touched, and listeners/array entries are only added/removed if not
/// already in the expected state.
/// </summary>
public static class BossGateSetup
{
    private const string UndoLabel = "Gate Boss Arena Behind Final Wave";
    private const string WaveBarrierLayerName = "WaveBarrier";
    private const string BarrierMaterialPath = "Assets/Materials/Environment/WaveBarrier.mat";
    private const string BarrierObjectName = "BossArenaEntranceBarrier";

    [MenuItem("Tools/Zombie War/Gate Boss Arena Behind Final Wave")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[BossGateSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        Transform waveSystemTransform = FindRootObject(activeScene, "WaveSystem");
        Transform bossArenaTransform = FindRootObject(activeScene, "BossArena");

        if (waveSystemTransform == null || bossArenaTransform == null)
        {
            Debug.LogError("[BossGateSetup] Could not find 'WaveSystem' and/or 'BossArena' in the active scene. Aborted.");
            return;
        }

        ZombieWaveManager waveManager = waveSystemTransform.GetComponentInChildren<ZombieWaveManager>(true);

        if (waveManager == null)
        {
            Debug.LogError("[BossGateSetup] Could not find a ZombieWaveManager under 'WaveSystem'. Aborted.");
            return;
        }

        Transform introTriggerTransform = FindImmediateChild(bossArenaTransform, "BossIntroTrigger");

        WaveBarrier barrierScript = BuildEntranceBarrier(bossArenaTransform, introTriggerTransform);

        AddBarrierToLastWave(waveManager, barrierScript);

        Transform levelResultManagerTransform = FindRootObject(activeScene, "LevelResultManager");
        LevelResultManager levelResultManager = levelResultManagerTransform != null
            ? levelResultManagerTransform.GetComponent<LevelResultManager>()
            : null;

        if (levelResultManager != null)
        {
            RemovePersistentListenerIfPresent(waveManager.OnAllWavesCompletedEvent, levelResultManager, nameof(LevelResultManager.ShowWinPanel));
        }

        BossFightManager fightManager = bossArenaTransform.GetComponentInChildren<BossFightManager>(true);

        if (fightManager == null)
        {
            Debug.LogWarning("[BossGateSetup] Could not find a BossFightManager under 'BossArena' — make sure Tools > Zombie War > Setup Boss Fight has been run, so onBossDefeated is wired to LevelResultManager.ShowWinPanel (that's what shows Victory now, not finishing the waves).");
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = barrierScript.gameObject;

        Debug.Log("[BossGateSetup] BossArena's entrance is now closed by 'BossArenaEntranceBarrier' and added to the final wave's own barriers — it'll open automatically once that wave completes. Finishing all waves no longer shows the Win panel by itself; only the Boss's death does now. Reposition/resize the barrier to match the real corridor, then save the scene (Ctrl+S).");
    }

    private static WaveBarrier BuildEntranceBarrier(Transform bossArenaTransform, Transform introTriggerTransform)
    {
        Transform existing = FindImmediateChild(bossArenaTransform, BarrierObjectName);
        bool isNew = existing == null;
        GameObject barrierObject;

        if (!isNew)
        {
            barrierObject = existing.gameObject;
        }
        else
        {
            barrierObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrierObject.name = BarrierObjectName;
            Undo.RegisterCreatedObjectUndo(barrierObject, UndoLabel);
            barrierObject.transform.SetParent(bossArenaTransform, false);

            // Placeholder only, positioned a few units in front of BossIntroTrigger (i.e.
            // between it and wherever the Player approaches from) — like every other wave
            // barrier's own placeholder, this MUST be dragged to match the real corridor.
            Vector3 basePosition = introTriggerTransform != null ? introTriggerTransform.localPosition : new Vector3(0f, 2f, 4f);
            barrierObject.transform.localPosition = basePosition - new Vector3(0f, 0f, 4f);
            barrierObject.transform.localScale = new Vector3(6f, 3f, 0.5f);
        }

        MeshRenderer meshRenderer = barrierObject.GetComponent<MeshRenderer>();

        if (meshRenderer != null)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BarrierMaterialPath);

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }
        }

        int waveBarrierLayer = LayerMask.NameToLayer(WaveBarrierLayerName);

        if (waveBarrierLayer >= 0)
        {
            barrierObject.layer = waveBarrierLayer;
        }
        else
        {
            Debug.LogWarning($"[BossGateSetup] Layer '{WaveBarrierLayerName}' not found — barrier left on its current layer.");
        }

        BoxCollider box = barrierObject.GetComponent<BoxCollider>();

        NavMeshObstacle obstacle = barrierObject.GetComponent<NavMeshObstacle>();

        if (obstacle == null)
        {
            obstacle = Undo.AddComponent<NavMeshObstacle>(barrierObject);
        }

        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;

        WaveBarrier barrierScript = barrierObject.GetComponent<WaveBarrier>();

        if (barrierScript == null)
        {
            barrierScript = Undo.AddComponent<WaveBarrier>(barrierObject);
        }

        SerializedObject serializedBarrier = new SerializedObject(barrierScript);
        SetReference(serializedBarrier, "blockingCollider", box);
        SetReference(serializedBarrier, "navMeshObstacle", obstacle);
        serializedBarrier.ApplyModifiedProperties();

        EditorUtility.SetDirty(barrierObject);

        return barrierScript;
    }

    private static void AddBarrierToLastWave(ZombieWaveManager waveManager, WaveBarrier barrierScript)
    {
        SerializedObject serializedWaveManager = new SerializedObject(waveManager);
        SerializedProperty wavesProperty = serializedWaveManager.FindProperty("waves");

        if (wavesProperty == null || wavesProperty.arraySize == 0)
        {
            Debug.LogWarning("[BossGateSetup] ZombieWaveManager has no waves configured — cannot attach the Boss gate to the final wave.");
            return;
        }

        SerializedProperty lastWaveProperty = wavesProperty.GetArrayElementAtIndex(wavesProperty.arraySize - 1);
        SerializedProperty barriersProperty = lastWaveProperty.FindPropertyRelative("barriers");

        if (barriersProperty == null)
        {
            Debug.LogWarning("[BossGateSetup] Could not find the final wave's 'barriers' array.");
            return;
        }

        for (int i = 0; i < barriersProperty.arraySize; i++)
        {
            if (barriersProperty.GetArrayElementAtIndex(i).objectReferenceValue == barrierScript)
            {
                serializedWaveManager.ApplyModifiedProperties();
                return;
            }
        }

        int insertIndex = barriersProperty.arraySize;
        barriersProperty.arraySize++;
        barriersProperty.GetArrayElementAtIndex(insertIndex).objectReferenceValue = barrierScript;

        serializedWaveManager.ApplyModifiedProperties();
    }

    private static void RemovePersistentListenerIfPresent(UnityEventBase unityEvent, Object target, string methodName)
    {
        for (int i = unityEvent.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            if (unityEvent.GetPersistentTarget(i) == target && unityEvent.GetPersistentMethodName(i) == methodName)
            {
                UnityEventTools.RemovePersistentListener(unityEvent, i);
            }
        }
    }

    private static void SetReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null)
        {
            property.objectReferenceValue = value;
        }
        else
        {
            Debug.LogWarning($"[BossGateSetup] Property '{propertyName}' not found on {serializedObject.targetObject?.GetType().Name}.");
        }
    }

    private static Transform FindRootObject(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root.transform;
            }
        }

        return null;
    }

    private static Transform FindImmediateChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }
}
