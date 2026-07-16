using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// One-shot swap of BigZombie_AI.prefab's visual model + entire animation set (Idle/Walk/Run/
/// Attack/Death/Roar) over to the "Creature Pack" asset already imported under
/// Assets/Animations/BigZombie/Creature Pack. The old model ("VisualRoot", sourced from
/// Assets/Zombie/Base mesh) and the Creature Pack model use completely different, incompatible
/// skeletons (Generic rig, different bone names) — animations cannot be mixed between them, so
/// this replaces the whole visual child rather than swapping individual clips. Rebuilds
/// BigZombieAnimator.controller's existing 5 states in place (same parameters: MoveSpeed,
/// Attack, DieBack, DieForward, Roar — ZombieAI/BigZombieAI/ZombieHealth code needs zero
/// changes), bakes an OnAttackHit Animation Event onto the chosen attack clip at the same
/// normalized timing the original BigZombie_Attack.anim used (65% through), and re-measures
/// the new model's Renderer bounds to rescale the CapsuleCollider/NavMeshAgent/BossCameraTarget
/// proportionally — no hard-coded sizes. This prefab is also used by the smaller Wave-3
/// mini-boss instance, so that one gets the new model too (same skeleton either way).
/// Editor-only, safe to re-run (each run fully replaces the previous visual/motions).
/// </summary>
public static class BossCreaturePackSwap
{
    private const string BossPrefabPath = "Assets/Prefabs/Zombie/BigZombie_AI.prefab";
    private const string ControllerPath = "Assets/Animations/BigZombie/BigZombieAnimator.controller";
    private const string CreaturePackFolder = "Assets/Animations/BigZombie/Creature Pack";
    private const string NewModelPath = CreaturePackFolder + "/zombie+monster+3d+model.fbx";

    // Pick a different file here (all names verified to exist in the imported Creature Pack)
    // if you'd rather use e.g. "mutant idle.fbx" (plain, static) instead of the breathing
    // variant, or "mutant swiping.fbx"/"mutant jump attack.fbx" instead of a standing punch.
    private const string IdleClipPath = CreaturePackFolder + "/mutant breathing idle.fbx";
    private const string WalkClipPath = CreaturePackFolder + "/mutant walking.fbx";
    private const string RunClipPath = CreaturePackFolder + "/mutant run.fbx";
    private const string AttackClipPath = CreaturePackFolder + "/mutant punch.fbx";
    private const string DeathClipPath = CreaturePackFolder + "/mutant dying.fbx";
    private const string RoarClipPath = CreaturePackFolder + "/mutant roaring.fbx";

    // Extra states beyond the original 5 — not required for the Boss to function, only add
    // visual variety/new abilities. Wired in as Any State transitions, exactly like Roar.
    private const string Attack2ClipPath = CreaturePackFolder + "/mutant swiping.fbx";
    private const string FlexClipPath = CreaturePackFolder + "/mutant flexing muscles.fbx";
    private const string JumpAttackClipPath = CreaturePackFolder + "/mutant jump attack.fbx";

    // Matches the exact normalized timing (65% through the clip) the original
    // BigZombie_Attack.anim's own OnAttackHit event used.
    private const float AttackHitNormalizedTime = 0.65f;

