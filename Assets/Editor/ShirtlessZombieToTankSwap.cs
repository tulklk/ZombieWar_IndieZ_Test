using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>
/// Merges ShirtlessZombie into ZombieTank: rebuilds Assets/Prefabs/Zombie/ZombieTank.prefab on
/// top of the ShirtlessZombie_FREE model (Assets/NewPunch/ShirtlessZombieFree/Prefabs), copying
/// every tuned gameplay value (attackWindup, damage, health, blood VFX, etc.) off the current
/// ZombieTank.prefab before it gets overwritten. Both rigs are Humanoid, so the shared
/// Assets/Zombie/Animations/Zombie.controller retargets straight onto the new skeleton — same
/// Idle/Walk/Run/Attack/Death animation as the other zombie types. Also deletes the now-redundant
/// standalone Assets/Prefabs/Zombie/ShirtlessZombie.prefab and its one-off setup script, since
/// ShirtlessZombie's identity is being absorbed into ZombieTank. Works entirely on prefab assets
/// via PrefabUtility.LoadPrefabContents — never touches whatever scene happens to be open.
/// </summary>
public static class ShirtlessZombieToTankSwap
{
    private const string NewModelPrefabPath = "Assets/NewPunch/ShirtlessZombieFree/Prefabs/ShirtlessZombie_FREE.prefab";
    private const string TargetPrefabPath = "Assets/Prefabs/Zombie/ZombieTank.prefab";
    private const string SharedControllerPath = "Assets/Zombie/Animations/Zombie.controller";
    private const string StandaloneShirtlessZombiePrefabPath = "Assets/Prefabs/Zombie/ShirtlessZombie.prefab";
    private const string StandaloneSetupScriptPath = "Assets/Editor/ShirtlessZombieSetup.cs";

    private static readonly string[] ZombieAiExcludedFields = { "target", "animator", "audioSource" };
    private static readonly string[] ZombieHealthExcludedFields = { "animator", "zombieAI", "zombieCollider", "renderers", "currentHealth" };

    [MenuItem("Tools/Zombie War/Merge ShirtlessZombie Into ZombieTank")]
    public static void Merge()
    {
        GameObject newModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NewModelPrefabPath);
        if (newModelAsset == null)
        {
            Debug.LogError($"[ShirtlessZombieToTankSwap] New model prefab not found at '{NewModelPrefabPath}'. Aborted.");
            return;
        }

        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SharedControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[ShirtlessZombieToTankSwap] Shared zombie Animator Controller not found at '{SharedControllerPath}'. Aborted.");
            return;
        }

        GameObject oldTank = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath);
        if (oldTank == null)
        {
            Debug.LogError($"[ShirtlessZombieToTankSwap] Existing prefab not found at '{TargetPrefabPath}'. Aborted — nothing to preserve stats from.");
            return;
        }

        ZombieTank oldAI = oldTank.GetComponent<ZombieTank>();
        ZombieHealth oldHealth = oldTank.GetComponent<ZombieHealth>();
        NavMeshAgent oldAgent = oldTank.GetComponent<NavMeshAgent>();
        CapsuleCollider oldCapsule = oldTank.GetComponent<CapsuleCollider>();

        if (oldAI == null || oldHealth == null)
        {
            Debug.LogError("[ShirtlessZombieToTankSwap] Existing ZombieTank prefab is missing ZombieTank/ZombieHealth — aborted so nothing gets silently reset to defaults.");
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

        ZombieTank newAI = root.AddComponent<ZombieTank>();
        CopySerializedFieldsExcept(oldAI, newAI, ZombieAiExcludedFields);

        ZombieHealth newHealth = root.AddComponent<ZombieHealth>();
        CopySerializedFieldsExcept(oldHealth, newHealth, ZombieHealthExcludedFields);

        PrefabUtility.SaveAsPrefabAsset(root, TargetPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        if (AssetDatabase.LoadAssetAtPath<GameObject>(StandaloneShirtlessZombiePrefabPath) != null)
        {
            AssetDatabase.DeleteAsset(StandaloneShirtlessZombiePrefabPath);
        }

        if (AssetDatabase.LoadAssetAtPath<MonoScript>(StandaloneSetupScriptPath) != null)
        {
            AssetDatabase.DeleteAsset(StandaloneSetupScriptPath);
        }

        AssetDatabase.Refresh();
        Debug.Log($"[ShirtlessZombieToTankSwap] '{TargetPrefabPath}' now uses the ShirtlessZombie model, Animator retargeted onto the shared Zombie controller, with all previous ZombieTank/ZombieHealth stats preserved. The standalone ShirtlessZombie prefab and its setup script were removed. Check the new ZombieTank's scale against the other zombie types.");
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
                    Debug.LogWarning($"[ShirtlessZombieToTankSwap] Don't know how to copy field '{iterator.name}' of type {iterator.propertyType} — skipped, left at default.");
                    break;
            }
        }

        targetObject.ApplyModifiedProperties();
    }
}
