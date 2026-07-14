using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>
/// Rebuilds Assets/Prefabs/Zombie/ZombieRunner.prefab on top of the ArtStore3D "Zombie" model
/// (Assets/ArtStore3D/Zombie/Prefab/Zombie.prefab) instead of the old Zombie1-based body,
/// while preserving every tuned gameplay value (speed, damage, health, blood VFX, etc.) by
/// copying them off the current ZombieRunner.prefab before it gets overwritten. Both rigs are
/// Humanoid, so the same shared Assets/Zombie/Animations/Zombie.controller retargets straight
/// onto the new skeleton — same Idle/Walk/Run/Attack/Death animation as Zombie/ZombieTank.
/// Works entirely on prefab assets via PrefabUtility.LoadPrefabContents — never touches
/// whatever scene happens to be open. Safe to run more than once.
/// </summary>
public static class ZombieRunnerModelSwap
{
    private const string NewModelPrefabPath = "Assets/ArtStore3D/Zombie/Prefab/Zombie.prefab";
    private const string TargetPrefabPath = "Assets/Prefabs/Zombie/ZombieRunner.prefab";
    private const string SharedControllerPath = "Assets/Zombie/Animations/Zombie.controller";

    // Fields that reference a sibling component on the SAME prefab instance (Animator,
    // other scripts, the model's own renderers/collider) must never be copied across —
    // each component re-resolves these itself via GetComponent<>() in Awake() when left null.
    private static readonly string[] ZombieAiExcludedFields = { "target", "animator", "audioSource" };
    private static readonly string[] ZombieHealthExcludedFields = { "animator", "zombieAI", "zombieCollider", "renderers", "currentHealth" };

    [MenuItem("Tools/Zombie War/Swap ZombieRunner Model")]
    public static void Swap()
    {
        GameObject newModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NewModelPrefabPath);
        if (newModelAsset == null)
        {
            Debug.LogError($"[ZombieRunnerModelSwap] New model prefab not found at '{NewModelPrefabPath}'. Aborted.");
            return;
        }

        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SharedControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[ZombieRunnerModelSwap] Shared zombie Animator Controller not found at '{SharedControllerPath}'. Aborted.");
            return;
        }

        GameObject oldRunner = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath);
        if (oldRunner == null)
        {
            Debug.LogError($"[ZombieRunnerModelSwap] Existing prefab not found at '{TargetPrefabPath}'. Aborted — nothing to preserve stats from.");
            return;
        }

        ZombieRunner oldAI = oldRunner.GetComponent<ZombieRunner>();
        ZombieHealth oldHealth = oldRunner.GetComponent<ZombieHealth>();
        NavMeshAgent oldAgent = oldRunner.GetComponent<NavMeshAgent>();
        CapsuleCollider oldCapsule = oldRunner.GetComponent<CapsuleCollider>();

        if (oldAI == null || oldHealth == null)
        {
            Debug.LogError("[ZombieRunnerModelSwap] Existing ZombieRunner prefab is missing ZombieRunner/ZombieHealth — aborted so nothing gets silently reset to defaults.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(NewModelPrefabPath);

        Animator animator = root.GetComponent<Animator>();
        if (animator == null)
        {
            animator = root.AddComponent<Animator>();
        }
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        root.tag = "Zombie";
        int zombieLayer = LayerMask.NameToLayer("Zombie");
        if (zombieLayer >= 0)
        {
            root.layer = zombieLayer;
        }

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        if (oldAgent != null)
        {
            agent.radius = oldAgent.radius;
            agent.speed = oldAgent.speed;
            agent.acceleration = oldAgent.acceleration;
            agent.avoidancePriority = oldAgent.avoidancePriority;
            agent.angularSpeed = oldAgent.angularSpeed;
            agent.stoppingDistance = oldAgent.stoppingDistance;
            agent.height = oldAgent.height;
        }

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        if (oldCapsule != null)
        {
            capsule.radius = oldCapsule.radius;
            capsule.height = oldCapsule.height;
            capsule.center = oldCapsule.center;
        }

        ZombieRunner newAI = root.AddComponent<ZombieRunner>();
        CopySerializedFieldsExcept(oldAI, newAI, ZombieAiExcludedFields);

        ZombieHealth newHealth = root.AddComponent<ZombieHealth>();
        CopySerializedFieldsExcept(oldHealth, newHealth, ZombieHealthExcludedFields);

        PrefabUtility.SaveAsPrefabAsset(root, TargetPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        AssetDatabase.Refresh();
        Debug.Log($"[ZombieRunnerModelSwap] '{TargetPrefabPath}' now uses the ArtStore3D Zombie model, Animator retargeted onto the shared Zombie controller (same animation as Zombie/ZombieTank), with all previous ZombieRunner/ZombieHealth stats preserved. Check its scale in the Inspector against the other zombie types and adjust if the new model reads as a different size.");
    }

    private static void CopySerializedFieldsExcept(Object source, Object target, string[] excludedNames)
    {
        SerializedObject sourceObject = new SerializedObject(source);
        SerializedObject targetObject = new SerializedObject(target);

        SerializedProperty iterator = sourceObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (iterator.name == "m_Script" || System.Array.IndexOf(excludedNames, iterator.name) >= 0)
            {
                continue;
            }

            SerializedProperty targetProperty = targetObject.FindProperty(iterator.name);
            if (targetProperty == null)
            {
                continue;
            }

            switch (iterator.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    targetProperty.objectReferenceValue = iterator.objectReferenceValue;
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    targetProperty.intValue = iterator.intValue;
                    break;
                case SerializedPropertyType.Float:
                    targetProperty.floatValue = iterator.floatValue;
                    break;
                case SerializedPropertyType.Boolean:
                    targetProperty.boolValue = iterator.boolValue;
                    break;
                case SerializedPropertyType.Color:
                    targetProperty.colorValue = iterator.colorValue;
                    break;
                case SerializedPropertyType.String:
                    targetProperty.stringValue = iterator.stringValue;
                    break;
                default:
                    Debug.LogWarning($"[ZombieRunnerModelSwap] Don't know how to copy field '{iterator.name}' of type {iterator.propertyType} — skipped, left at default.");
                    break;
            }
        }

        targetObject.ApplyModifiedProperties();
    }
}