    [MenuItem("Tools/Zombie War/Swap Boss Model To Creature Pack")]
    public static void Swap()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[BossCreaturePackSwap] Skipped — run this outside Play Mode.");
            return;
        }

        GameObject newModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NewModelPath);

        if (newModelAsset == null)
        {
            Debug.LogError($"[BossCreaturePackSwap] Could not find model at '{NewModelPath}'. Aborted.");
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        if (controller == null)
        {
            Debug.LogError($"[BossCreaturePackSwap] Could not find AnimatorController at '{ControllerPath}'. Aborted.");
            return;
        }

        // Humanoid (not Generic) — Mixamo/Creature Pack skeletons are specifically designed
        // for Humanoid retargeting, and Generic avatar auto-detection can silently fail to
        // recognize a valid root/skeleton for some hierarchies, leaving the Animator with no
        // avatar to apply any clip against (the model then just sits frozen in its bind pose,
        // with no error of any kind — exactly what was happening before this fix).
        Avatar modelAvatar = EnsureModelHasAvatar();

        if (modelAvatar == null)
        {
            Debug.LogError($"[BossCreaturePackSwap] Could not generate/find a Humanoid Avatar for '{NewModelPath}' — check its Rig import tab manually (it needs a detectable skeleton). Aborted.");
            return;
        }

        EnsureClipUsesAvatar(IdleClipPath, modelAvatar);
        EnsureClipUsesAvatar(WalkClipPath, modelAvatar);
        EnsureClipUsesAvatar(RunClipPath, modelAvatar);
        EnsureClipUsesAvatar(AttackClipPath, modelAvatar);
        EnsureClipUsesAvatar(DeathClipPath, modelAvatar);
        EnsureClipUsesAvatar(RoarClipPath, modelAvatar);
        EnsureClipUsesAvatar(Attack2ClipPath, modelAvatar);
        EnsureClipUsesAvatar(FlexClipPath, modelAvatar);
        EnsureClipUsesAvatar(JumpAttackClipPath, modelAvatar);

        // These 3 are imported with an empty clipAnimations list, meaning Unity falls back to
        // a single auto-generated clip with Loop Time OFF — it plays once then holds on the
        // last frame forever (looks "stuck"/juddery) instead of cycling continuously, which a
        // Locomotion blend tree's Idle/Walk/Run children need to do. Attack/Roar/Death/Flex/
        // JumpAttack are deliberately left alone — those are one-shot animations.
        EnsureClipLoops(IdleClipPath);
        EnsureClipLoops(WalkClipPath);
        EnsureClipLoops(RunClipPath);

        AnimationClip idleClip = LoadFirstClip(IdleClipPath);
        AnimationClip walkClip = LoadFirstClip(WalkClipPath);
        AnimationClip runClip = LoadFirstClip(RunClipPath);
        AnimationClip attackClip = LoadFirstClip(AttackClipPath);
        AnimationClip deathClip = LoadFirstClip(DeathClipPath);
        AnimationClip roarClip = LoadFirstClip(RoarClipPath);

        if (idleClip == null || walkClip == null || runClip == null || attackClip == null || deathClip == null || roarClip == null)
        {
            Debug.LogError("[BossCreaturePackSwap] One or more Creature Pack clips could not be loaded — check the file names/paths at the top of this script still match your imported files. Aborted.");
            return;
        }

        // Optional extra states — logged as warnings (not aborted) if missing, since the Boss
        // is fully functional without them.
        AnimationClip attack2Clip = LoadFirstClip(Attack2ClipPath);
        AnimationClip flexClip = LoadFirstClip(FlexClipPath);
        AnimationClip jumpAttackClip = LoadFirstClip(JumpAttackClipPath);

        EnsureAttackHitEvent(attackClip);
        SwapAnimatorMotions(controller, idleClip, walkClip, runClip, attackClip, deathClip, roarClip);

        if (attack2Clip != null)
        {
            EnsureAttackHitEvent(attack2Clip);
            EnsureExtraState(controller, "Attack2", "Attack2", attack2Clip);
        }
        else
        {
            Debug.LogWarning($"[BossCreaturePackSwap] Could not find '{Attack2ClipPath}' — skipping the Attack2 (swipe) variant, BigZombieAI's random Attack/Attack2 pick will just harmlessly always land on Attack.");
        }

        if (flexClip != null)
        {
            EnsureExtraState(controller, "Flex", "Flex", flexClip);
        }
        else
        {
            Debug.LogWarning($"[BossCreaturePackSwap] Could not find '{FlexClipPath}' — skipping the Flex transition beat (BigBossPhaseController.flexTriggerName will just harmlessly find no matching parameter).");
        }

        if (jumpAttackClip != null)
        {
            EnsureExtraState(controller, "JumpAttack", "JumpAttack", jumpAttackClip);
        }
        else
        {
            Debug.LogWarning($"[BossCreaturePackSwap] Could not find '{JumpAttackClipPath}' — skipping the Jump Attack state (BigBossPhaseController's Jump Attack will trigger nothing visually until this clip/state exists).");
        }

        SwapBossModel(newModelAsset, controller);

        AssetDatabase.SaveAssets();

        RevertSceneInstanceOverrides();
        RefreshCachedRendererArrays();

        Debug.Log("[BossCreaturePackSwap] Boss model + full animation set (Idle/Walk/Run/Attack/DieBack/DieForward/Roar) swapped to the Creature Pack mutant. " +
            "IMPORTANT — this could not be visually verified automatically: open BigZombie_AI.prefab and check (1) the model isn't floating/sinking relative to the Capsule Collider, " +
            "(2) the OnAttackHit event on 'mutant punch' (defaulted to 65% through the clip, matching the old attack's timing) actually lands on the punch's contact frame — adjust in the Animation window if not, " +
            "(3) DieBack and DieForward now both play the same 'mutant dying' clip (Creature Pack only has one death animation), " +
            "(4) re-run Tools > Zombie War > Setup Boss Fight and Setup Boss Phase 2 afterward so bossRenderers/BossCameraTarget framing picks up the new model. " +
            "This prefab is shared with the Wave-3 mini-boss instance, so it gets the new model too.");
    }

    private static AnimationClip LoadFirstClip(string fbxPath)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);

        foreach (Object asset in assets)
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }

        return null;
    }

    private static void EnsureAttackHitEvent(AnimationClip clip)
    {
        AnimationEvent[] existingEvents = AnimationUtility.GetAnimationEvents(clip);

        foreach (AnimationEvent existingEvent in existingEvents)
        {
            if (existingEvent.functionName == "OnAttackHit")
            {
                return;
            }
        }

        AnimationEvent hitEvent = new AnimationEvent
        {
            time = clip.length * AttackHitNormalizedTime,
            functionName = "OnAttackHit"
        };

        AnimationEvent[] newEvents = new AnimationEvent[existingEvents.Length + 1];
        existingEvents.CopyTo(newEvents, 0);
        newEvents[existingEvents.Length] = hitEvent;

        AnimationUtility.SetAnimationEvents(clip, newEvents);
        EditorUtility.SetDirty(clip);

        Debug.Log($"[BossCreaturePackSwap] Added an 'OnAttackHit' Animation Event at {AttackHitNormalizedTime:P0} through '{clip.name}' ({hitEvent.time:0.00}s of {clip.length:0.00}s) — open the clip in the Animation window and drag it to the exact punch-contact frame if it looks off.");
    }

    private static void SwapAnimatorMotions(AnimatorController controller, AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip attack, AnimationClip death, AnimationClip roar)
    {
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            AnimatorState state = child.state;

            switch (state.name)
            {
                case "Locomotion":
                    if (state.motion is BlendTree blendTree)
                    {
                        ChildMotion[] children = blendTree.children;

                        for (int i = 0; i < children.Length; i++)
                        {
                            if (Mathf.Approximately(children[i].threshold, 0f))
                            {
                                children[i].motion = idle;
                            }
                            else if (Mathf.Approximately(children[i].threshold, 0.5f))
                            {
                                children[i].motion = walk;
                            }
                            else if (Mathf.Approximately(children[i].threshold, 1f))
                            {
                                children[i].motion = run;
                            }
                        }

                        blendTree.children = children;
                    }

                    break;

                case "Attack":
                    state.motion = attack;
                    break;

                case "DieBack":
                case "DieForward":
                    state.motion = death;
                    break;

                case "Roar":
                    state.motion = roar;
                    break;
            }
        }

        EditorUtility.SetDirty(controller);
    }

    /// <summary>
    /// Adds a Trigger parameter + state (Any State → state, via that trigger, back to the
    /// default state on exit) — the same idiom BossFightSetup.EnsureRoarAnimatorState already
    /// established for Roar. Idempotent: if the state already exists, only its Motion is
    /// refreshed (a re-run with a different clip path updates it), the transitions are left
    /// alone in case a designer already retimed them by hand.
    /// </summary>
    private static void EnsureExtraState(AnimatorController controller, string stateName, string triggerName, AnimationClip clip)
    {
        bool hasParameter = false;

        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == triggerName)
            {
                hasParameter = true;
                break;
            }
        }

        if (!hasParameter)
        {
            controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        foreach (ChildAnimatorState existingChild in stateMachine.states)
        {
            if (existingChild.state.name == stateName)
            {
                existingChild.state.motion = clip;
                EditorUtility.SetDirty(controller);
                return;
            }
        }

        AnimatorState defaultState = stateMachine.defaultState;
        AnimatorState newState = stateMachine.AddState(stateName);
        newState.motion = clip;

        AnimatorStateTransition anyStateToNew = stateMachine.AddAnyStateTransition(newState);
        anyStateToNew.hasExitTime = false;
        anyStateToNew.hasFixedDuration = true;
        anyStateToNew.duration = 0.1f;
        anyStateToNew.canTransitionToSelf = false;
        anyStateToNew.AddCondition(AnimatorConditionMode.If, 0f, triggerName);

        if (defaultState != null)
        {
            AnimatorStateTransition newToReturn = newState.AddTransition(defaultState);
            newToReturn.hasExitTime = true;
            newToReturn.exitTime = 0.9f;
            newToReturn.hasFixedDuration = true;
            newToReturn.duration = 0.2f;
        }

        EditorUtility.SetDirty(controller);
        Debug.Log($"[BossCreaturePackSwap] Added a '{stateName}' state + '{triggerName}' trigger to BigZombieAnimator.controller (Any State -> {stateName} -> back to Locomotion).");
    }

    /// <summary>
    /// zombie+monster+3d+model.fbx was originally imported with Avatar Definition set to "No
    /// Avatar" — fine for a static prop, but an Animator with no Avatar can't resolve any
    /// clip's skeleton/root at all, so every animation silently fails to apply and the model
    /// just sits frozen in its bind pose forever, with no error of any kind. Forces Humanoid +
    /// "Create From This Model" (Mixamo/Creature Pack skeletons are specifically built for
    /// Humanoid retargeting — more reliable here than Generic, whose auto-root-detection can
    /// silently fail on some hierarchies) and reimports if it isn't already set that way.
    /// Returns the generated Avatar, or null if none could be found/created.
    /// </summary>
    private static Avatar EnsureModelHasAvatar()
    {
        ModelImporter importer = AssetImporter.GetAtPath(NewModelPath) as ModelImporter;

        if (importer == null)
        {
            Debug.LogWarning($"[BossCreaturePackSwap] Could not find a ModelImporter for '{NewModelPath}' — skipping the Avatar fix-up.");
            return null;
        }

        if (importer.animationType != ModelImporterAnimationType.Human
            || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            Debug.Log($"[BossCreaturePackSwap] '{NewModelPath}' was imported with no Avatar — set Animation Type to Humanoid with 'Create From This Model' and reimported.");
        }

        return LoadModelAvatar(NewModelPath);
    }

    /// <summary>Points an animation-only clip's FBX at the model's own Humanoid Avatar ("Copy From Other Avatar") so it retargets correctly onto this specific skeleton, reimporting only if the settings actually need to change.</summary>
    private static void EnsureClipUsesAvatar(string clipPath, Avatar avatar)
    {
        ModelImporter importer = AssetImporter.GetAtPath(clipPath) as ModelImporter;

        if (importer == null)
        {
            return;
        }

        if (importer.animationType == ModelImporterAnimationType.Human
            && importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther
            && importer.sourceAvatar == avatar)
        {
            return;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// Explicitly enables Loop Time (+ Loop Pose, so the last frame blends back into the
    /// first instead of popping) on the clip's import settings — needed because this file was
    /// imported with an empty clipAnimations list, which makes Unity fall back to a single
    /// auto-generated clip with looping OFF by default.
    /// </summary>
    private static void EnsureClipLoops(string clipPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(clipPath) as ModelImporter;

        if (importer == null)
        {
            return;
        }

        ModelImporterClipAnimation[] clipAnimations = importer.clipAnimations;

        if (clipAnimations == null || clipAnimations.Length == 0)
        {
            clipAnimations = importer.defaultClipAnimations;
        }

        if (clipAnimations == null || clipAnimations.Length == 0)
        {
            return;
        }

        bool needsReimport = false;

        for (int i = 0; i < clipAnimations.Length; i++)
        {
            if (!clipAnimations[i].loopTime || !clipAnimations[i].loopPose)
            {
                clipAnimations[i].loopTime = true;
                clipAnimations[i].loopPose = true;
                needsReimport = true;
            }
        }

        if (!needsReimport)
        {
            return;
        }

        importer.clipAnimations = clipAnimations;
        importer.SaveAndReimport();
    }

    private static Avatar LoadModelAvatar(string modelPath)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);

        foreach (Object asset in assets)
        {
            if (asset is Avatar avatar)
            {
                return avatar;
            }
        }

        return null;
    }

    private static void SwapBossModel(GameObject newModelAsset, AnimatorController controller)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(BossPrefabPath);

        try
        {
            Transform oldVisual = prefabRoot.transform.Find("VisualRoot");

            if (oldVisual != null)
            {
                Object.DestroyImmediate(oldVisual.gameObject);
            }

            GameObject newVisual = (GameObject)PrefabUtility.InstantiatePrefab(newModelAsset, prefabRoot.transform);
            newVisual.name = "VisualRoot";
            newVisual.transform.localPosition = Vector3.zero;
            newVisual.transform.localRotation = Quaternion.identity;
            newVisual.transform.localScale = Vector3.one;

            foreach (Transform childTransform in newVisual.GetComponentsInChildren<Transform>(true))
            {
                childTransform.gameObject.layer = prefabRoot.layer;

                // This model was originally imported as a static prop (Avatar Definition: No
                // Avatar) — if any of its GameObjects carry "Batching Static", Unity static-
                // batches the mesh at build/bake time and the SkinnedMeshRenderer's vertices
                // freeze in whatever pose they had then forever after, no matter what the
                // Animator's state machine does underneath (the classic "stuck in one pose,
                // sliding across the floor" symptom). A moving, animated character must never
                // carry any static flag.
                GameObjectUtility.SetStaticEditorFlags(childTransform.gameObject, (StaticEditorFlags)0);
            }

            // A visual-only model has no business bringing its own physics — the root's
            // CapsuleCollider/NavMeshAgent are the only ones that should ever exist here.
            foreach (Collider childCollider in newVisual.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(childCollider);
            }

            foreach (Rigidbody childRigidbody in newVisual.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(childRigidbody);
            }

            Animator animator = newVisual.GetComponent<Animator>();

            if (animator == null)
            {
                animator = newVisual.AddComponent<Animator>();
            }

            if (animator.avatar == null)
            {
                Avatar modelAvatar = LoadModelAvatar(NewModelPath);

                if (modelAvatar != null)
                {
                    animator.avatar = modelAvatar;
                }
                else
                {
                    Debug.LogWarning($"[BossCreaturePackSwap] '{NewModelPath}' has no generated Avatar — animations will not play (the model will sit frozen in its bind pose). Check the model's Rig import tab: Animation Type should be Humanoid, Avatar Definition should be 'Create From This Model'.");
                }
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            // Forces full skeleton evaluation regardless of camera-visibility bounds — this
            // model's SkinnedMeshRenderer bounds have already been seen to compute wildly
            // wrong (hundreds of units off), which made Unity's default "Cull Update
            // Transforms" culling mode think the Boss was off-screen and skip posing its
            // bones entirely, even though the Animator's own state machine kept running
            // correctly underneath. A single Boss character costs nothing measurable here.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (newVisual.GetComponent<BigZombieAttackEventRelay>() == null)
            {
                newVisual.AddComponent<BigZombieAttackEventRelay>();
            }

            RescaleColliderAndAgentToNewModel(prefabRoot, newVisual);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, BossPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    /// <summary>
    /// Measures the new model's combined Renderer bounds (in the prefab-editing scene, where
    /// the root's own authored scale is already applied) and rescales the CapsuleCollider,
    /// NavMeshAgent, and BossCameraTarget to fit it — the same "measure once, never hard-code"
    /// pattern BossCameraController already uses for camera framing. Divides back out by the
    /// root's own scale so the stored values stay in the same pre-scale local units a scene
    /// instance's own scale (6, 8, or a smaller mini-boss override) multiplies on top of.
    /// </summary>
    private static void RescaleColliderAndAgentToNewModel(GameObject prefabRoot, GameObject newVisual)
    {
        Renderer[] renderers = newVisual.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = default;
        bool started = false;

        foreach (Renderer renderer in renderers)
        {
            if (!started)
            {
                bounds = renderer.bounds;
                started = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!started)
        {
            Debug.LogWarning("[BossCreaturePackSwap] New model has no Renderers — could not auto-measure bounds. Capsule Collider/NavMeshAgent/BossCameraTarget left unchanged; adjust by hand.");
            return;
        }

        float rootScale = Mathf.Max(prefabRoot.transform.localScale.x, 0.0001f);
        Vector3 localCenter = prefabRoot.transform.InverseTransformPoint(bounds.center);

        // Some Creature Pack-style FBX exports bake in a large positional offset from wherever
        // the character happened to sit in the artist's original authoring scene — recenter
        // the model horizontally under the root's own local origin (X/Z only; Y is left alone,
        // since "feet at Y=0" is the standard convention this and the old model both already
        // follow) so gameplay logic (Collider/NavMeshAgent/ThrowPoint, all defined relative to
        // the root) actually lines up with where the visual mesh renders, instead of the mesh
        // sitting dozens of units off to one side while the root/collider stay at the origin.
        newVisual.transform.localPosition -= new Vector3(localCenter.x, 0f, localCenter.z);
        localCenter.x = 0f;
        localCenter.z = 0f;

        float height = bounds.size.y / rootScale;
        float width = Mathf.Max(bounds.size.x, bounds.size.z) / rootScale;

        CapsuleCollider capsule = prefabRoot.GetComponent<CapsuleCollider>();

        if (capsule != null)
        {
            capsule.height = height;
            capsule.radius = Mathf.Max(width * 0.25f, 0.2f);
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
        }

        NavMeshAgent agent = prefabRoot.GetComponent<NavMeshAgent>();

        if (agent != null)
        {
            agent.height = height;
            agent.radius = Mathf.Max(width * 0.25f, 0.2f);
        }

        Transform cameraTarget = prefabRoot.transform.Find("BossCameraTarget");

        if (cameraTarget != null)
        {
            cameraTarget.localPosition = new Vector3(0f, height * 0.65f, 0f);
        }

        Debug.Log($"[BossCreaturePackSwap] Measured new model — height={height:0.00}, width={width:0.00} (pre-scale local units), recentered horizontally under the root's origin — Capsule Collider/NavMeshAgent/BossCameraTarget rescaled to match.");
    }

    /// <summary>
    /// A scene's own BigZombieBoss instance can carry per-instance Transform overrides on
    /// BossCameraTarget/CapsuleCollider/NavMeshAgent from earlier hand-tuning (e.g. camera
    /// framing tweaks made directly on the instance) — those pin the instance to whatever
    /// value was stored regardless of what the prefab asset's own default now says, so fixing
    /// the asset alone silently does nothing for a scene that already has such an override.
    /// Reverting forces the instance back to the (now-corrected) prefab default.
    /// </summary>
    private static void RevertSceneInstanceOverrides()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        Transform bossTransform = FindInActiveScene(activeScene, "BigZombieBoss");

        if (bossTransform == null)
        {
            return;
        }

        CapsuleCollider capsule = bossTransform.GetComponent<CapsuleCollider>();

        if (capsule != null && PrefabUtility.IsPartOfPrefabInstance(capsule))
        {
            PrefabUtility.RevertObjectOverride(capsule, InteractionMode.AutomatedAction);
        }

        NavMeshAgent agent = bossTransform.GetComponent<NavMeshAgent>();

        if (agent != null && PrefabUtility.IsPartOfPrefabInstance(agent))
        {
            PrefabUtility.RevertObjectOverride(agent, InteractionMode.AutomatedAction);
        }

        Transform cameraTarget = bossTransform.Find("BossCameraTarget");

        if (cameraTarget != null && PrefabUtility.IsPartOfPrefabInstance(cameraTarget))
        {
            PrefabUtility.RevertObjectOverride(cameraTarget, InteractionMode.AutomatedAction);
        }

        Transform visualRoot = bossTransform.Find("VisualRoot");
        Animator sceneAnimator = visualRoot != null ? visualRoot.GetComponent<Animator>() : null;

        if (sceneAnimator != null && PrefabUtility.IsPartOfPrefabInstance(sceneAnimator))
        {
            // The scene's own Animator can be pinned to a stale per-instance override (e.g.
            // Avatar=None or the wrong RuntimeAnimatorController) left over from an earlier
            // run, before this tool's fixes existed — reverting forces it back to the
            // prefab's current (corrected) values instead of silently keeping the old one.
            PrefabUtility.RevertObjectOverride(sceneAnimator, InteractionMode.AutomatedAction);
        }

        Debug.Log("[BossCreaturePackSwap] Reverted any per-instance overrides on the scene's BigZombieBoss (BossCameraTarget/Collider/Agent/Animator) back to the prefab's corrected defaults — harmless no-op if there weren't any.");
        EditorSceneManager.MarkSceneDirty(activeScene);
    }

    /// <summary>
    /// BossFightManager.bossRenderers/bossAnimator and BigBossPhaseController.bossRenderers/
    /// animator are all plain serialized references snapshotted once by whichever Editor tool
    /// last ran — not live references. Re-running this swap destroys and recreates VisualRoot
    /// (and its Animator) again, which leaves those fields dangling until something
    /// re-populates them: a stale bossRenderers array made BossCameraController measure bounds
    /// off dead Renderers (fixed above by preferring the Collider instead), and a stale
    /// bossAnimator made BossFightManager.PlayRoarAnimationAndSfx() silently skip SetTrigger
    /// every time (Unity treats a reference to a destroyed object as null, so the null-check
    /// there just quietly no-ops instead of throwing).
    /// </summary>
    private static void RefreshCachedRendererArrays()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        Transform bossTransform = FindInActiveScene(activeScene, "BigZombieBoss");

        if (bossTransform == null)
        {
            return;
        }

        Renderer[] currentRenderers = bossTransform.GetComponentsInChildren<Renderer>(true);
        Animator currentAnimator = bossTransform.GetComponentInChildren<Animator>(true);

        Transform bossFightManagerTransform = FindInActiveScene(activeScene, "BossFightManager");
        BossFightManager bossFightManager = bossFightManagerTransform != null ? bossFightManagerTransform.GetComponent<BossFightManager>() : null;

        if (bossFightManager != null)
        {
            SerializedObject serializedManager = new SerializedObject(bossFightManager);
            WriteRendererArray(serializedManager, currentRenderers);
            SetObjectReference(serializedManager, "bossAnimator", currentAnimator);
        }

        BigBossPhaseController phaseController = bossTransform.GetComponent<BigBossPhaseController>();

        if (phaseController != null)
        {
            SerializedObject serializedController = new SerializedObject(phaseController);
            WriteRendererArray(serializedController, currentRenderers);
            SetObjectReference(serializedController, "animator", currentAnimator);
        }

        // ZombieAI/ZombieHealth each keep their OWN separate Animator reference (used every
        // frame for MoveSpeed / DieBack-DieForward) — missed by the two refreshes above since
        // those are different fields on different components. A stale one here is exactly
        // why the Boss could look correctly configured everywhere else yet still never
        // actually receive a nonzero MoveSpeed while visibly moving.
        ZombieAI zombieAI = bossTransform.GetComponent<ZombieAI>();

        if (zombieAI != null)
        {
            SetObjectReference(new SerializedObject(zombieAI), "animator", currentAnimator);
        }

        ZombieHealth zombieHealth = bossTransform.GetComponent<ZombieHealth>();

        if (zombieHealth != null)
        {
            SetObjectReference(new SerializedObject(zombieHealth), "animator", currentAnimator);
        }

        Debug.Log($"[BossCreaturePackSwap] Refreshed every cached Animator/Renderer reference (BossFightManager, BigBossPhaseController, ZombieAI, ZombieHealth) to the current model's {currentRenderers.Length} Renderer(s) and Animator.");
        EditorSceneManager.MarkSceneDirty(activeScene);
    }

    private static void WriteRendererArray(SerializedObject serializedObject, Renderer[] renderers)
    {
        SerializedProperty property = serializedObject.FindProperty("bossRenderers");

        if (property == null)
        {
            return;
        }

        property.arraySize = renderers.Length;

        for (int i = 0; i < renderers.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property == null)
        {
            return;
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedProperties();
    }

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
