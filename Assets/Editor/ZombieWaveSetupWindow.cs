using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the WaveSystem hierarchy (WaveManager + Wave1Zone/Wave2Zone/Wave3Zone, each with
/// a Trigger, SpawnPoints and a Barrier) plus the WaveHUD UI under GameCanvas, and wires
/// every reference on ZombieWaveManager — including the Wave 1/2/3 zombie composition
/// from the spec. Safe to run more than once (finds-or-creates). Trigger/SpawnPoint/
/// Barrier positions are placeholders only, staggered along X so they don't overlap —
/// they MUST be repositioned by hand to match Level1's actual corridors afterward, since
/// this tool has no way to know the real map layout. Editor-only, lives under Assets/Editor.
/// </summary>
public class ZombieWaveSetupWindow : EditorWindow
{
    [MenuItem("Tools/Zombie War/Wave Setup")]
    public static void ShowWindow()
    {
        GetWindow<ZombieWaveSetupWindow>("Wave Setup");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Builds WaveSystem (WaveManager + 3 wave zones, each with a Trigger, SpawnPoints " +
            "and a Barrier) plus the WaveHUD UI in the active scene, and wires everything into " +
            "ZombieWaveManager (including the Wave 1/2/3 zombie composition from the spec). " +
            "Existing objects with matching names are reused, not duplicated.\n\n" +
            "IMPORTANT: Trigger / SpawnPoint / Barrier POSITIONS are placeholders only " +
            "(staggered along X so they don't all overlap) — you MUST drag them to match " +
            "Level1's real corridors afterward.",
            MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Build / Update Wave System", GUILayout.Height(32)))
        {
            Build();
        }
    }

    private const string UndoLabel = "Setup Zombie Wave System";
    private const string WaveBarrierLayerName = "WaveBarrier";

    private const string ZombiePrefabPath = "Assets/Prefabs/Zombie/Zombie.prefab";
    private const string ZombieRunnerPrefabPath = "Assets/Prefabs/Zombie/ZombieRunner.prefab";
    private const string ZombieTankPrefabPath = "Assets/Prefabs/Zombie/ZombieTank.prefab";
    private const string BigZombiePrefabPath = "Assets/Prefabs/Zombie/BigZombie_AI.prefab";

    private static readonly string[] WaveNames = { "Wave 1", "Wave 2", "Final Wave" };
    private static readonly float[] DelayBeforeStart = { 1.5f, 1.5f, 2f };
    private static readonly float[] DelayAfterComplete = { 1.5f, 1.5f, 1.5f };
    private const float Wave1SpawnInterval = 0.8f;
    private const float Wave2SpawnInterval = 0.65f;
    private const float Wave3SpawnInterval = 0.5f;

    private const int SpawnPointsPerZone = 4;
    private const float ZoneStaggerDistance = 60f;
    private const float SpawnPointRingRadius = 8f;
    private const float SpawnPointsForwardOffset = 4f;

    private const float TriggerWidth = 6f;
    private const float TriggerHeight = 3f;
    private const float TriggerDepth = 2f;

    private const float BarrierWidth = 6f;
    private const float BarrierHeight = 3f;
    private const float BarrierThickness = 0.5f;
    private const float BarrierForwardOffset = 8f;

    private const string BarrierMaterialPath = "Assets/Materials/Environment/WaveBarrier.mat";
    private static readonly Color BarrierColor = new Color32(0x1E, 0x17, 0x13, 0xFF);

    private class WaveZoneRefs
    {
        public GameObject trigger;
        public GameObject[] barriers;
        public Transform[] spawnPoints;
    }

    private static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[ZombieWaveSetupWindow] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        Transform waveSystemTransform = GetOrCreateChild(null, activeScene, "WaveSystem");
        Transform managerTransform = GetOrCreateChild(waveSystemTransform, activeScene, "WaveManager");

        ZombieWaveManager waveManager = managerTransform.GetComponent<ZombieWaveManager>();

        if (waveManager == null)
        {
            waveManager = Undo.AddComponent<ZombieWaveManager>(managerTransform.gameObject);
        }

        WaveZoneRefs[] zones = new WaveZoneRefs[WaveNames.Length];

        for (int i = 0; i < WaveNames.Length; i++)
        {
            zones[i] = BuildWaveZone(waveSystemTransform, activeScene, i);
        }

        WireManagerReferences(waveManager, activeScene, zones);

        EditorSceneManager.MarkSceneDirty(activeScene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = managerTransform.gameObject;

        Debug.Log("[ZombieWaveSetupWindow] Wave system built/updated. Reposition each zone's Trigger, SpawnPoints and Barrier to match Level1's actual layout, review the ZombieWaveManager's Waves array, then save the scene (Ctrl+S).");
    }

    private static WaveZoneRefs BuildWaveZone(Transform waveSystemTransform, Scene scene, int waveIndex)
    {
        string zoneName = $"Wave{waveIndex + 1}Zone";
        Transform zoneTransform = GetOrCreateChild(waveSystemTransform, scene, zoneName);
        float zoneOffsetX = waveIndex * ZoneStaggerDistance;

        string triggerName = $"Wave{waveIndex + 1}Trigger";
        Transform triggerTransform = GetOrCreateChild(zoneTransform, scene, triggerName, out bool triggerIsNew);
        ConfigureTrigger(triggerTransform.gameObject, zoneOffsetX, triggerIsNew);

        string spawnPointsRootName = $"Wave{waveIndex + 1}SpawnPoints";
        Transform spawnPointsRoot = GetOrCreateChild(zoneTransform, scene, spawnPointsRootName);
        Transform[] spawnPoints = new Transform[SpawnPointsPerZone];

        for (int i = 0; i < SpawnPointsPerZone; i++)
        {
            Transform pointTransform = GetOrCreateChild(spawnPointsRoot, scene, $"SpawnPoint{i + 1}", out bool pointIsNew);

            // Only placed on first creation — re-running this tool must never undo a
            // designer's manual repositioning of a spawn point that's already been placed.
            if (pointIsNew)
            {
                float angle = (360f / SpawnPointsPerZone) * i * Mathf.Deg2Rad;
                Vector3 ringOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SpawnPointRingRadius;
                pointTransform.localPosition = ringOffset + new Vector3(zoneOffsetX, 0f, SpawnPointsForwardOffset);
            }

            spawnPoints[i] = pointTransform;
        }

        // Always ensures the zone has at least one barrier ("BarrierN") — a wave can have
        // more than one gate (e.g. two side corridors), so any EXTRA WaveBarrier objects
        // already sitting under this zone (hand-duplicated by the designer) are picked up
        // automatically too, without needing to be dragged into the Inspector by hand.
        string barrierName = $"Barrier{waveIndex + 1}";
        GameObject defaultBarrierObject = GetOrCreateBarrierObject(zoneTransform, barrierName, zoneOffsetX);

        // Configured before the scan below so a freshly-created default barrier (which
        // doesn't have a WaveBarrier component yet at this point) is still found by
        // FindAllBarriers instead of being skipped on this very first run.
        ConfigureBarrierComponents(defaultBarrierObject);
        GameObject[] barrierObjects = FindAllBarriers(zoneTransform);

        // Every barrier under this zone gets NavMeshObstacle + WaveBarrier wiring — not
        // just the tool's own default-named one — so a designer's hand-duplicated extra
        // barrier (e.g. a second corridor) also blocks zombies once this tool is re-run.
        // Re-configuring the default one again here is harmless (idempotent).
        foreach (GameObject barrierObject in barrierObjects)
        {
            ConfigureBarrierComponents(barrierObject);
        }

        return new WaveZoneRefs
        {
            trigger = triggerTransform.gameObject,
            barriers = barrierObjects,
            spawnPoints = spawnPoints
        };
    }

    private static GameObject[] FindAllBarriers(Transform zoneTransform)
    {
        List<GameObject> barriers = new List<GameObject>();

        for (int i = 0; i < zoneTransform.childCount; i++)
        {
            Transform child = zoneTransform.GetChild(i);

            if (child.GetComponent<WaveBarrier>() != null)
            {
                barriers.Add(child.gameObject);
            }
        }

        return barriers.ToArray();
    }

    private static void ConfigureTrigger(GameObject triggerObject, float zoneOffsetX, bool isNew)
    {
        BoxCollider box = triggerObject.GetComponent<BoxCollider>();

        if (box == null)
        {
            box = Undo.AddComponent<BoxCollider>(triggerObject);
        }

        box.isTrigger = true;

        // Size/position are placeholders only applied once — re-running this tool must
        // never undo a designer's manual resize/reposition of an already-placed trigger.
        if (isNew)
        {
            box.size = new Vector3(TriggerWidth, TriggerHeight, TriggerDepth);
            triggerObject.transform.localPosition = new Vector3(zoneOffsetX, TriggerHeight * 0.5f, 0f);
        }

        if (triggerObject.GetComponent<WaveTrigger>() == null)
        {
            Undo.AddComponent<WaveTrigger>(triggerObject);
        }

        EditorUtility.SetDirty(triggerObject);
    }

    private static GameObject GetOrCreateBarrierObject(Transform zoneTransform, string name, float zoneOffsetX)
    {
        Transform existing = FindImmediateChild(zoneTransform, name);
        GameObject barrierObject;
        bool isNew = existing == null;

        if (!isNew)
        {
            barrierObject = existing.gameObject;
        }
        else
        {
            barrierObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrierObject.name = name;
            Undo.RegisterCreatedObjectUndo(barrierObject, UndoLabel);
            barrierObject.transform.SetParent(zoneTransform, false);
        }

        // Position/scale are placeholders only applied once — re-running this tool must
        // never undo a designer's manual repositioning/rescaling of an already-placed
        // barrier. Everything else (material/layer/NavMeshObstacle/WaveBarrier wiring) is
        // handled uniformly for every barrier under a zone by ConfigureBarrierComponents.
        if (isNew)
        {
            barrierObject.transform.localPosition = new Vector3(zoneOffsetX, BarrierHeight * 0.5f, BarrierForwardOffset);
            barrierObject.transform.localScale = new Vector3(BarrierWidth, BarrierHeight, BarrierThickness);
        }

        return barrierObject;
    }

    /// <summary>
    /// Applied to every barrier found under a zone (the tool's own default-named one, plus
    /// any extra ones a designer hand-duplicated) so a second/third gate blocks zombie
    /// NavMeshAgents exactly like the first.
    /// </summary>
    private static void ConfigureBarrierComponents(GameObject barrierObject)
    {
        MeshRenderer meshRenderer = barrierObject.GetComponent<MeshRenderer>();

        if (meshRenderer != null)
        {
            meshRenderer.sharedMaterial = GetOrCreateBarrierMaterial();
        }

        int waveBarrierLayer = LayerMask.NameToLayer(WaveBarrierLayerName);

        if (waveBarrierLayer >= 0)
        {
            barrierObject.layer = waveBarrierLayer;
        }
        else
        {
            Debug.LogWarning($"[ZombieWaveSetupWindow] Layer '{WaveBarrierLayerName}' not found — did TagManager.asset get updated? Barrier left on its current layer.");
        }

        BoxCollider box = barrierObject.GetComponent<BoxCollider>();

        // Blocks zombie NavMeshAgents too (not just the Player, which the collider already
        // blocks via the WaveBarrier layer) — the box's default local size (1,1,1) matches
        // the barrier cube's own default mesh size, so it's already correct once combined
        // with the object's own localScale (Barrier Width/Height/Thickness).
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
    }

    /// <summary>
    /// A dedicated material (not the shared Built-in "Default-Material", which must never
    /// be mutated since it's used all over the project) so every Barrier gets the same
    /// tuned color and re-running this tool can safely recolor them all at once.
    /// </summary>
    private static Material GetOrCreateBarrierMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(BarrierMaterialPath);

        if (material == null)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            material = new Material(shader);

            string folder = System.IO.Path.GetDirectoryName(BarrierMaterialPath)?.Replace('\\', '/');
            EnsureFolder(folder);

            AssetDatabase.CreateAsset(material, BarrierMaterialPath);
        }

        material.color = BarrierColor;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = System.IO.Path.GetFileName(path);

        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void WireManagerReferences(ZombieWaveManager waveManager, Scene scene, WaveZoneRefs[] zones)
    {
        SerializedObject serializedManager = new SerializedObject(waveManager);

        Transform playerTransform = FindInActiveScene(scene, "Player");
        SetReference(serializedManager, "playerTransform", playerTransform);

        Transform gameManagerTransform = FindInActiveScene(scene, "GameManager");
        ZombieSpawner existingSpawner = gameManagerTransform != null ? gameManagerTransform.GetComponent<ZombieSpawner>() : null;
        SetReference(serializedManager, "backgroundSpawnerToDisable", existingSpawner);

        GameObject zombiePrefab = LoadPrefabOrWarn(ZombiePrefabPath);
        GameObject runnerPrefab = LoadPrefabOrWarn(ZombieRunnerPrefabPath);
        GameObject tankPrefab = LoadPrefabOrWarn(ZombieTankPrefabPath);
        GameObject bigZombiePrefab = LoadPrefabOrWarn(BigZombiePrefabPath);

        SerializedProperty wavesProperty = serializedManager.FindProperty("waves");
        wavesProperty.arraySize = WaveNames.Length;

        for (int i = 0; i < WaveNames.Length; i++)
        {
            SerializedProperty waveProperty = wavesProperty.GetArrayElementAtIndex(i);

            waveProperty.FindPropertyRelative("waveName").stringValue = WaveNames[i];
            waveProperty.FindPropertyRelative("startTrigger").objectReferenceValue = zones[i].trigger.GetComponent<WaveTrigger>();
            waveProperty.FindPropertyRelative("delayBeforeStart").floatValue = DelayBeforeStart[i];
            waveProperty.FindPropertyRelative("delayAfterComplete").floatValue = DelayAfterComplete[i];

            SerializedProperty barriersProperty = waveProperty.FindPropertyRelative("barriers");
            barriersProperty.arraySize = zones[i].barriers.Length;

            for (int b = 0; b < zones[i].barriers.Length; b++)
            {
                barriersProperty.GetArrayElementAtIndex(b).objectReferenceValue = zones[i].barriers[b].GetComponent<WaveBarrier>();
            }

            SerializedProperty spawnPointsProperty = waveProperty.FindPropertyRelative("spawnPoints");
            spawnPointsProperty.arraySize = zones[i].spawnPoints.Length;

            for (int p = 0; p < zones[i].spawnPoints.Length; p++)
            {
                spawnPointsProperty.GetArrayElementAtIndex(p).objectReferenceValue = zones[i].spawnPoints[p];
            }

            List<(GameObject prefab, int amount, float interval)> composition =
                GetWaveComposition(i, zombiePrefab, runnerPrefab, tankPrefab, bigZombiePrefab);

            SerializedProperty entriesProperty = waveProperty.FindPropertyRelative("zombieEntries");
            entriesProperty.arraySize = composition.Count;

            for (int e = 0; e < composition.Count; e++)
            {
                SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(e);
                entryProperty.FindPropertyRelative("zombiePrefab").objectReferenceValue = composition[e].prefab;
                entryProperty.FindPropertyRelative("amount").intValue = composition[e].amount;
                entryProperty.FindPropertyRelative("spawnInterval").floatValue = composition[e].interval;
            }
        }

        Transform gameCanvasTransform = FindInActiveScene(scene, "GameCanvas");

        if (gameCanvasTransform != null)
        {
            BuildWaveHud(gameCanvasTransform, scene, serializedManager);
        }
        else
        {
            Debug.LogWarning("[ZombieWaveSetupWindow] Could not find 'GameCanvas' in the active scene — WaveHUD UI was not created. Wire the UI fields on ZombieWaveManager by hand.");
        }

        serializedManager.ApplyModifiedProperties();
    }

    private static GameObject LoadPrefabOrWarn(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        if (prefab == null)
        {
            Debug.LogWarning($"[ZombieWaveSetupWindow] Could not load prefab at '{path}' — that zombie entry will be left empty until assigned manually.");
        }

        return prefab;
    }

    private static List<(GameObject prefab, int amount, float interval)> GetWaveComposition(
        int waveIndex, GameObject zombiePrefab, GameObject runnerPrefab, GameObject tankPrefab, GameObject bigZombiePrefab)
    {
        List<(GameObject, int, float)> composition = new List<(GameObject, int, float)>();

        switch (waveIndex)
        {
            case 0:
                composition.Add((zombiePrefab, 6, Wave1SpawnInterval));
                composition.Add((runnerPrefab, 2, Wave1SpawnInterval));
                break;

            case 1:
                composition.Add((zombiePrefab, 8, Wave2SpawnInterval));
                composition.Add((runnerPrefab, 4, Wave2SpawnInterval));
                composition.Add((tankPrefab, 1, Wave2SpawnInterval));
                break;

            case 2:
                composition.Add((zombiePrefab, 10, Wave3SpawnInterval));
                composition.Add((runnerPrefab, 5, Wave3SpawnInterval));
                composition.Add((tankPrefab, 2, Wave3SpawnInterval));
                composition.Add((bigZombiePrefab, 1, Wave3SpawnInterval));
                break;
        }

        return composition;
    }

    private static void BuildWaveHud(Transform gameCanvasTransform, Scene scene, SerializedObject serializedManager)
    {
        Transform hudRoot = GetOrCreateUIChild(gameCanvasTransform, "WaveHUD");
        RectTransform hudRect = hudRoot.GetComponent<RectTransform>();
        StretchFull(hudRect);

        TMP_Text waveTitleText = GetOrCreateTmpText(hudRoot, "WaveTitleText", "WAVE 1 / 3", 42f);
        RectTransform titleRect = waveTitleText.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(500f, 70f);
        titleRect.anchoredPosition = new Vector2(0f, -20f);

        TMP_Text remainingText = GetOrCreateTmpText(hudRoot, "ZombieRemainingText", "ZOMBIES: 0", 32f);
        RectTransform remainingRect = remainingText.rectTransform;
        remainingRect.anchorMin = new Vector2(0.5f, 1f);
        remainingRect.anchorMax = new Vector2(0.5f, 1f);
        remainingRect.pivot = new Vector2(0.5f, 1f);
        remainingRect.sizeDelta = new Vector2(500f, 50f);
        remainingRect.anchoredPosition = new Vector2(0f, -85f);

        RectTransform announcementRect = GetOrCreateUIChildImage(hudRoot, "WaveAnnouncementPanel", out Image announcementBg);
        announcementRect.anchorMin = new Vector2(0.5f, 0.5f);
        announcementRect.anchorMax = new Vector2(0.5f, 0.5f);
        announcementRect.pivot = new Vector2(0.5f, 0.5f);
        announcementRect.sizeDelta = new Vector2(900f, 160f);
        announcementRect.anchoredPosition = Vector2.zero;
        announcementBg.color = new Color(0f, 0f, 0f, 0.6f);
        announcementBg.raycastTarget = false;

        CanvasGroup announcementCanvasGroup = announcementRect.GetComponent<CanvasGroup>();

        if (announcementCanvasGroup == null)
        {
            announcementCanvasGroup = Undo.AddComponent<CanvasGroup>(announcementRect.gameObject);
        }

        announcementCanvasGroup.alpha = 0f;
        announcementCanvasGroup.interactable = false;
        announcementCanvasGroup.blocksRaycasts = false;
        announcementRect.gameObject.SetActive(false);

        TMP_Text announcementText = GetOrCreateTmpText(announcementRect, "AnnouncementText", "WAVE 1", 64f);
        RectTransform announcementTextRect = announcementText.rectTransform;
        announcementTextRect.anchorMin = Vector2.zero;
        announcementTextRect.anchorMax = Vector2.one;
        announcementTextRect.offsetMin = new Vector2(20f, 10f);
        announcementTextRect.offsetMax = new Vector2(-20f, -10f);

        SetReference(serializedManager, "waveTitleText", waveTitleText);
        SetReference(serializedManager, "zombieRemainingText", remainingText);
        SetReference(serializedManager, "waveAnnouncementPanel", announcementRect.gameObject);
        SetReference(serializedManager, "waveAnnouncementCanvasGroup", announcementCanvasGroup);
        SetReference(serializedManager, "waveAnnouncementText", announcementText);
    }

    private static TMP_Text GetOrCreateTmpText(Transform parent, string name, string defaultText, float fontSize)
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject textObject;

        if (existing != null)
        {
            textObject = existing.gameObject;
        }
        else
        {
            textObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textObject, UndoLabel);
            textObject.transform.SetParent(parent, false);
        }

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();

        if (text == null)
        {
            text = Undo.AddComponent<TextMeshProUGUI>(textObject);
            text.text = defaultText;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        EditorUtility.SetDirty(text);
        return text;
    }

    private static RectTransform GetOrCreateUIChildImage(Transform parent, string name, out Image image)
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject childObject;

        if (existing != null)
        {
            childObject = existing.gameObject;
        }
        else
        {
            childObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(childObject, UndoLabel);
            childObject.transform.SetParent(parent, false);
        }

        image = childObject.GetComponent<Image>();

        if (image == null)
        {
            image = Undo.AddComponent<Image>(childObject);
        }

        EditorUtility.SetDirty(childObject);
        return childObject.GetComponent<RectTransform>();
    }

    /// <summary>Like GetOrCreateChild, but creates with a RectTransform — for children nested under a Canvas/RectTransform hierarchy (a plain Transform there would break layout).</summary>
    private static Transform GetOrCreateUIChild(Transform parent, string name)
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject childObject;

        if (existing != null)
        {
            childObject = existing.gameObject;
        }
        else
        {
            childObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(childObject, UndoLabel);
            childObject.transform.SetParent(parent, false);
        }

        return childObject.transform;
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
            Debug.LogWarning($"[ZombieWaveSetupWindow] Property '{propertyName}' not found on {serializedObject.targetObject?.GetType().Name}.");
        }
    }

    private static Transform GetOrCreateChild(Transform parent, Scene scene, string name)
    {
        return GetOrCreateChild(parent, scene, name, out _);
    }

    private static Transform GetOrCreateChild(Transform parent, Scene scene, string name, out bool wasCreated)
    {
        Transform existing = parent != null ? FindImmediateChild(parent, name) : FindRootObject(scene, name);
        GameObject childObject;
        wasCreated = existing == null;

        if (!wasCreated)
        {
            childObject = existing.gameObject;
        }
        else
        {
            childObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(childObject, UndoLabel);

            if (parent != null)
            {
                childObject.transform.SetParent(parent, false);
            }
            else
            {
                SceneManager.MoveGameObjectToScene(childObject, scene);
            }
        }

        return childObject.transform;
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

    /// <summary>Searches every root GameObject's full hierarchy, including inactive objects.</summary>
    private static Transform FindInActiveScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
