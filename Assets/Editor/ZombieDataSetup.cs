using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Creates one ZombieData asset per existing zombie prefab under Assets/ScriptableObjects/
/// Zombies, populated by copying each prefab's own currently-tuned ZombieAI/ZombieHealth
/// field values (so nothing gets guessed/reset), then wires zombieData on both components
/// back onto that same prefab. After running, editing the .asset directly re-tunes that
/// zombie type without touching the prefab. Safe to run more than once — existing assets
/// are refreshed from the prefab's current values, not duplicated.
/// </summary>
public static class ZombieDataSetup
{
    private const string DataFolder = "Assets/ScriptableObjects/Zombies";

    private static readonly (string prefabPath, string assetName)[] Targets =
    {
        ("Assets/Prefabs/Zombie/Zombie.prefab", "ZombieData"),
        ("Assets/Prefabs/Zombie/ZombieRunner.prefab", "ZombieRunnerData"),
        ("Assets/Prefabs/Zombie/ZombieTank.prefab", "ZombieTankData"),
        ("Assets/Prefabs/Zombie/BigZombie_AI.prefab", "BigZombieData"),
    };

    [MenuItem("Tools/Zombie War/Setup Zombie Data Assets")]
    public static void Setup()
    {
        EnsureFolder(DataFolder);

        foreach ((string prefabPath, string assetName) in Targets)
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefabAsset == null)
            {
                Debug.LogWarning($"[ZombieDataSetup] Prefab not found at '{prefabPath}' — skipped.");
                continue;
            }

            ZombieAI readOnlyAi = prefabAsset.GetComponent<ZombieAI>();
            ZombieHealth readOnlyHealth = prefabAsset.GetComponent<ZombieHealth>();

            if (readOnlyAi == null && readOnlyHealth == null)
            {
                Debug.LogWarning($"[ZombieDataSetup] '{prefabPath}' has no ZombieAI/ZombieHealth — skipped.");
                continue;
            }

            string assetPath = $"{DataFolder}/{assetName}.asset";
            ZombieData data = AssetDatabase.LoadAssetAtPath<ZombieData>(assetPath);

            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ZombieData>();
                AssetDatabase.CreateAsset(data, assetPath);
            }

            if (readOnlyAi != null)
            {
                CopyMatchingFields(readOnlyAi, data);
            }

            if (readOnlyHealth != null)
            {
                CopyMatchingFields(readOnlyHealth, data);
            }

            EditorUtility.SetDirty(data);

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

            WireZombieDataField(root.GetComponent<ZombieAI>(), data);
            WireZombieDataField(root.GetComponent<ZombieHealth>(), data);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);

            Debug.Log($"[ZombieDataSetup] '{assetPath}' populated from '{prefabPath}' and wired back onto it.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ZombieDataSetup] Done — edit the .asset files under Assets/ScriptableObjects/Zombies to re-tune each zombie type without touching prefabs.");
    }

    private static void WireZombieDataField(Object component, ZombieData data)
    {
        if (component == null)
        {
            return;
        }

        SerializedObject serializedComponent = new SerializedObject(component);
        SerializedProperty property = serializedComponent.FindProperty("zombieData");

        if (property == null)
        {
            return;
        }

        property.objectReferenceValue = data;
        serializedComponent.ApplyModifiedProperties();
    }

    /// <summary>
    /// Iterates ZombieData's own fields and, for each one, copies the same-named /
    /// same-type field off source (a live ZombieAI/ZombieTank/ZombieRunner or ZombieHealth
    /// instance) if it exists there — so this works for every zombie subclass (e.g.
    /// ZombieTank's extra attackWindup field) without hardcoding a field list twice.
    /// </summary>
    private static void CopyMatchingFields(Object source, ZombieData target)
    {
        SerializedObject sourceObject = new SerializedObject(source);
        SerializedObject targetObject = new SerializedObject(target);

        SerializedProperty iterator = targetObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (iterator.name == "m_Script")
            {
                continue;
            }

            SerializedProperty sourceProperty = sourceObject.FindProperty(iterator.name);

            if (sourceProperty == null || sourceProperty.propertyType != iterator.propertyType)
            {
                continue;
            }

            switch (iterator.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    iterator.objectReferenceValue = sourceProperty.objectReferenceValue;
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    iterator.intValue = sourceProperty.intValue;
                    break;
                case SerializedPropertyType.Float:
                    iterator.floatValue = sourceProperty.floatValue;
                    break;
                case SerializedPropertyType.Color:
                    iterator.colorValue = sourceProperty.colorValue;
                    break;
            }
        }

        targetObject.ApplyModifiedProperties();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);

        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }
}
