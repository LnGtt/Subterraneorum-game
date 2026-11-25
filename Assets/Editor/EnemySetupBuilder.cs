using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class EnemySetupBuilder
{
    private const string EnemyFolder = "Assets/Prefabs/Enemies";
    private const string ControllerFolder = "Assets/Animations/Enemies";
    private const int PlayerLayer = 8;
    private const int EnvironmentLayer = 9;
    private const int EnemyLayer = 10;
    private const int PlayerAttackLayer = 11;
    private const int EnemyAttackLayer = 12;

    public static void Build()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder(EnemyFolder);
        EnsureFolder("Assets/Animations");
        EnsureFolder(ControllerFolder);
        ConvertMonsterMaterialsToUrp();

        Material flashMaterial = CreateOrLoadFlashMaterial();
        Material projectileMaterial = CreateOrLoadProjectileMaterial();
        AnimatorController controller = CreateOrLoadController();

        GameObject projectilePrefab = CreateProjectilePrefab(projectileMaterial);
        GameObject meleePrefab = CreateEnemyPrefab(
            "EnemyMeleePrefab",
            "Assets/Level 1 Monster Pack/Prefabs/Slime/Slime_Red.prefab",
            "Slime_Level_1",
            controller,
            flashMaterial,
            typeof(EnemyMelee),
            60f,
            25f,
            10f,
            1.5f,
            1.2f,
            3.5f,
            1.2f);
        GameObject rangedPrefab = CreateEnemyPrefab(
            "EnemyRangedPrefab",
            "Assets/Level 1 Monster Pack/Prefabs/Bat/Bat_Violet.prefab",
            "Bat_Level_1",
            controller,
            flashMaterial,
            typeof(EnemyRanged),
            40f,
            25f,
            12f,
            6f,
            2f,
            2.5f,
            1.2f);
        GameObject heavyPrefab = CreateEnemyPrefab(
            "EnemyHeavyPrefab",
            "Assets/Level 1 Monster Pack/Prefabs/Ghost/Ghost_White.prefab",
            "Ghost_Lv1",
            controller,
            flashMaterial,
            typeof(EnemyHeavy),
            120f,
            50f,
            8f,
            2.2f,
            2.5f,
            2f,
            2.5f);

        ConfigureRangedPrefab(rangedPrefab, projectilePrefab);
        ConfigureHeavyPrefab(heavyPrefab);
        ConfigurePhysicsMatrix();
        PopulateSampleScene(meleePrefab, rangedPrefab, heavyPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static GameObject CreateEnemyPrefab(string prefabName, string sourcePath, string modelName, AnimatorController controller, Material flashMaterial, System.Type enemyType, float maxHealth, float xpReward, float detectionRadius, float attackRange, float attackCooldown, float speed, float attackRadius)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null)
        {
            throw new System.InvalidOperationException($"Prefab de origem não encontrado: {sourcePath}");
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
        instance.name = prefabName;
        instance.tag = "Enemy";
        instance.layer = EnemyLayer;
        SetLayerRecursively(instance, EnemyLayer);

        Animator animator = instance.GetComponent<Animator>();
        if (animator == null)
        {
            animator = instance.AddComponent<Animator>();
        }

        AnimatorOverrideController overrideController = CreateOverrideController(prefabName, controller, modelName);
        animator.runtimeAnimatorController = overrideController;

        Rigidbody body = instance.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = instance.AddComponent<Rigidbody>();
        }

        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        CapsuleCollider collider = instance.GetComponent<CapsuleCollider>();
        if (collider == null)
        {
            collider = instance.AddComponent<CapsuleCollider>();
        }

        Bounds bounds = CalculateBounds(instance);
        collider.center = instance.transform.InverseTransformPoint(bounds.center);
        collider.height = Mathf.Max(1f, bounds.size.y);
        collider.radius = Mathf.Max(0.35f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.35f);

        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            agent = instance.AddComponent<NavMeshAgent>();
        }

        agent.speed = speed;
        agent.acceleration = 12f;
        agent.angularSpeed = 720f;
        agent.radius = 0.3f;
        agent.height = collider.height;
        agent.stoppingDistance = 0.1f;

        HitFeedback feedback = instance.GetComponent<HitFeedback>();
        if (feedback == null)
        {
            feedback = instance.AddComponent<HitFeedback>();
        }

        Renderer renderer = instance.GetComponentInChildren<Renderer>();
        ConfigureHitFeedback(feedback, renderer, flashMaterial);

        EnemyStatus status = instance.GetComponent<EnemyStatus>();
        if (status == null)
        {
            status = instance.AddComponent<EnemyStatus>();
        }

        GameObject hpFill = CreateHealthBar(instance.transform);
        ConfigureEnemyStatus(status, maxHealth, xpReward, animator, feedback, hpFill, collider, agent);

        Component enemy = instance.GetComponent(enemyType);
        if (enemy == null)
        {
            enemy = instance.AddComponent(enemyType);
        }

        ConfigureEnemyBase(enemy, detectionRadius, attackRange, attackCooldown, agent, animator);
        Transform attackPoint = CreatePoint(instance.transform, "AttackPoint", new Vector3(0f, collider.height * 0.4f, collider.radius));

        if (enemy is EnemyMelee)
        {
            SetSerializedObject(enemy, "meleeDamage", 15f);
            SetSerializedObject(enemy, "attackPoint", attackPoint);
            SetSerializedObject(enemy, "attackRadius", attackRadius);
        }
        else if (enemy is EnemyHeavy)
        {
            SetSerializedObject(enemy, "heavyDamage", 35f);
            SetSerializedObject(enemy, "attackPoint", attackPoint);
            SetSerializedObject(enemy, "slamRadius", attackRadius);
            SetSerializedObject(enemy, "attackWindup", 0.6f);
        }

        string path = $"{EnemyFolder}/{prefabName}.prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static GameObject CreateProjectilePrefab(Material projectileMaterial)
    {
        GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectile.name = "EnemyProjectilePrefab";
        projectile.layer = EnemyAttackLayer;
        projectile.transform.localScale = Vector3.one * 0.35f;

        Collider collider = projectile.GetComponent<Collider>();
        collider.isTrigger = true;
        Rigidbody body = projectile.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        projectile.GetComponent<Renderer>().sharedMaterial = projectileMaterial;

        EnemyProjectile enemyProjectile = projectile.AddComponent<EnemyProjectile>();
        SetSerializedObject(enemyProjectile, "speed", 10f);
        SetSerializedObject(enemyProjectile, "damage", 10f);
        SetSerializedObject(enemyProjectile, "lifeTime", 5f);
        SetSerializedObject(enemyProjectile, "collisionLayers", (LayerMask)((1 << PlayerLayer) | (1 << EnvironmentLayer)));

        string path = $"{EnemyFolder}/EnemyProjectilePrefab.prefab";
        PrefabUtility.SaveAsPrefabAsset(projectile, path);
        Object.DestroyImmediate(projectile);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void ConfigureRangedPrefab(GameObject prefab, GameObject projectilePrefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        EnemyRanged ranged = contents.GetComponent<EnemyRanged>();
        CapsuleCollider collider = contents.GetComponent<CapsuleCollider>();
        Transform firePoint = CreatePoint(contents.transform, "FirePoint", new Vector3(0f, collider.height * 0.55f, collider.radius));
        SetSerializedObject(ranged, "projectilePrefab", projectilePrefab);
        SetSerializedObject(ranged, "firePoint", firePoint);
        SetSerializedObject(ranged, "preferredDistance", 6f);
        SetSerializedObject(ranged, "positionTolerance", 0.5f);
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void ConfigureHeavyPrefab(GameObject prefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        contents.transform.localScale = Vector3.one * 1.35f;
        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    private static void PopulateSampleScene(GameObject meleePrefab, GameObject rangedPrefab, GameObject heavyPrefab)
    {
        string scenePath = GameSetupBuilder.ResolveGameScene();
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        RemoveExistingSceneEnemy("EnemyMeleeInstance");
        RemoveExistingSceneEnemy("EnemyRangedInstance");
        RemoveExistingSceneEnemy("EnemyHeavyInstance");

        GameObject player = GameObject.FindWithTag("Player");
        Vector3 playerPosition = player != null ? player.transform.position : Vector3.zero;
        GameObject navigation = GameObject.Find("Navigation");
        if (navigation == null)
        {
            navigation = new GameObject("Navigation");
        }

        NavMeshSurface surface = navigation.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = navigation.AddComponent<NavMeshSurface>();
        }

        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~((1 << PlayerLayer) | (1 << EnemyLayer) | (1 << PlayerAttackLayer) | (1 << EnemyAttackLayer));
        BuildDungeonNavMesh(surface);
        InstantiateSceneEnemy(meleePrefab, "EnemyMeleeInstance", playerPosition + new Vector3(5f, 0f, 0f));
        InstantiateSceneEnemy(rangedPrefab, "EnemyRangedInstance", playerPosition + new Vector3(-6f, 0f, 3f));
        InstantiateSceneEnemy(heavyPrefab, "EnemyHeavyInstance", playerPosition + new Vector3(0f, 0f, 7f));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void InstantiateSceneEnemy(GameObject prefab, string instanceName, Vector3 position)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        instance.name = instanceName;
        instance.transform.position = position;
        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent != null && NavMesh.SamplePosition(position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }
    }

    private static void BuildDungeonNavMesh(NavMeshSurface surface)
    {
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(
            bounds,
            surface.layerMask,
            NavMeshCollectGeometry.RenderMeshes,
            surface.defaultArea,
            new List<NavMeshBuildMarkup>(),
            sources);

        GameObject player = GameObject.FindWithTag("Player");
        Bounds dungeonBounds = CalculateDungeonBounds();
        GameObject navigationGround = GameObject.Find("NavigationGround");
        if (navigationGround == null)
        {
            navigationGround = new GameObject("NavigationGround");
        }

        navigationGround.layer = EnvironmentLayer;
        navigationGround.transform.position = new Vector3(
            dungeonBounds.center.x,
            player != null ? player.transform.position.y - 0.05f : dungeonBounds.min.y,
            dungeonBounds.center.z);
        Vector3 navigationGroundSize = new Vector3(dungeonBounds.size.x, 0.1f, dungeonBounds.size.z);
        sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = navigationGroundSize,
            transform = navigationGround.transform.localToWorldMatrix,
            area = surface.defaultArea
        });

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(surface.agentTypeID);
        settings.agentRadius = 0.3f;
        settings.minRegionArea = 0f;
        NavMeshData generatedData = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        const string navMeshDataPath = "Assets/Scenes/SampleSceneNavMesh.asset";
        NavMeshData navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(navMeshDataPath);
        if (navMeshData == null)
        {
            navMeshData = generatedData;
            AssetDatabase.CreateAsset(navMeshData, navMeshDataPath);
        }
        else
        {
            EditorUtility.CopySerialized(generatedData, navMeshData);
            Object.DestroyImmediate(generatedData);
        }

        surface.RemoveData();
        surface.navMeshData = navMeshData;
        surface.AddData();
        EditorUtility.SetDirty(surface);
    }

    private static Bounds CalculateDungeonBounds()
    {
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
        Bounds bounds = renderers[0].bounds;
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer.gameObject.layer != PlayerLayer && renderer.gameObject.layer != EnemyLayer)
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private static void RemoveExistingSceneEnemy(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }
    }

    private static AnimatorController CreateOrLoadController()
    {
        string path = $"{ControllerFolder}/Enemy_AC.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller != null)
        {
            return controller;
        }

        controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("isMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("takeDamage", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("die", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimationClip idle = CreatePlaceholderClip("EnemyIdle");
        AnimationClip move = CreatePlaceholderClip("EnemyMove");
        AnimationClip attack = CreatePlaceholderClip("EnemyAttack");
        AnimationClip damage = CreatePlaceholderClip("EnemyDamage");
        AnimationClip die = CreatePlaceholderClip("EnemyDie");
        AnimatorState idleState = stateMachine.AddState("Idle");
        AnimatorState moveState = stateMachine.AddState("Move");
        AnimatorState attackState = stateMachine.AddState("Attack");
        AnimatorState damageState = stateMachine.AddState("Damage");
        AnimatorState dieState = stateMachine.AddState("Die");
        idleState.motion = idle;
        moveState.motion = move;
        attackState.motion = attack;
        damageState.motion = damage;
        dieState.motion = die;
        stateMachine.defaultState = idleState;

        AnimatorStateTransition idleToMove = idleState.AddTransition(moveState);
        idleToMove.hasExitTime = false;
        idleToMove.AddCondition(AnimatorConditionMode.If, 0f, "isMoving");
        AnimatorStateTransition moveToIdle = moveState.AddTransition(idleState);
        moveToIdle.hasExitTime = false;
        moveToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isMoving");
        AddTriggerTransition(stateMachine, attackState, "attack", true);
        AddTriggerTransition(stateMachine, damageState, "takeDamage", true);
        AddTriggerTransition(stateMachine, dieState, "die", false);
        AddExitTransition(attackState, idleState);
        AddExitTransition(damageState, idleState);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimatorOverrideController CreateOverrideController(string prefabName, AnimatorController controller, string modelName)
    {
        string path = $"{ControllerFolder}/{prefabName}_AC.overrideController";
        AnimatorOverrideController overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
        if (overrideController == null)
        {
            overrideController = new AnimatorOverrideController(controller);
            AssetDatabase.CreateAsset(overrideController, path);
        }

        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(overrides);
        Dictionary<string, AnimationClip> clips = LoadModelClips(modelName);
        for (int index = 0; index < overrides.Count; index++)
        {
            string clipName = overrides[index].Key.name;
            if (clipName == "EnemyIdle") overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[index].Key, clips["idle"]);
            if (clipName == "EnemyMove") overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[index].Key, clips["move"]);
            if (clipName == "EnemyAttack") overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[index].Key, clips["attack"]);
            if (clipName == "EnemyDamage") overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[index].Key, clips["damage"]);
            if (clipName == "EnemyDie") overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[index].Key, clips["die"]);
        }

        overrideController.ApplyOverrides(overrides);
        EditorUtility.SetDirty(overrideController);
        return overrideController;
    }

    private static Dictionary<string, AnimationClip> LoadModelClips(string modelName)
    {
        string modelPath = $"Assets/Level 1 Monster Pack/Models/{modelName}.fbx";
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
        Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        foreach (Object asset in assets)
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                if (clip.name.EndsWith("_idle")) clips["idle"] = clip;
                if (clip.name.EndsWith("_move")) clips["move"] = clip;
                if (clip.name.EndsWith("_attack")) clips["attack"] = clip;
                if (clip.name.EndsWith("_damage")) clips["damage"] = clip;
                if (clip.name.EndsWith("_die")) clips["die"] = clip;
            }
        }

        if (!clips.ContainsKey("damage")) clips["damage"] = clips["attack"];
        return clips;
    }

    private static void AddTriggerTransition(AnimatorStateMachine stateMachine, AnimatorState target, string parameter, bool canTransitionToSelf)
    {
        AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(target);
        transition.hasExitTime = false;
        transition.canTransitionToSelf = canTransitionToSelf;
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    private static void AddExitTransition(AnimatorState source, AnimatorState target)
    {
        AnimatorStateTransition transition = source.AddTransition(target);
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = 0.05f;
    }

    private static AnimationClip CreatePlaceholderClip(string name)
    {
        string path = $"{ControllerFolder}/{name}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip != null)
        {
            return clip;
        }

        clip = new AnimationClip { name = name };
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static void ConfigureEnemyBase(Component enemy, float detectionRadius, float attackRange, float attackCooldown, NavMeshAgent agent, Animator animator)
    {
        SetSerializedObject(enemy, "detectionRadius", detectionRadius);
        SetSerializedObject(enemy, "attackRange", attackRange);
        SetSerializedObject(enemy, "attackCooldown", attackCooldown);
        SetSerializedObject(enemy, "agent", agent);
        SetSerializedObject(enemy, "animator", animator);
        SetSerializedObject(enemy, "playerLayer", (LayerMask)(1 << PlayerLayer));
    }

    private static void ConfigureEnemyStatus(EnemyStatus status, float maxHealth, float xpReward, Animator animator, HitFeedback feedback, GameObject hpFill, Collider collider, NavMeshAgent agent)
    {
        SetSerializedObject(status, "maxHealth", maxHealth);
        SetSerializedObject(status, "xpReward", xpReward);
        SetSerializedObject(status, "animator", animator);
        SetSerializedObject(status, "hitFeedback", feedback);
        SetSerializedObject(status, "hpBarFill", hpFill);
        SetSerializedObject(status, "mainCollider", collider);
        SetSerializedObject(status, "agent", agent);
    }

    private static GameObject CreateHealthBar(Transform parent)
    {
        Transform existing = parent.Find("UI_WorldCanvas");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject canvasObject = new GameObject("UI_WorldCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Billboard));
        canvasObject.transform.SetParent(parent, false);
        canvasObject.transform.localPosition = new Vector3(0f, 2.25f, 0f);
        canvasObject.transform.localScale = Vector3.one * 0.008f;
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(220f, 30f);

        GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(canvasObject.transform, false);
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image backgroundImage = background.GetComponent<Image>();
        backgroundImage.color = new Color(0f, 0f, 0f, 0.75f);
        backgroundImage.raycastTarget = false;

        GameObject fill = new GameObject("HpBarFill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(background.transform, false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        Image fillImage = fill.GetComponent<Image>();
        fillImage.color = new Color(0.85f, 0.08f, 0.08f, 1f);
        fillImage.raycastTarget = false;
        return fill;
    }

    private static Transform CreatePoint(Transform parent, string pointName, Vector3 localPosition)
    {
        Transform point = parent.Find(pointName);
        if (point == null)
        {
            point = new GameObject(pointName).transform;
            point.SetParent(parent, false);
        }

        point.localPosition = localPosition;
        point.localRotation = Quaternion.identity;
        return point;
    }

    private static void ConfigureHitFeedback(HitFeedback feedback, Renderer renderer, Material flashMaterial)
    {
        SetSerializedObject(feedback, "characterRenderer", renderer);
        SetSerializedObject(feedback, "originalMaterial", renderer.sharedMaterial);
        SetSerializedObject(feedback, "flashMaterial", flashMaterial);
        SetSerializedObject(feedback, "flashDuration", 0.1f);
    }

    private static Material CreateOrLoadFlashMaterial()
    {
        return CreateOrLoadMaterial($"{EnemyFolder}/EnemyFlashURP.mat", Color.white, 0f);
    }

    private static Material CreateOrLoadProjectileMaterial()
    {
        return CreateOrLoadMaterial($"{EnemyFolder}/EnemyProjectileURP.mat", new Color(0.95f, 0.3f, 0.08f, 1f), 2f);
    }

    private static Material CreateOrLoadMaterial(string path, Color color, float emission)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new System.InvalidOperationException("Shader URP/Lit não encontrado.");
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = Shader.Find("Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", color);
        material.SetColor("_EmissionColor", color * emission);
        if (emission > 0f)
        {
            material.EnableKeyword("_EMISSION");
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConvertMonsterMaterialsToUrp()
    {
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Level 1 Monster Pack/Materials" });
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            throw new System.InvalidOperationException("Shader URP/Lit não encontrado.");
        }

        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Texture mainTexture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            if (mainTexture != null)
            {
                material.SetTexture("_BaseMap", mainTexture);
            }

            EditorUtility.SetDirty(material);
        }
    }

    private static void ConfigurePhysicsMatrix()
    {
        for (int layer = 0; layer < 32; layer++)
        {
            Physics.IgnoreLayerCollision(EnemyAttackLayer, layer, layer != PlayerLayer && layer != EnvironmentLayer);
        }

        Physics.IgnoreLayerCollision(EnemyLayer, EnemyAttackLayer, true);
        Physics.IgnoreLayerCollision(EnemyLayer, EnvironmentLayer, false);
        Physics.IgnoreLayerCollision(EnemyLayer, PlayerLayer, false);
        Physics.IgnoreLayerCollision(EnemyLayer, PlayerAttackLayer, false);
    }

    private static Bounds CalculateBounds(GameObject gameObject)
    {
        Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static void SetLayerRecursively(GameObject gameObject, int layer)
    {
        gameObject.layer = layer;
        foreach (Transform child in gameObject.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static void SetSerializedObject(Object target, string propertyName, Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        serializedObject.FindProperty(propertyName).objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedObject(Object target, string propertyName, float value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        serializedObject.FindProperty(propertyName).floatValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedObject(Object target, string propertyName, LayerMask value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        serializedObject.FindProperty(propertyName).intValue = value.value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string folderName = System.IO.Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
