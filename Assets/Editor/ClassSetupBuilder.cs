using System.Collections.Generic;
using System.Text;
using Subterraneorum.DungeonSystem;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public static class ClassSetupBuilder
{
    private const string FbxFolder = "Assets/RPG Characters/FBX";
    private const string PlayerFolder = "Assets/prefabs/Players";
    private const string ControllerFolder = "Assets/Animations/Players";
    private const string ClassFolder = "Assets/Data/Classes";
    private const string BasePrefabPath = "Assets/prefabs/PlayerPrefab.prefab";
    private const string SeatPrefabPath = PlayerFolder + "/PlayerSeat.prefab";
    private const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string BaseControllerPath = ControllerFolder + "/PlayerClass_AC.controller";
    private const int PlayerLayer = 8;

    private const string IdleSlot = "PlayerIdle";
    private const string RunSlot = "PlayerRun";
    private const string DashSlot = "PlayerDash";
    private const string AttackSlot = "PlayerAttack";
    private const string HitSlot = "PlayerHit";
    private const string DeathSlot = "PlayerDeath";

    private static readonly string[] LoopingClips = { "Idle", "Run", "Walk", "Idle_Weapon", "Run_Weapon", "Idle_Attacking", "Attacking_Idle", "Run_Holding" };

    private struct ClassSpec
    {
        public string classId;
        public string displayName;
        public string roleName;
        public string description;
        public Color color;
        public string attackClip;

        public ClassSpec(string classId, string displayName, string roleName, string description, Color color, string attackClip)
        {
            this.classId = classId;
            this.displayName = displayName;
            this.roleName = roleName;
            this.description = description;
            this.color = color;
            this.attackClip = attackClip;
        }
    }

    private static readonly ClassSpec[] Specs =
    {
        new ClassSpec("Warrior", "Nanus Ferox", "Guerreiro", "Linha de frente: espada pesada, aguenta o tranco e protege o grupo.", new Color(0.9f, 0.35f, 0.25f), "Sword_Attack"),
        new ClassSpec("Ranger", "Aquila", "Arqueira", "Ataca de longe com o arco e se move rápido pelo campo.", new Color(0.4f, 0.8f, 0.35f), "Bow_Attack_Shoot"),
        new ClassSpec("Wizard", "Pollienus", "Mago", "Magia em área contra hordas e cura para os aliados.", new Color(0.35f, 0.6f, 1f), "Staff_Attack"),
        new ClassSpec("Rogue", "Sangarinus", "Assassino", "Golpes rápidos de adaga que roubam a vida dos inimigos.", new Color(0.75f, 0.45f, 0.9f), "Dagger_Attack"),
    };

    [MenuItem("Subterraneorum/Configurar Classes e Multiplayer Local")]
    public static void Build()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var log = new StringBuilder("[ClassSetupBuilder]\n");
        EnsureFolder("Assets/prefabs");
        EnsureFolder(PlayerFolder);
        EnsureFolder("Assets/Animations");
        EnsureFolder(ControllerFolder);
        EnsureFolder("Assets/Data");
        EnsureFolder(ClassFolder);

        var clipsByClass = new Dictionary<string, Dictionary<string, AnimationClip>>();
        foreach (ClassSpec spec in Specs)
        {
            string modelPath = $"{FbxFolder}/{spec.classId}.fbx";
            ConfigureLooping(modelPath, log);
            Dictionary<string, AnimationClip> clips = LoadClips(modelPath);
            foreach (string required in new[] { "Idle", "Run", "Roll", "Death", "RecieveHit", spec.attackClip })
            {
                if (!clips.ContainsKey(required))
                {
                    throw new System.InvalidOperationException($"Clip '{required}' não encontrado em {modelPath}. Clips: {string.Join(", ", clips.Keys)}");
                }
            }

            clipsByClass[spec.classId] = clips;
        }

        AnimatorController baseController = CreateBaseController(clipsByClass["Warrior"]["Roll"]);
        log.AppendLine($"  - Animator base: {BaseControllerPath}");

        PrepareBasePrefab(log);
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);

        var definitions = new List<ClassDefinition>();
        foreach (ClassSpec spec in Specs)
        {
            AnimatorOverrideController overrideController = CreateOverride(spec, baseController, clipsByClass[spec.classId]);
            ClassDefinition definition = CreateOrLoadDefinition(spec);
            GameObject variant = CreateVariant(spec, basePrefab, overrideController, definition);
            definition.prefab = variant;
            EditorUtility.SetDirty(definition);
            definitions.Add(definition);
            log.AppendLine($"  - {spec.displayName} ({spec.roleName}): {AssetDatabase.GetAssetPath(variant)}, ataque '{spec.attackClip}'");
        }

        GameObject seatPrefab = CreateSeatPrefab();
        log.AppendLine($"  - PlayerSeat: {SeatPrefabPath}");
        ConfigureScene(seatPrefab, definitions, log);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(log.ToString());
    }

    [MenuItem("Subterraneorum/Validar Classes")]
    public static void Validate()
    {
        var report = new StringBuilder("[ClassSetupValidate]\n");
        foreach (ClassSpec spec in Specs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PlayerFolder}/Player_{spec.classId}.prefab");
            if (prefab == null)
            {
                report.AppendLine($"  {spec.classId}: prefab NÃO ENCONTRADO");
                continue;
            }

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                Bounds bounds = new Bounds(instance.transform.position, Vector3.zero);
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
                {
                    if (r is SkinnedMeshRenderer || r is MeshRenderer) bounds.Encapsulate(r.bounds);
                }

                CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
                Animator animator = instance.GetComponentInChildren<Animator>();
                Transform model = instance.transform.Find("Model");
                report.AppendLine($"  {spec.classId}: modelo size={bounds.size} center={bounds.center} | cápsula h={capsule?.height} r={capsule?.radius} c={capsule?.center} | Model rot={model?.localEulerAngles} scale={model?.localScale}");
                report.AppendLine($"     PlayerInput na raiz: {instance.GetComponent<PlayerInput>() != null} | renderers: {instance.GetComponentsInChildren<SkinnedMeshRenderer>().Length} skinned, {instance.GetComponentsInChildren<MeshRenderer>().Length} mesh");

                if (animator != null && animator.runtimeAnimatorController is AnimatorOverrideController aoc)
                {
                    var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    aoc.GetOverrides(overrides);
                    var parts = new List<string>();
                    foreach (var pair in overrides)
                    {
                        parts.Add($"{pair.Key.name}->{(pair.Value != null ? $"{ShortName(pair.Value.name)}({pair.Value.length:0.00}s{(pair.Value.isLooping ? ",loop" : "")})" : "VAZIO")}");
                    }

                    report.AppendLine($"     clips: {string.Join(", ", parts)}");
                }
                else
                {
                    report.AppendLine("     Animator/override NÃO CONFIGURADO");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        Debug.Log(report.ToString());
    }

    private static void ConfigureLooping(string modelPath, StringBuilder log)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
        {
            throw new System.InvalidOperationException($"Modelo não encontrado: {modelPath}");
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        bool changed = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            bool shouldLoop = System.Array.IndexOf(LoopingClips, ShortName(clip.name)) >= 0;
            if (clip.loopTime != shouldLoop)
            {
                clip.loopTime = shouldLoop;
                changed = true;
            }
        }

        if (changed || importer.clipAnimations.Length == 0)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            log.AppendLine($"  - loop de Idle/Run/Walk ligado em {modelPath}");
        }
    }

    private static Dictionary<string, AnimationClip> LoadClips(string modelPath)
    {
        var clips = new Dictionary<string, AnimationClip>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                clips[ShortName(clip.name)] = clip;
            }
        }

        return clips;
    }

    private static string ShortName(string clipName)
    {
        int bar = clipName.LastIndexOf('|');
        return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
    }

    private static AnimatorController CreateBaseController(AnimationClip referenceRoll)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(BaseControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(BaseControllerPath);
        controller.AddParameter("isMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("takeDamage", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("die", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("dash", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idle = AddState(sm, "Idle", IdleSlot, new Vector3(300f, 0f));
        AnimatorState run = AddState(sm, "Run", RunSlot, new Vector3(550f, 0f));
        AnimatorState dash = AddState(sm, "Dash", DashSlot, new Vector3(300f, 120f));
        AnimatorState attack = AddState(sm, "Attack", AttackSlot, new Vector3(550f, 120f));
        AnimatorState hit = AddState(sm, "Hit", HitSlot, new Vector3(300f, -120f));
        AnimatorState death = AddState(sm, "Death", DeathSlot, new Vector3(550f, -120f));
        sm.defaultState = idle;

        dash.speed = referenceRoll != null ? Mathf.Max(1f, referenceRoll.length / 0.45f) : 2f;
        attack.speed = 1.4f;

        AnimatorStateTransition toRun = idle.AddTransition(run);
        toRun.hasExitTime = false;
        toRun.duration = 0.1f;
        toRun.AddCondition(AnimatorConditionMode.If, 0f, "isMoving");
        AnimatorStateTransition toIdle = run.AddTransition(idle);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.1f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isMoving");

        AddAnyStateTrigger(sm, death, "die", false, 0.05f);
        AddAnyStateTrigger(sm, dash, "dash", true, 0.03f);
        AddAnyStateTrigger(sm, attack, "attack", true, 0.05f);
        AddAnyStateTrigger(sm, hit, "takeDamage", true, 0.05f);
        AddExit(dash, idle, 0.85f);
        AddExit(attack, idle, 0.9f);
        AddExit(hit, idle, 0.9f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimatorState AddState(AnimatorStateMachine sm, string stateName, string slot, Vector3 position)
    {
        AnimatorState state = sm.AddState(stateName, position);
        state.motion = CreatePlaceholderClip(slot);
        state.writeDefaultValues = true;
        return state;
    }

    private static void AddAnyStateTrigger(AnimatorStateMachine sm, AnimatorState target, string parameter, bool canTransitionToSelf, float duration)
    {
        AnimatorStateTransition transition = sm.AddAnyStateTransition(target);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.canTransitionToSelf = canTransitionToSelf;
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    private static void AddExit(AnimatorState from, AnimatorState to, float exitTime)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.duration = 0.1f;
    }

    private static AnimationClip CreatePlaceholderClip(string clipName)
    {
        string path = $"{ControllerFolder}/{clipName}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = clipName };
            AssetDatabase.CreateAsset(clip, path);
        }

        return clip;
    }

    private static AnimatorOverrideController CreateOverride(ClassSpec spec, AnimatorController baseController, Dictionary<string, AnimationClip> clips)
    {
        string path = $"{ControllerFolder}/{spec.classId}_AC.overrideController";
        AnimatorOverrideController overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
        if (overrideController == null)
        {
            overrideController = new AnimatorOverrideController(baseController);
            AssetDatabase.CreateAsset(overrideController, path);
        }

        overrideController.runtimeAnimatorController = baseController;

        var map = new Dictionary<string, AnimationClip>
        {
            { IdleSlot, clips["Idle"] },
            { RunSlot, clips["Run"] },
            { DashSlot, clips["Roll"] },
            { AttackSlot, clips[spec.attackClip] },
            { HitSlot, clips["RecieveHit"] },
            { DeathSlot, clips["Death"] },
        };

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(overrides);
        for (int i = 0; i < overrides.Count; i++)
        {
            if (map.TryGetValue(overrides[i].Key.name, out AnimationClip clip))
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, clip);
            }
        }

        overrideController.ApplyOverrides(overrides);
        EditorUtility.SetDirty(overrideController);
        return overrideController;
    }

    private static void PrepareBasePrefab(StringBuilder log)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(BasePrefabPath);
        try
        {
            PlayerInput input = contents.GetComponent<PlayerInput>();
            if (input != null)
            {
                Object.DestroyImmediate(input);
                log.AppendLine("  - PlayerPrefab: PlayerInput removido (agora na PlayerSeat)");
            }

            foreach (string childName in new[] { "CharacterArmature", "Warrior_Body", "Model" })
            {
                Transform child = contents.transform.Find(childName);
                if (child != null)
                {
                    Object.DestroyImmediate(child.gameObject);
                    log.AppendLine($"  - PlayerPrefab: modelo '{childName}' removido da base");
                }
            }

            Animator rootAnimator = contents.GetComponent<Animator>();
            if (rootAnimator != null)
            {
                Object.DestroyImmediate(rootAnimator);
            }

            if (contents.GetComponent<PlayerIdentity>() == null)
            {
                contents.AddComponent<PlayerIdentity>();
            }

            HitFeedback feedback = contents.GetComponent<HitFeedback>();
            if (feedback != null)
            {
                SetReference(feedback, "characterRenderer", null);
                SetReference(feedback, "originalMaterial", null);
            }

            PrefabUtility.SaveAsPrefabAsset(contents, BasePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static GameObject CreateVariant(ClassSpec spec, GameObject basePrefab, RuntimeAnimatorController controller, ClassDefinition definition)
    {
        string modelPath = $"{FbxFolder}/{spec.classId}.fbx";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        string path = $"{PlayerFolder}/Player_{spec.classId}.prefab";

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        try
        {
            instance.name = $"Player_{spec.classId}";

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model, instance.transform);
            modelInstance.name = "Model";
            modelInstance.transform.localPosition = Vector3.zero;
            SetLayerRecursively(modelInstance, PlayerLayer);

            Animator animator = modelInstance.GetComponent<Animator>();
            if (animator == null) animator = modelInstance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            SetReference(instance.GetComponent<PlayerStatus>(), "animator", animator);
            SetReference(instance.GetComponent<PlayerMovement>(), "animator", animator);
            SetReference(instance.GetComponent<PlayerCombat>(), "animator", animator);

            SkinnedMeshRenderer body = modelInstance.GetComponentInChildren<SkinnedMeshRenderer>();
            HitFeedback feedback = instance.GetComponent<HitFeedback>();
            if (feedback != null && body != null) SetReference(feedback, "characterRenderer", body);

            SetReference(instance.GetComponent<PlayerIdentity>(), "classDefinition", definition);

            PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static ClassDefinition CreateOrLoadDefinition(ClassSpec spec)
    {
        string path = $"{ClassFolder}/{spec.classId}.asset";
        ClassDefinition definition = AssetDatabase.LoadAssetAtPath<ClassDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<ClassDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }

        definition.classId = spec.classId;
        definition.displayName = spec.displayName;
        definition.roleName = spec.roleName;
        definition.description = spec.description;
        definition.color = spec.color;
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static GameObject CreateSeatPrefab()
    {
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        if (actions == null)
        {
            throw new System.InvalidOperationException($"Input actions não encontradas: {ActionsPath}");
        }

        var go = new GameObject("PlayerSeat");
        try
        {
            PlayerInput input = go.AddComponent<PlayerInput>();
            var so = new SerializedObject(input);
            so.FindProperty("m_Actions").objectReferenceValue = actions;
            so.FindProperty("m_DefaultActionMap").stringValue = "Player";
            so.FindProperty("m_NotificationBehavior").intValue = (int)PlayerNotifications.InvokeCSharpEvents;
            so.FindProperty("m_NeverAutoSwitchControlSchemes").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.AddComponent<PlayerSeat>();
            PrefabUtility.SaveAsPrefabAsset(go, SeatPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(SeatPrefabPath);
    }

    private static void ConfigureScene(GameObject seatPrefab, List<ClassDefinition> definitions, StringBuilder log)
    {
        var scene = EditorSceneManager.OpenScene(GameSetupBuilder.ResolveGameScene(), OpenSceneMode.Single);

        foreach (PlayerStatus status in Object.FindObjectsByType<PlayerStatus>(FindObjectsInactive.Include))
        {
            GameObject root = status.transform.root.gameObject;
            log.AppendLine($"  - cena: jogador fixo '{root.name}' removido");
            Object.DestroyImmediate(root);
        }

        PlayerInputManager manager = Object.FindAnyObjectByType<PlayerInputManager>();
        if (manager == null)
        {
            manager = new GameObject("PlayerInputManager").AddComponent<PlayerInputManager>();
        }

        var managerSo = new SerializedObject(manager);
        managerSo.FindProperty("m_NotificationBehavior").intValue = (int)PlayerNotifications.InvokeCSharpEvents;
        managerSo.FindProperty("m_JoinBehavior").intValue = (int)PlayerJoinBehavior.JoinPlayersWhenButtonIsPressed;
        managerSo.FindProperty("m_PlayerPrefab").objectReferenceValue = seatPrefab;
        managerSo.FindProperty("m_MaxPlayerCount").intValue = 4;
        managerSo.FindProperty("m_AllowJoining").boolValue = true;
        managerSo.FindProperty("m_SplitScreen").boolValue = false;
        managerSo.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("  - cena: PlayerInputManager (entrar apertando botão, máx. 4, PlayerSeat)");

        DungeonManager dungeon = Object.FindAnyObjectByType<DungeonManager>();
        if (dungeon != null)
        {
            var dungeonSo = new SerializedObject(dungeon);
            dungeonSo.FindProperty("generateOnStart").boolValue = false;
            dungeonSo.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning("[ClassSetupBuilder] DungeonManager não encontrado na cena.");
        }

        ClassSelectionLobby lobby = Object.FindAnyObjectByType<ClassSelectionLobby>();
        if (lobby == null)
        {
            lobby = new GameObject("ClassSelectLobby").AddComponent<ClassSelectionLobby>();
        }

        lobby.gameObject.name = "ClassSelectLobby";
        var lobbySo = new SerializedObject(lobby);
        SerializedProperty classesProp = lobbySo.FindProperty("classes");
        classesProp.arraySize = definitions.Count;
        for (int i = 0; i < definitions.Count; i++)
        {
            classesProp.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
        }

        lobbySo.FindProperty("inputManager").objectReferenceValue = manager;
        lobbySo.FindProperty("dungeonManager").objectReferenceValue = dungeon;
        lobbySo.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("  - cena: ClassSelectLobby ligado às 4 classes; DungeonManager espera a seleção (generateOnStart = false)");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetReference(Object target, string propertyName, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"[ClassSetupBuilder] Campo '{propertyName}' não existe em {target.GetType().Name}.");
            return;
        }

        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
