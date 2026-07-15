using TMPro;
using Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the BossArena hierarchy (BigZombieBoss instance, BossIntroTrigger, a
/// CinemachineTargetGroup, BossCameraController rig, BossFightManager), the two extra
/// Cinemachine 2.x vcams (CM_BossIntro / CM_BossCombat, siblings of the existing
/// CM_PlayerFollow), and the BossIntroPanel/BossHealthPanel UI under GameCanvas — then wires
/// every reference. Also (additively, idempotent) adds a "Roar" trigger + state to
/// BigZombieAnimator.controller using the already-imported zombie@roar.fbx clip, and adds a
/// BossCameraTarget child (positioned from combined Renderer bounds) to BigZombie_AI.prefab.
/// Safe to run more than once — every object/wire-up is find-or-create; only a freshly
/// created element's placeholder position/color/size is ever touched. Editor-only, lives
/// under Assets/Editor.
/// </summary>
public static class BossFightSetup
{
    private const string UndoLabel = "Setup Boss Fight";

    private const string BigZombiePrefabPath = "Assets/Prefabs/Zombie/BigZombie_AI.prefab";
    private const string BigZombieControllerPath = "Assets/Animations/BigZombie/BigZombieAnimator.controller";
    private const string RoarFbxPath = "Assets/Zombie/Animation/zombie@roar.fbx";
    private const string RoarTriggerName = "Roar";
    private const string BossRoarSfxPath = "Assets/Audio/SFX/Zombie/BigZombie.mp3";

    // Continues Wave1Zone/Wave2Zone/Wave3Zone's own 60-unit X stagger (0, 60, 120) — a
    // fourth, clearly-separated placeholder slot for the boss arena.
    private const float ArenaOffsetX = 180f;

    private static readonly Color PanelBackgroundColor = new Color(0.07f, 0.045f, 0.035f, 0.88f);
    private static readonly Color AccentOrange = new Color(1f, 0.55f, 0.15f, 1f);
    private static readonly Color AccentRed = new Color(0.92f, 0.2f, 0.14f, 1f);
    private static readonly Color TextTan = new Color(0.87f, 0.8f, 0.68f, 1f);

    [MenuItem("Tools/Zombie War/Setup Boss Fight")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[BossFightSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        EnsureBossCameraTargetOnPrefab();
        EnsureRoarAnimatorState();

        Transform playerTransform = FindInActiveScene(activeScene, "Player");

        if (playerTransform == null)
        {
            Debug.LogError("[BossFightSetup] Could not find 'Player' in the active scene. Aborted.");
            return;
        }

        Transform gameCanvasTransform = FindInActiveScene(activeScene, "GameCanvas");

        if (gameCanvasTransform == null)
        {
            Debug.LogError("[BossFightSetup] Could not find 'GameCanvas' in the active scene. Aborted.");
            return;
        }

        Transform playerFollowTransform = FindInActiveScene(activeScene, "CM_PlayerFollow");
        CinemachineVirtualCamera playerFollowCamera = playerFollowTransform != null
            ? playerFollowTransform.GetComponent<CinemachineVirtualCamera>()
            : null;

        if (playerFollowCamera == null)
        {
            Debug.LogWarning("[BossFightSetup] Could not find 'CM_PlayerFollow' (CinemachineVirtualCamera) in the active scene — camera switching won't return to the player camera correctly until this is wired by hand.");
        }

        CinemachineImpulseSource playerFollowImpulseSource = playerFollowTransform != null
            ? playerFollowTransform.GetComponent<CinemachineImpulseSource>()
            : null;

        Transform levelResultManagerTransform = FindInActiveScene(activeScene, "LevelResultManager");
        LevelResultManager levelResultManager = levelResultManagerTransform != null
            ? levelResultManagerTransform.GetComponent<LevelResultManager>()
            : null;

        PlayerMovement playerMovement = playerTransform.GetComponent<PlayerMovement>();
        WeaponController weaponController = playerTransform.GetComponent<WeaponController>();
        PlayerBombController bombController = playerTransform.GetComponent<PlayerBombController>();

        // ---- BossArena ----------------------------------------------------------------
        Transform arenaTransform = GetOrCreateRootChild(activeScene, "BossArena", out bool arenaIsNew);

        if (arenaIsNew)
        {
            arenaTransform.position = new Vector3(ArenaOffsetX, 0f, 0f);
        }

        GameObject bossInstance = GetOrCreateBossInstance(arenaTransform, out bool bossIsNew);

        if (bossIsNew)
        {
            bossInstance.transform.localPosition = new Vector3(0f, 0f, 20f);
        }

        ZombieAI bossAI = bossInstance.GetComponent<ZombieAI>();
        ZombieHealth bossHealth = bossInstance.GetComponent<ZombieHealth>();
        Animator bossAnimator = bossInstance.GetComponentInChildren<Animator>(true);
        Renderer[] bossRenderers = bossInstance.GetComponentsInChildren<Renderer>(true);
        Transform bossCameraTarget = bossInstance.transform.Find("BossCameraTarget");

        AudioSource bossAudioSource = bossInstance.GetComponent<AudioSource>();

        if (bossAudioSource == null)
        {
            bossAudioSource = Undo.AddComponent<AudioSource>(bossInstance);
        }

        bossAudioSource.playOnAwake = false;
        bossAudioSource.loop = false;
        bossAudioSource.spatialBlend = 0.85f;
        bossAudioSource.minDistance = 3f;
        bossAudioSource.maxDistance = 32f;
        bossAudioSource.rolloffMode = AudioRolloffMode.Linear;

        AudioClip bossRoarClip = AssetDatabase.LoadAssetAtPath<AudioClip>(BossRoarSfxPath);

        Transform triggerTransform = GetOrCreateChild(arenaTransform, "BossIntroTrigger", out bool triggerIsNew);
        BoxCollider triggerCollider = triggerTransform.GetComponent<BoxCollider>();

        if (triggerCollider == null)
        {
            triggerCollider = Undo.AddComponent<BoxCollider>(triggerTransform.gameObject);
        }

        triggerCollider.isTrigger = true;

        if (triggerIsNew)
        {
            triggerCollider.size = new Vector3(10f, 4f, 4f);
            triggerTransform.localPosition = new Vector3(0f, 2f, 8f);
        }

        BossIntroTrigger introTrigger = triggerTransform.GetComponent<BossIntroTrigger>();

        if (introTrigger == null)
        {
            introTrigger = Undo.AddComponent<BossIntroTrigger>(triggerTransform.gameObject);
        }

        // ---- BossCombatTargetGroup ------------------------------------------------------
        Transform targetGroupTransform = GetOrCreateChild(arenaTransform, "BossCombatTargetGroup", out _);
        CinemachineTargetGroup targetGroup = targetGroupTransform.GetComponent<CinemachineTargetGroup>();

        if (targetGroup == null)
        {
            targetGroup = Undo.AddComponent<CinemachineTargetGroup>(targetGroupTransform.gameObject);
        }

        EnsureTargetGroupMember(targetGroup, playerTransform, 1f, 1.2f);
        EnsureTargetGroupMember(targetGroup, bossCameraTarget, 1f, 2f);
        EditorUtility.SetDirty(targetGroup);

        // ---- Cameras (root-level, siblings of CM_PlayerFollow) --------------------------
        Transform introCamTransform = GetOrCreateRootChild(activeScene, "CM_BossIntro", out bool introCamIsNew);
        CinemachineVirtualCamera introCam = introCamTransform.GetComponent<CinemachineVirtualCamera>();

        if (introCam == null)
        {
            introCam = Undo.AddComponent<CinemachineVirtualCamera>(introCamTransform.gameObject);
        }

        if (introCamIsNew)
        {
            // Deliberately no Body/Aim component — BossCameraController positions/rotates
            // this vcam's own Transform directly as a static "hero shot" every time
            // ConfigureCameraForBoss runs, so no pipeline should fight that.
            introCam.Priority = 0;
            introCam.m_Lens.FieldOfView = 50f;
        }

        Transform combatCamTransform = GetOrCreateRootChild(activeScene, "CM_BossCombat", out bool combatCamIsNew);
        CinemachineVirtualCamera combatCam = combatCamTransform.GetComponent<CinemachineVirtualCamera>();

        if (combatCam == null)
        {
            combatCam = Undo.AddComponent<CinemachineVirtualCamera>(combatCamTransform.gameObject);
        }

        if (combatCamIsNew)
        {
            // Position isn't set here — BossCameraController.ConfigureCombatCamera assigns
            // Follow=Player every time a boss fight starts, so Cinemachine's own Transposer
            // takes over positioning immediately; a one-time Editor-tool placement would
            // just be overwritten anyway.
            combatCam.Priority = 0;
        }

        // Same Body component type as CM_PlayerFollow (CinemachineTransposer, not
        // FramingTransposer) — BossCameraController.ConfigureCombatCamera sets Follow=Player
        // and BindingMode=LockToTargetWithWorldUp on this every fight, so it orbits behind
        // the Player exactly like the normal follow camera, just scaled further back.
        CinemachineTransposer transposer = combatCam.GetCinemachineComponent<CinemachineTransposer>();

        if (transposer == null)
        {
            transposer = combatCam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetWithWorldUp;
        }

        CinemachineComposer combatComposer = combatCam.GetCinemachineComponent<CinemachineComposer>();

        if (combatComposer == null)
        {
            combatComposer = combatCam.AddCinemachineComponent<CinemachineComposer>();

            // Same screen composition as CM_PlayerFollow's own Composer, for visual
            // consistency between the two cameras.
            combatComposer.m_ScreenX = 0.5f;
            combatComposer.m_ScreenY = 0.6f;
            combatComposer.m_SoftZoneWidth = 0.8f;
            combatComposer.m_SoftZoneHeight = 0.8f;
            combatComposer.m_HorizontalDamping = 0.5f;
            combatComposer.m_VerticalDamping = 0.5f;
        }

        // ---- BossCameraRig ----------------------------------------------------------------
        Transform cameraRigTransform = GetOrCreateChild(arenaTransform, "BossCameraRig", out _);
        BossCameraController cameraController = cameraRigTransform.GetComponent<BossCameraController>();

        if (cameraController == null)
        {
            cameraController = Undo.AddComponent<BossCameraController>(cameraRigTransform.gameObject);
        }

        SerializedObject serializedCameraController = new SerializedObject(cameraController);
        SetReference(serializedCameraController, "playerFollowCamera", playerFollowCamera);
        SetReference(serializedCameraController, "bossIntroCamera", introCam);
        SetReference(serializedCameraController, "bossCombatCamera", combatCam);
        SetReference(serializedCameraController, "bossCombatTargetGroup", targetGroup);
        SetReference(serializedCameraController, "playerTransform", playerTransform);
        SetReference(serializedCameraController, "bossCameraTarget", bossCameraTarget);
        SetReference(serializedCameraController, "roarImpulseSource", playerFollowImpulseSource);
        serializedCameraController.ApplyModifiedProperties();

        if (playerFollowImpulseSource == null)
        {
            Debug.LogWarning("[BossFightSetup] CM_PlayerFollow has no CinemachineImpulseSource — roar camera shake will silently no-op. Add one to CM_PlayerFollow if you want the shake.");
        }

        // ---- UI: BossIntroPanel + BossHealthPanel under GameCanvas -----------------------
        BossIntroUI bossIntroUI = BuildBossIntroPanel(gameCanvasTransform);
        BossHealthUI bossHealthUI = BuildBossHealthPanel(gameCanvasTransform);

        // ---- BossFightManager (nested under BossArena, mirroring WaveSystem/WaveManager) -
        Transform managerTransform = GetOrCreateChild(arenaTransform, "BossFightManager", out _);
        BossFightManager fightManager = managerTransform.GetComponent<BossFightManager>();

        if (fightManager == null)
        {
            fightManager = Undo.AddComponent<BossFightManager>(managerTransform.gameObject);
        }

        SerializedObject serializedManager = new SerializedObject(fightManager);
        SetReference(serializedManager, "bossObject", bossInstance);
        SetReference(serializedManager, "bossAI", bossAI);
        SetReference(serializedManager, "bossHealth", bossHealth);
        SetReference(serializedManager, "bossAnimator", bossAnimator);
        SetReference(serializedManager, "bossCameraTarget", bossCameraTarget);
        SetReference(serializedManager, "playerTransform", playerTransform);
        SetReference(serializedManager, "playerMovement", playerMovement);
        SetReference(serializedManager, "weaponController", weaponController);
        SetReference(serializedManager, "bombController", bombController);
        SetReference(serializedManager, "bossCameraController", cameraController);
        SetReference(serializedManager, "bossIntroUI", bossIntroUI);
        SetReference(serializedManager, "bossHealthUI", bossHealthUI);
        SetReference(serializedManager, "gameCanvasRoot", gameCanvasTransform);
        SetReference(serializedManager, "bossAudioSource", bossAudioSource);
        SetReference(serializedManager, "bossRoarClip", bossRoarClip);

        SerializedProperty renderersProperty = serializedManager.FindProperty("bossRenderers");

        if (renderersProperty != null)
        {
            renderersProperty.arraySize = bossRenderers.Length;

            for (int i = 0; i < bossRenderers.Length; i++)
            {
                renderersProperty.GetArrayElementAtIndex(i).objectReferenceValue = bossRenderers[i];
            }
        }

        serializedManager.ApplyModifiedProperties();

        SerializedObject serializedTrigger = new SerializedObject(introTrigger);
        SetReference(serializedTrigger, "bossFightManager", fightManager);
        serializedTrigger.ApplyModifiedProperties();

        if (levelResultManager != null)
        {
            AddPersistentListenerOnce(fightManager.OnBossDefeatedEvent, levelResultManager, nameof(LevelResultManager.ShowWinPanel));

            SerializedObject serializedLevelResultManager = new SerializedObject(levelResultManager);
            SetReference(serializedLevelResultManager, "bossHealthUI", bossHealthUI);
            serializedLevelResultManager.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[BossFightSetup] Could not find 'LevelResultManager' in the active scene — onBossDefeated won't automatically show the Win panel, and BossHealthPanel won't auto-hide if the Player dies mid-fight. Wire both by hand or call BossFightManager.HandleBossDefeated() from your own logic.");
        }

        if (bossRoarClip == null)
        {
            Debug.LogWarning($"[BossFightSetup] Could not find a dedicated roar SFX at '{BossRoarSfxPath}' — bossRoarClip left unassigned; the intro will proceed silently until you assign one.");
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = managerTransform.gameObject;

        Debug.Log("[BossFightSetup] Boss Fight built/updated under 'BossArena' (+ CM_BossIntro/CM_BossCombat at scene root, BossIntroPanel/BossHealthPanel under GameCanvas). " +
            "Reposition BossArena/BigZombieBoss/BossIntroTrigger to match Level1's real layout, review BossFightManager's Inspector fields, then save the scene (Ctrl+S).");
    }

    // ------------------------------------------------------------------------------------
    // Prefab: BossCameraTarget
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Adds a BossCameraTarget child to BigZombie_AI.prefab, positioned from the prefab's own
    /// combined Renderer bounds (never the root pivot, which NavMeshAgent/CapsuleCollider data
    /// shows sits at ground level, and never the feet) — biased up toward the chest so the
    /// intro/combat cameras look at roughly torso height. BossCameraController repositions this
    /// same transform again at runtime from the live instance's bounds, so this placement only
    /// needs to be reasonable, not exact.
    /// </summary>
    private static void EnsureBossCameraTargetOnPrefab()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(BigZombiePrefabPath);

        try
        {
            if (prefabRoot.transform.Find("BossCameraTarget") != null)
            {
                return;
            }

            Renderer[] renderers = prefabRoot.GetComponentsInChildren<Renderer>(true);
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

            GameObject targetObject = new GameObject("BossCameraTarget");
            targetObject.transform.SetParent(prefabRoot.transform, false);

            targetObject.transform.position = started
                ? bounds.center + Vector3.up * (bounds.size.y * 0.15f)
                : prefabRoot.transform.position + Vector3.up * 1.2f;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, BigZombiePrefabPath);
            Debug.Log("[BossFightSetup] Added 'BossCameraTarget' to BigZombie_AI.prefab (positioned from its combined Renderer bounds).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static GameObject GetOrCreateBossInstance(Transform arenaTransform, out bool isNew)
    {
        Transform existing = FindImmediateChild(arenaTransform, "BigZombieBoss");

        if (existing != null)
        {
            isNew = false;
            return existing.gameObject;
        }

        GameObject bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BigZombiePrefabPath);

        if (bossPrefab == null)
        {
            Debug.LogError($"[BossFightSetup] Could not load prefab at '{BigZombiePrefabPath}'.");
            isNew = false;
            return null;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab, arenaTransform);
        instance.name = "BigZombieBoss";
        Undo.RegisterCreatedObjectUndo(instance, UndoLabel);
        isNew = true;
        return instance;
    }

    // ------------------------------------------------------------------------------------
    // Animator: Roar trigger + state (additive only — never touches existing states)
    // ------------------------------------------------------------------------------------

    private static void EnsureRoarAnimatorState()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BigZombieControllerPath);

        if (controller == null)
        {
            Debug.LogWarning($"[BossFightSetup] Could not load AnimatorController at '{BigZombieControllerPath}' — skipping Roar state setup.");
            return;
        }

        bool hasParameter = false;

        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == RoarTriggerName)
            {
                hasParameter = true;
                break;
            }
        }

        if (!hasParameter)
        {
            controller.AddParameter(RoarTriggerName, AnimatorControllerParameterType.Trigger);
        }

        if (controller.layers.Length == 0)
        {
            Debug.LogWarning("[BossFightSetup] BigZombieAnimator.controller has no layers — skipping Roar state setup.");
            return;
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        foreach (ChildAnimatorState childState in stateMachine.states)
        {
            if (childState.state.name == "Roar")
            {
                // Already set up on a previous run — leave it exactly as it is (a designer
                // may have since retimed the transitions by hand).
                return;
            }
        }

        AnimationClip roarClip = LoadRoarClip();

        if (roarClip == null)
        {
            Debug.LogWarning($"[BossFightSetup] Could not find an AnimationClip inside '{RoarFbxPath}' — skipping Roar state setup. BossFightManager will log a warning and skip the roar animation at runtime instead of crashing.");
            return;
        }

        AnimatorState returnState = stateMachine.defaultState;

        AnimatorState roarState = stateMachine.AddState("Roar");
        roarState.motion = roarClip;

        AnimatorStateTransition anyStateToRoar = stateMachine.AddAnyStateTransition(roarState);
        anyStateToRoar.hasExitTime = false;
        anyStateToRoar.hasFixedDuration = true;
        anyStateToRoar.duration = 0.1f;
        anyStateToRoar.canTransitionToSelf = false;
        anyStateToRoar.AddCondition(AnimatorConditionMode.If, 0f, RoarTriggerName);

        if (returnState != null)
        {
            AnimatorStateTransition roarToReturn = roarState.AddTransition(returnState);
            roarToReturn.hasExitTime = true;
            roarToReturn.exitTime = 0.9f;
            roarToReturn.hasFixedDuration = true;
            roarToReturn.duration = 0.25f;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[BossFightSetup] Added a 'Roar' trigger + state to BigZombieAnimator.controller (Any State -> Roar -> back to the default state), driven by the existing zombie@roar.fbx clip.");
    }

    private static AnimationClip LoadRoarClip()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(RoarFbxPath);

        foreach (Object asset in assets)
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------------------------
    // UI: BossIntroPanel (left-anchored card — deliberately not full-screen, so it never
    // covers the boss, who's framed centrally by CM_BossIntro)
    // ------------------------------------------------------------------------------------

    private static BossIntroUI BuildBossIntroPanel(Transform gameCanvasTransform)
    {
        Transform existing = FindImmediateChild(gameCanvasTransform, "BossIntroPanel");
        bool isNew = existing == null;

        GameObject panelObject;
        Image background;

        if (!isNew)
        {
            panelObject = existing.gameObject;
            background = panelObject.GetComponent<Image>();
        }
        else
        {
            panelObject = new GameObject("BossIntroPanel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panelObject, UndoLabel);
            panelObject.transform.SetParent(gameCanvasTransform, false);
            background = Undo.AddComponent<Image>(panelObject);
        }

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();

        if (isNew)
        {
            panelRect.anchorMin = new Vector2(0f, 0.5f);
            panelRect.anchorMax = new Vector2(0f, 0.5f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.sizeDelta = new Vector2(560f, 520f);
            panelRect.anchoredPosition = new Vector2(60f, 0f);

            background.color = PanelBackgroundColor;
            background.raycastTarget = false;
        }

        TMP_Text nameText = GetOrCreateTmpText(panelRect, "BossNameText", "BOSS ZOMBIE", 46f, AccentOrange, FontStyles.Bold, out bool nameIsNew);

        if (nameIsNew)
        {
            SetTopAnchoredRect(nameText.rectTransform, 0f, -30f, 500f, 60f);
        }

        TMP_Text typeText = GetOrCreateTmpText(panelRect, "BossTypeText", "THE EXECUTIONER", 24f, TextTan, FontStyles.Normal, out bool typeIsNew);

        if (typeIsNew)
        {
            SetTopAnchoredRect(typeText.rectTransform, 0f, -92f, 500f, 36f);
        }

        Transform statsRootExisting = FindImmediateChild(panelRect, "BossStatsRoot");
        RectTransform statsRoot;
        bool statsRootIsNew = statsRootExisting == null;

        if (!statsRootIsNew)
        {
            statsRoot = statsRootExisting.GetComponent<RectTransform>();
        }
        else
        {
            GameObject statsRootObject = new GameObject("BossStatsRoot", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(statsRootObject, UndoLabel);
            statsRootObject.transform.SetParent(panelRect, false);
            statsRoot = statsRootObject.GetComponent<RectTransform>();
            statsRoot.anchorMin = new Vector2(0f, 1f);
            statsRoot.anchorMax = new Vector2(0f, 1f);
            statsRoot.pivot = new Vector2(0f, 1f);
            statsRoot.anchoredPosition = new Vector2(0f, -150f);
            statsRoot.sizeDelta = new Vector2(520f, 220f);
        }

        TMP_Text healthText = GetOrCreateTmpText(statsRoot, "HealthText", "HP: 1000", 28f, TextTan, FontStyles.Normal, out bool healthIsNew);
        if (healthIsNew) SetTopAnchoredRect(healthText.rectTransform, 20f, 0f, 480f, 36f, TextAlignmentOptions.Left);

        TMP_Text damageText = GetOrCreateTmpText(statsRoot, "DamageText", "DAMAGE: 40", 28f, TextTan, FontStyles.Normal, out bool damageIsNew);
        if (damageIsNew) SetTopAnchoredRect(damageText.rectTransform, 20f, -38f, 480f, 36f, TextAlignmentOptions.Left);

        TMP_Text speedText = GetOrCreateTmpText(statsRoot, "SpeedText", "SPEED: 2.5", 28f, TextTan, FontStyles.Normal, out bool speedIsNew);
        if (speedIsNew) SetTopAnchoredRect(speedText.rectTransform, 20f, -76f, 480f, 36f, TextAlignmentOptions.Left);

        TMP_Text attackRangeText = GetOrCreateTmpText(statsRoot, "AttackRangeText", "ATTACK RANGE: 2.8", 28f, TextTan, FontStyles.Normal, out bool rangeIsNew);
        if (rangeIsNew) SetTopAnchoredRect(attackRangeText.rectTransform, 20f, -114f, 480f, 36f, TextAlignmentOptions.Left);

        TMP_Text warningText = GetOrCreateTmpText(panelRect, "WarningText", "WARNING:\nELIMINATE THE BOSS", 24f, AccentRed, FontStyles.Bold, out bool warningIsNew);

        if (warningIsNew)
        {
            warningText.alignment = TextAlignmentOptions.Bottom;
            RectTransform warningRect = warningText.rectTransform;
            warningRect.anchorMin = new Vector2(0f, 0f);
            warningRect.anchorMax = new Vector2(0f, 0f);
            warningRect.pivot = new Vector2(0f, 0f);
            warningRect.sizeDelta = new Vector2(500f, 80f);
            warningRect.anchoredPosition = new Vector2(0f, 24f);
        }

        CanvasGroup canvasGroup = panelObject.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = Undo.AddComponent<CanvasGroup>(panelObject);
        }

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        BossIntroUI introUI = panelObject.GetComponent<BossIntroUI>();

        if (introUI == null)
        {
            introUI = Undo.AddComponent<BossIntroUI>(panelObject);
        }

        SerializedObject serializedIntroUI = new SerializedObject(introUI);
        SetReference(serializedIntroUI, "root", panelObject);
        SetReference(serializedIntroUI, "canvasGroup", canvasGroup);
        SetReference(serializedIntroUI, "panelRect", panelRect);
        SetReference(serializedIntroUI, "bossNameText", nameText);
        SetReference(serializedIntroUI, "bossTypeText", typeText);
        SetReference(serializedIntroUI, "healthText", healthText);
        SetReference(serializedIntroUI, "damageText", damageText);
        SetReference(serializedIntroUI, "speedText", speedText);
        SetReference(serializedIntroUI, "attackRangeText", attackRangeText);
        SetReference(serializedIntroUI, "warningText", warningText);
        serializedIntroUI.ApplyModifiedProperties();

        if (isNew)
        {
            panelObject.SetActive(false);
        }

        return introUI;
    }

    // ------------------------------------------------------------------------------------
    // UI: BossHealthPanel (top-right anchored — mirrors the existing top-left Player HP bar)
    // ------------------------------------------------------------------------------------

    private static BossHealthUI BuildBossHealthPanel(Transform gameCanvasTransform)
    {
        Transform existing = FindImmediateChild(gameCanvasTransform, "BossHealthPanel");
        bool isNew = existing == null;

        GameObject panelObject;

        if (!isNew)
        {
            panelObject = existing.gameObject;
        }
        else
        {
            panelObject = new GameObject("BossHealthPanel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panelObject, UndoLabel);
            panelObject.transform.SetParent(gameCanvasTransform, false);
        }

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();

        if (isNew)
        {
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.sizeDelta = new Vector2(560f, 150f);
            panelRect.anchoredPosition = new Vector2(-40f, -40f);
        }

        TMP_Text nameText = GetOrCreateTmpText(panelRect, "BossNameText", "BOSS ZOMBIE", 32f, AccentOrange, FontStyles.Bold, out bool nameIsNew);

        if (nameIsNew)
        {
            SetTopAnchoredRect(nameText.rectTransform, 0f, 0f, 540f, 44f);
        }

        RectTransform backgroundRect = GetOrCreateUIChildImage(panelRect, "BossHealthBackground", out Image backgroundImage, out bool backgroundIsNew);

        if (backgroundIsNew)
        {
            backgroundRect.anchorMin = new Vector2(0f, 1f);
            backgroundRect.anchorMax = new Vector2(0f, 1f);
            backgroundRect.pivot = new Vector2(0f, 1f);
            backgroundRect.anchoredPosition = new Vector2(0f, -50f);
            backgroundRect.sizeDelta = new Vector2(540f, 40f);
            backgroundImage.color = new Color(0.05f, 0.03f, 0.03f, 0.9f);
            backgroundImage.raycastTarget = false;
        }

        RectTransform fillRect = GetOrCreateUIChildImage(backgroundRect, "BossHealthFill", out Image fillImage, out bool fillIsNew);

        if (fillIsNew)
        {
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);
            fillImage.color = AccentOrange;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;
        }

        TMP_Text valueText = GetOrCreateTmpText(backgroundRect, "BossHealthValueText", "1000 / 1000", 22f, Color.white, FontStyles.Normal, out bool valueIsNew);

        if (valueIsNew)
        {
            StretchFull(valueText.rectTransform);
        }

        TMP_Text phaseText = GetOrCreateTmpText(panelRect, "BossPhaseText", "PHASE 1", 22f, AccentRed, FontStyles.Bold, out bool phaseIsNew);

        if (phaseIsNew)
        {
            RectTransform phaseRect = phaseText.rectTransform;
            phaseRect.anchorMin = new Vector2(0f, 1f);
            phaseRect.anchorMax = new Vector2(0f, 1f);
            phaseRect.pivot = new Vector2(0f, 1f);
            phaseRect.anchoredPosition = new Vector2(0f, -98f);
            phaseRect.sizeDelta = new Vector2(200f, 32f);
            phaseText.alignment = TextAlignmentOptions.Left;
            phaseText.gameObject.SetActive(false);
        }

        CanvasGroup canvasGroup = panelObject.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = Undo.AddComponent<CanvasGroup>(panelObject);
        }

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        BossHealthUI healthUI = panelObject.GetComponent<BossHealthUI>();

        if (healthUI == null)
        {
            healthUI = Undo.AddComponent<BossHealthUI>(panelObject);
        }

        Transform waveHudTransform = FindImmediateChild(gameCanvasTransform, "WaveHUD");

        SerializedObject serializedHealthUI = new SerializedObject(healthUI);
        SetReference(serializedHealthUI, "root", panelObject);
        SetReference(serializedHealthUI, "canvasGroup", canvasGroup);
        SetReference(serializedHealthUI, "healthFill", fillImage);
        SetReference(serializedHealthUI, "bossNameText", nameText);
        SetReference(serializedHealthUI, "healthValueText", valueText);
        SetReference(serializedHealthUI, "bossPhaseText", phaseText);
        SetReference(serializedHealthUI, "waveHudRoot", waveHudTransform != null ? waveHudTransform.gameObject : null);

        SerializedProperty gradientProperty = serializedHealthUI.FindProperty("healthGradient");

        if (gradientProperty != null && isNew)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(AccentRed, 0f),
                    new GradientColorKey(AccentOrange, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });

            gradientProperty.gradientValue = gradient;
        }

        // Was inert until BossFightManager gained an actual Phase 2 stat-boost to go with it —
        // turned on unconditionally (not isNew-gated) since it's the direct feature being
        // wired up now, not a creative choice this tool would otherwise be overwriting.
        SerializedProperty enableBossPhasesProperty = serializedHealthUI.FindProperty("enableBossPhases");

        if (enableBossPhasesProperty != null)
        {
            enableBossPhasesProperty.boolValue = true;
        }

        serializedHealthUI.ApplyModifiedProperties();

        if (isNew)
        {
            panelObject.SetActive(false);
        }

        return healthUI;
    }

    // ------------------------------------------------------------------------------------
    // Small shared helpers
    // ------------------------------------------------------------------------------------

    private static void EnsureTargetGroupMember(CinemachineTargetGroup group, Transform target, float weight, float radius)
    {
        if (target == null)
        {
            return;
        }

        CinemachineTargetGroup.Target[] targets = group.m_Targets;

        if (targets != null)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i].target == target)
                {
                    targets[i].weight = weight;
                    targets[i].radius = radius;
                    group.m_Targets = targets;
                    return;
                }
            }
        }

        int oldLength = targets?.Length ?? 0;
        CinemachineTargetGroup.Target[] resized = new CinemachineTargetGroup.Target[oldLength + 1];

        for (int i = 0; i < oldLength; i++)
        {
            resized[i] = targets[i];
        }

        resized[oldLength] = new CinemachineTargetGroup.Target { target = target, weight = weight, radius = radius };
        group.m_Targets = resized;
    }

    private static void SetTopAnchoredRect(RectTransform rect, float x, float y, float width, float height, TextAlignmentOptions? alignment = null)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);

        if (alignment.HasValue)
        {
            TMP_Text text = rect.GetComponent<TMP_Text>();

            if (text != null)
            {
                text.alignment = alignment.Value;
            }
        }
    }

    private static TMP_Text GetOrCreateTmpText(Transform parent, string name, string defaultText, float fontSize, Color color, FontStyles fontStyle, out bool isNew)
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject textObject;
        isNew = existing == null;

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
            text.color = color;
            text.fontStyle = fontStyle;
            text.raycastTarget = false;
        }

        EditorUtility.SetDirty(text);
        return text;
    }

    private static RectTransform GetOrCreateUIChildImage(Transform parent, string name, out Image image, out bool isNew)
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject childObject;
        isNew = existing == null;

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

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
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
            Debug.LogWarning($"[BossFightSetup] Property '{propertyName}' not found on {serializedObject.targetObject?.GetType().Name}.");
        }
    }

    private static void AddPersistentListenerOnce(UnityEventBase unityEvent, Object target, string methodName)
    {
        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
        {
            if (unityEvent.GetPersistentTarget(i) == target && unityEvent.GetPersistentMethodName(i) == methodName)
            {
                return;
            }
        }

        System.Reflection.MethodInfo method = target.GetType().GetMethod(methodName, System.Type.EmptyTypes);

        if (method == null)
        {
            Debug.LogWarning($"[BossFightSetup] Could not find parameterless method '{methodName}' on {target.GetType().Name}.");
            return;
        }

        UnityAction action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, method);

        if (unityEvent is UnityEvent typedEvent)
        {
            UnityEventTools.AddPersistentListener(typedEvent, action);
        }
    }

    private static Transform GetOrCreateChild(Transform parent, string name, out bool isNew)
    {
        Transform existing = FindImmediateChild(parent, name);

        if (existing != null)
        {
            isNew = false;
            return existing;
        }

        GameObject childObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(childObject, UndoLabel);
        childObject.transform.SetParent(parent, false);
        isNew = true;
        return childObject.transform;
    }

    private static Transform GetOrCreateRootChild(Scene scene, string name, out bool isNew)
    {
        Transform existing = FindRootObject(scene, name);

        if (existing != null)
        {
            isNew = false;
            return existing;
        }

        GameObject childObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(childObject, UndoLabel);
        SceneManager.MoveGameObjectToScene(childObject, scene);
        isNew = true;
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
