using System.Collections.Generic;
using System.IO;
using System.Text;
using Subterraneorum.DungeonSystem;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class GameSetupBuilder
{
    public const string GameScenePath = "Assets/Scenes/Game.unity";
    public const string OldScenePath = "Assets/Scenes/SampleScene.unity";
    public const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

    public static string ResolveGameScene() => File.Exists(GameScenePath) ? GameScenePath : OldScenePath;

    private const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string ResourcesFolder = "Assets/Resources/Subterraneorum";
    private const string AbilityFolder = "Assets/Data/Abilities";
    private const string CardFolder = "Assets/Data/Cards";
    private const string ClassFolder = "Assets/Data/Classes";
    private const string PlayerFolder = "Assets/prefabs/Players";
    private const string EnemyFolder = "Assets/prefabs/Enemies";
    private const string BossControllerPath = "Assets/Animations/Boss/Boss_AC.controller";
    private const string BossPrefabPath = EnemyFolder + "/BossPrefab.prefab";
    private const string BasePrefabPath = "Assets/prefabs/PlayerPrefab.prefab";
    private const string TileFolder = "Assets/ui-pack/Tiles/Large tiles/Thick outline";
    private const string Monster38Folder = "Assets/Stylized3DMonster/Monster38";
    private const int EnemyLayer = 10;

    [MenuItem("Subterraneorum/Configurar Fases 2 a 7")]
    public static void Build()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new StringBuilder("[GameSetupBuilder]\n");
        foreach (string folder in new[] { "Assets/Resources", ResourcesFolder, "Assets/Data", AbilityFolder, CardFolder, "Assets/Animations", "Assets/Animations/Boss" })
        {
            EnsureFolder(folder);
        }

        ConfigureInputActions(log);
        CreateRuntimeMaterials(log);
        UiTheme theme = CreateUiTheme(log);

        FixCharacterMaterials(log);
        FixRuinsMaterials(log);
        Dictionary<string, AbilityDefinition[]> abilities = CreateAbilities();
        (GameObject arrow, GameObject bolt) = CreateProjectiles();
        ConfigureClasses(abilities, arrow, bolt, log);
        AddPlayerComponents(log);

        GameObject boss = CreateBossPrefab(log);

        CardDefinition[] cards = CreateCards();
        log.AppendLine($"  - {cards.Length} cartas em {CardFolder}");

        FixEnemyScale(log);
        TuneEnemyPrefabs(log);
        ConfigurePerformance(log);

        RenameGameScene(log);
        ConfigureGameScene(boss, cards, log);
        CreateMainMenuScene(log);
        ConfigureBuildSettings(log);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(log.ToString());
    }

    private static void ConfigureInputActions(StringBuilder log)
    {
        InputActionAsset asset = InputActionAsset.FromJson(File.ReadAllText(ActionsPath));
        InputActionMap map = asset != null ? asset.FindActionMap("Player") : null;
        if (map == null) throw new System.InvalidOperationException("Mapa 'Player' não encontrado em " + ActionsPath);

        if (EditorBuildSettings.TryGetConfigObject("com.unity.input.settings.actions", out Object _))
        {
            EditorBuildSettings.RemoveConfigObject("com.unity.input.settings.actions");
            log.AppendLine("  - Input: InputSystem_Actions deixou de ser 'project-wide' (cada jogador usa o seu)");
        }

        bool changed = false;
        changed |= AddAction(map, "Ability1", ("<Keyboard>/k", "Keyboard&Mouse"), ("<Mouse>/rightButton", "Keyboard&Mouse"), ("<Gamepad>/buttonEast", "Gamepad"), ("<Gamepad>/rightTrigger", "Gamepad"));
        changed |= AddAction(map, "Ability2", ("<Keyboard>/l", "Keyboard&Mouse"), ("<Keyboard>/q", "Keyboard&Mouse"), ("<Gamepad>/leftShoulder", "Gamepad"), ("<Gamepad>/leftTrigger", "Gamepad"));
        changed |= AddAction(map, "Skills", ("<Keyboard>/tab", "Keyboard&Mouse"), ("<Gamepad>/select", "Gamepad"));
        if (!changed) return;

        File.WriteAllText(ActionsPath, asset.ToJson());
        Object.DestroyImmediate(asset);
        AssetDatabase.ImportAsset(ActionsPath, ImportAssetOptions.ForceUpdate);
        log.AppendLine("  - Input: ações Ability1 (K/RMB/B/RT), Ability2 (L/Q/LB/LT), Skills (Tab/Select)");
    }

    private static bool AddAction(InputActionMap map, string name, params (string path, string group)[] bindings)
    {
        if (map.FindAction(name) != null) return false;
        InputAction action = map.AddAction(name, InputActionType.Button);
        foreach ((string path, string group) in bindings)
        {
            action.AddBinding(path, groups: group);
        }

        return true;
    }

    private static void CreateRuntimeMaterials(StringBuilder log)
    {
        Material telegraph = LoadOrCreateMaterial($"{ResourcesFolder}/TelegraphMaterial.mat", "Universal Render Pipeline/Unlit");
        telegraph.SetFloat("_Surface", 1f);
        telegraph.SetFloat("_Blend", 0f);
        telegraph.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        telegraph.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        telegraph.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        telegraph.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        telegraph.SetFloat("_ZWrite", 0f);
        telegraph.SetFloat("_Cull", 0f);
        telegraph.SetOverrideTag("RenderType", "Transparent");
        telegraph.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        telegraph.renderQueue = (int)RenderQueue.Transparent;
        telegraph.SetColor("_BaseColor", Color.white);
        telegraph.enableInstancing = true;
        EditorUtility.SetDirty(telegraph);

        Material glow = LoadOrCreateMaterial($"{ResourcesFolder}/GlowMaterial.mat", "Universal Render Pipeline/Lit");
        glow.SetColor("_BaseColor", Color.white);
        glow.SetColor("_EmissionColor", Color.white * 2f);
        glow.EnableKeyword("_EMISSION");
        glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        glow.enableInstancing = true;
        EditorUtility.SetDirty(glow);
        log.AppendLine($"  - Materiais de runtime em {ResourcesFolder}");
    }

    private static UiTheme CreateUiTheme(StringBuilder log)
    {
        string path = $"{ResourcesFolder}/UiTheme.asset";
        UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>(path);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            AssetDatabase.CreateAsset(theme, path);
        }

        theme.panelLight = ImportTile(0);
        theme.panelDark = ImportTile(3);
        theme.panelRed = ImportTile(20);

        const string atlasPath = "Assets/playing-cards/textures/card_uv.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
        if (importer != null && (importer.filterMode != FilterMode.Point || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed))
        {
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        theme.cardAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        EditorUtility.SetDirty(theme);
        log.AppendLine("  - Tema de UI (ui-pack tiles 0/3/20/21/22 + atlas de cartas)");
        return theme;
    }

    private static Sprite ImportTile(int index)
    {
        string path = $"{TileFolder}/tile_{index:D4}.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new System.InvalidOperationException("Tile do ui-pack não encontrado: " + path);

        var border = new Vector4(10f, 10f, 10f, 10f);
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border || importer.filterMode != FilterMode.Point)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void FixCharacterMaterials(StringBuilder log)
    {
        const string textures = "Assets/RPG Characters/Textures";
        const string folder = "Assets/Materials/Characters";
        EnsureFolder("Assets/Materials");
        EnsureFolder(folder);

        var weapons = new Dictionary<string, string>
        {
            ["Warrior"] = "Warrior_Sword",
            ["Ranger"] = "Ranger_Bow",
            ["Wizard"] = "Wizard_Staff",
            ["Rogue"] = "Rogue_Dagger",
        };

        foreach (KeyValuePair<string, string> pair in weapons)
        {
            Material body = TexturedMaterial($"{folder}/{pair.Key}_Body.mat", $"{textures}/{pair.Key}_Texture.png");
            Material weapon = TexturedMaterial($"{folder}/{pair.Value}.mat", $"{textures}/{pair.Value}_Texture.png");
            string remapped = RemapMaterials($"Assets/RPG Characters/FBX/{pair.Key}.fbx", name => IsWeaponMaterial(name) ? weapon : body);
            string weaponOnly = RemapMaterials($"Assets/RPG Characters/FBX/Only Weapons/{pair.Value}.fbx", _ => weapon);
            log.AppendLine($"  - Texturas: {pair.Key} {remapped}{weaponOnly}");
        }

        Material bow = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/Ranger_Bow.mat");
        RemapMaterials("Assets/RPG Characters/FBX/Only Weapons/Ranger_Arrow.fbx", _ => bow);
    }

    private static void FixRuinsMaterials(StringBuilder log)
    {
        const string folder = "Assets/Materials/Environment";
        EnsureFolder("Assets/Materials");
        EnsureFolder(folder);
        Material green = ColorMaterial($"{folder}/Ruins_Green.mat", new Color(0.32f, 0.47f, 0.22f));
        Material leaves = ColorMaterial($"{folder}/Ruins_Leaves.mat", new Color(0.27f, 0.42f, 0.2f));

        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/prefabs/UltimateModularRuins" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string result = RemapMaterials(path, name => name == "Green" ? green : name == "Leaf_Texture" ? leaves : null);
            if (result.Length > 0) count++;
        }

        log.AppendLine($"  - Ruínas: vegetação branca virou verde em {count} modelos");
    }

    private static Material ColorMaterial(string path, Color color)
    {
        Material material = LoadOrCreateMaterial(path, "Universal Render Pipeline/Lit");
        material.SetTexture("_BaseMap", null);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.1f);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static bool IsWeaponMaterial(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("sword") || n.Contains("bow") || n.Contains("staff") || n.Contains("dagger") || n.Contains("arrow") || n.Contains("weapon");
    }

    private static Material TexturedMaterial(string path, string texturePath)
    {
        Material material = LoadOrCreateMaterial(path, "Universal Render Pipeline/Lit");
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null) Debug.LogWarning($"[GameSetupBuilder] Textura não encontrada: {texturePath}");
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.15f);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static string RemapMaterials(string modelPath, System.Func<string, Material> pick)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) return "";

        var summary = new List<string>();
        bool changed = false;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (!(asset is Material embedded) || !AssetDatabase.IsSubAsset(embedded)) continue;
            Material target = pick(embedded.name);
            if (target == null) continue;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), target);
            summary.Add($"{embedded.name}→{target.name}");
            changed = true;
        }

        if (changed) importer.SaveAndReimport();
        return summary.Count > 0 ? $"[{string.Join(", ", summary)}] " : "";
    }

    private static Dictionary<string, AbilityDefinition[]> CreateAbilities()
    {
        return new Dictionary<string, AbilityDefinition[]>
        {
            ["Warrior"] = new[]
            {
                Ability("Warrior_Escudo", AbilityKind.ShieldBlock, "Escudo", "Ergue o escudo: −70% de dano por 2,5 s (anda mais devagar).", cooldown: 8f, duration: 2.5f, amount: 0.7f),
                Ability("Warrior_GolpePesado", AbilityKind.HeavySlam, "Golpe Pesado", "Golpe em área à frente que causa muito dano.", cooldown: 6f, damage: 70f, radius: 2.6f, range: 1.8f),
            },
            ["Ranger"] = new[]
            {
                Ability("Ranger_Rajada", AbilityKind.ArrowVolley, "Rajada de Flechas", "Dispara 5 flechas em leque.", cooldown: 5f, damage: 20f, range: 14f, count: 5),
                Ability("Ranger_Disparada", AbilityKind.AgilityBurst, "Disparada", "Recarrega o dash e corre 50% mais rápido por 3 s.", cooldown: 9f, duration: 3f, amount: 1.5f),
            },
            ["Wizard"] = new[]
            {
                Ability("Wizard_Explosao", AbilityKind.ArcaneBlast, "Explosão Arcana", "Explosão em área à frente, ótima contra hordas.", cooldown: 6f, damage: 55f, radius: 3.5f, range: 5f),
                Ability("Wizard_Cura", AbilityKind.HealingAura, "Aura de Cura", "Cura 35 de vida de todos os aliados por perto.", cooldown: 12f, radius: 7f, amount: 35f),
            },
            ["Rogue"] = new[]
            {
                Ability("Rogue_Laminas", AbilityKind.BladeFlurry, "Dança das Lâminas", "4 golpes rápidos à frente, com roubo de vida.", cooldown: 5f, damage: 16f, radius: 1.6f, range: 1.2f, count: 4),
                Ability("Rogue_PassoSombrio", AbilityKind.ShadowStep, "Passo Sombrio", "Avança 6 m ferindo quem estiver no caminho.", cooldown: 7f, damage: 30f, radius: 1.2f, range: 6f),
            },
        };
    }

    private static AbilityDefinition Ability(string file, AbilityKind kind, string name, string description, float cooldown,
        float damage = 0f, float radius = 2f, float range = 3f, float duration = 2f, int count = 1, float amount = 0.5f)
    {
        string path = $"{AbilityFolder}/{file}.asset";
        AbilityDefinition ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(path);
        if (ability == null)
        {
            ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            AssetDatabase.CreateAsset(ability, path);
        }

        ability.kind = kind;
        ability.displayName = name;
        ability.description = description;
        ability.cooldown = cooldown;
        ability.damage = damage;
        ability.radius = radius;
        ability.range = range;
        ability.duration = duration;
        ability.count = count;
        ability.amount = amount;
        EditorUtility.SetDirty(ability);
        return ability;
    }

    private static (GameObject arrow, GameObject bolt) CreateProjectiles()
    {
        GameObject arrowModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Characters/FBX/Only Weapons/Ranger_Arrow.fbx");
        var arrowRoot = new GameObject("PlayerArrow");
        if (arrowModel != null)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(arrowModel, arrowRoot.transform);
            model.name = "Model";
            Bounds b = RendererBounds(model);
            Vector3 size = b.size;
            float longest = Mathf.Max(size.x, size.y, size.z);
            if (Mathf.Approximately(longest, size.x)) model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.transform.localRotation;
            else if (Mathf.Approximately(longest, size.y)) model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * model.transform.localRotation;
            float scale = longest > 0.0001f ? 0.9f / longest : 1f;
            model.transform.localScale *= scale;
            b = RendererBounds(model);
            model.transform.localPosition -= b.center - arrowRoot.transform.position;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        arrowRoot.AddComponent<PlayerProjectile>();
        GameObject arrow = SavePrefab(arrowRoot, $"{PlayerFolder}/PlayerArrow.prefab");

        GameObject boltGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        boltGo.name = "PlayerMagicBolt";
        Object.DestroyImmediate(boltGo.GetComponent<Collider>());
        boltGo.transform.localScale = Vector3.one * 0.4f;
        Material boltMaterial = LoadOrCreateMaterial($"{PlayerFolder}/MagicBoltURP.mat", "Universal Render Pipeline/Lit");
        boltMaterial.SetColor("_BaseColor", new Color(0.45f, 0.6f, 1f));
        boltMaterial.SetColor("_EmissionColor", new Color(0.45f, 0.6f, 1f) * 3f);
        boltMaterial.EnableKeyword("_EMISSION");
        boltMaterial.enableInstancing = true;
        EditorUtility.SetDirty(boltMaterial);
        MeshRenderer boltRenderer = boltGo.GetComponent<MeshRenderer>();
        boltRenderer.sharedMaterial = boltMaterial;
        boltRenderer.shadowCastingMode = ShadowCastingMode.Off;
        boltGo.AddComponent<PlayerProjectile>();
        GameObject bolt = SavePrefab(boltGo, $"{PlayerFolder}/PlayerMagicBolt.prefab");
        return (arrow, bolt);
    }

    private static void ConfigureClasses(Dictionary<string, AbilityDefinition[]> abilities, GameObject arrow, GameObject bolt, StringBuilder log)
    {
        SetClass("Warrior", 150f, 5.5f, 0.9f, AttackStyle.Melee, 30f, 0.6f, 1.6f, 0f, 0f, null, 0f, abilities);
        SetClass("Ranger", 90f, 7f, 0.5f, AttackStyle.Projectile, 18f, 0.35f, 1f, 22f, 14f, arrow, 0f, abilities);
        SetClass("Wizard", 85f, 5.8f, 0.9f, AttackStyle.Projectile, 22f, 0.55f, 1f, 15f, 11f, bolt, 0f, abilities);
        SetClass("Rogue", 100f, 6.8f, 0.6f, AttackStyle.Melee, 16f, 0.28f, 1.25f, 0f, 0f, null, 0.2f, abilities);
        log.AppendLine("  - Classes: atributos, ataque básico (Arqueira/Mago à distância) e 2 habilidades cada");
    }

    private static void SetClass(string id, float hp, float speed, float dashCd, AttackStyle style, float damage, float cooldown, float radius,
        float projectileSpeed, float projectileRange, GameObject projectile, float lifeSteal, Dictionary<string, AbilityDefinition[]> abilities)
    {
        string path = $"{ClassFolder}/{id}.asset";
        ClassDefinition def = AssetDatabase.LoadAssetAtPath<ClassDefinition>(path);
        if (def == null) throw new System.InvalidOperationException($"{path} não existe. Rode antes 'Configurar Classes e Multiplayer Local'.");

        def.maxHealth = hp;
        def.moveSpeed = speed;
        def.dashCooldown = dashCd;
        def.attackStyle = style;
        def.attackDamage = damage;
        def.attackCooldown = cooldown;
        def.attackRadius = radius;
        def.projectileSpeed = projectileSpeed > 0f ? projectileSpeed : def.projectileSpeed;
        def.projectileRange = projectileRange > 0f ? projectileRange : def.projectileRange;
        def.projectilePrefab = projectile;
        def.lifeSteal = lifeSteal;
        def.ability1 = abilities[id][0];
        def.ability2 = abilities[id][1];
        EditorUtility.SetDirty(def);
    }

    private static void AddPlayerComponents(StringBuilder log)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(BasePrefabPath);
        try
        {
            bool changed = false;
            changed |= Ensure<PlayerAbilities>(contents);
            changed |= Ensure<PlayerSkills>(contents);
            changed |= Ensure<PlayerReviver>(contents);
            changed |= Ensure<PlayerBuffs>(contents);

            foreach (RectTransform rect in contents.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name != "HP_Fill") continue;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                RectTransform background = rect.parent as RectTransform;
                float width = background != null ? background.sizeDelta.x - 4f : 136f;
                float height = background != null ? background.sizeDelta.y - 4f : 14f;
                if (rect.anchoredPosition != new Vector2(2f, 0f) || rect.sizeDelta != new Vector2(width, height))
                {
                    rect.anchoredPosition = new Vector2(2f, 0f);
                    rect.sizeDelta = new Vector2(width, height);
                    changed = true;
                }
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(contents, BasePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        log.AppendLine("  - PlayerPrefab: PlayerAbilities, PlayerSkills, PlayerReviver, PlayerBuffs");
    }

    private static bool Ensure<T>(GameObject go) where T : Component
    {
        if (go.GetComponent<T>() != null) return false;
        go.AddComponent<T>();
        return true;
    }

    private static GameObject CreateBossPrefab(StringBuilder log)
    {
        AnimatorController controller = CreateBossController();
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Monster38Folder}/Prefab/Monster38_01.prefab");
        if (source == null) throw new System.InvalidOperationException("Monster38_01.prefab não encontrado.");

        var root = new GameObject("BossPrefab") { tag = "Enemy", layer = EnemyLayer };
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        SetLayerRecursively(model, EnemyLayer);

        EnsureFolder("Assets/Materials");
        EnsureFolder("Assets/Materials/Characters");
        Material bossMaterial = TexturedMaterial("Assets/Materials/Characters/Boss_Monster38.mat", $"{Monster38Folder}/ShaderTexture/Monster38_01.png");
        if (bossMaterial.GetTexture("_BaseMap") == null)
        {
            Material original = AssetDatabase.LoadAssetAtPath<Material>($"{Monster38Folder}/ShaderTexture/Monster38_01.mat");
            Texture fromOriginal = original != null && original.HasProperty("_MainTex") ? original.GetTexture("_MainTex") : null;
            if (fromOriginal != null)
            {
                bossMaterial.SetTexture("_BaseMap", fromOriginal);
                EditorUtility.SetDirty(bossMaterial);
            }

            log.AppendLine($"  - Chefão: textura via material original = {(fromOriginal != null ? fromOriginal.name : "NÃO ENCONTRADA")}");
        }

        SkinnedMeshRenderer bossSkin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector2[] uvs = bossSkin != null && bossSkin.sharedMesh != null ? bossSkin.sharedMesh.uv : new Vector2[0];
        var uvBounds = new Bounds();
        for (int i = 0; i < uvs.Length; i++)
        {
            if (i == 0) uvBounds = new Bounds(uvs[0], Vector3.zero);
            else uvBounds.Encapsulate(uvs[i]);
        }

        bool usableUv = uvs.Length > 0 && uvBounds.size.x > 0.05f && uvBounds.size.y > 0.05f;
        log.AppendLine($"  - Chefão: {uvs.Length} UVs, faixa {uvBounds.size} → {(usableUv ? "textura" : "cor sólida")}");
        if (!usableUv)
        {
            bossMaterial.SetTexture("_BaseMap", null);
            bossMaterial.SetColor("_BaseColor", new Color(0.72f, 0.16f, 0.12f));
            EditorUtility.SetDirty(bossMaterial);
        }
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            var materials = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = bossMaterial;
            r.sharedMaterials = materials;
        }

        Bounds b = RendererBounds(model);
        float scale = b.size.y > 0.01f ? 4.5f / b.size.y : 1f;
        model.transform.localScale *= scale;
        b = RendererBounds(model);
        model.transform.localPosition += new Vector3(0f, -b.min.y, 0f);
        b = RendererBounds(model);

        Animator animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.height = b.size.y;
        capsule.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.35f, 0.6f, 1.6f);
        capsule.center = new Vector3(0f, b.size.y * 0.5f, 0f);

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.speed = 3.2f;
        agent.acceleration = 10f;
        agent.angularSpeed = 360f;
        agent.radius = 0.3f;
        agent.height = 2f;
        agent.stoppingDistance = 0.5f;

        HitFeedback feedback = root.AddComponent<HitFeedback>();
        Set(feedback, "flashMaterial", AssetDatabase.LoadAssetAtPath<Material>($"{EnemyFolder}/EnemyFlashURP.mat"));

        EnemyStatus status = root.AddComponent<EnemyStatus>();
        Set(status, "maxHealth", 1200f);
        Set(status, "xpReward", 300f);
        Set(status, "animator", animator);
        Set(status, "hitFeedback", feedback);
        Set(status, "mainCollider", capsule);
        Set(status, "agent", agent);
        Set(status, "destroyDelay", 4f);

        BossController boss = root.AddComponent<BossController>();
        Set(boss, "detectionRadius", 30f);
        Set(boss, "loseTargetRadius", 60f);
        Set(boss, "attackRange", 13f);
        Set(boss, "attackCooldown", 2f);
        Set(boss, "agent", agent);
        Set(boss, "animator", animator);
        SetInt(boss, "playerLayer", 1 << 8);
        Set(boss, "projectilePrefab", AssetDatabase.LoadAssetAtPath<GameObject>($"{EnemyFolder}/EnemyProjectilePrefab.prefab"));

        GameObject prefab = SavePrefab(root, BossPrefabPath);
        log.AppendLine($"  - Chefão: {BossPrefabPath} (Monster38, {b.size.y:0.0} m, Animator {BossControllerPath})");
        return prefab;
    }

    private static AnimatorController CreateBossController()
    {
        AnimationClip Clip(string name, bool loop)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Monster38Folder}/Anim/Monster38_{name}.anim");
            if (clip == null) throw new System.InvalidOperationException($"Clip Monster38_{name}.anim não encontrado.");
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopTime != loop)
            {
                settings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
            }

            return clip;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(BossControllerPath) != null) AssetDatabase.DeleteAsset(BossControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(BossControllerPath);
        controller.AddParameter("isMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("attackIndex", AnimatorControllerParameterType.Int);
        controller.AddParameter("takeDamage", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("die", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idle = sm.AddState("Idle");
        idle.motion = Clip("Idle01", true);
        AnimatorState run = sm.AddState("Run");
        run.motion = Clip("Run", true);
        AnimatorState die = sm.AddState("Die");
        die.motion = Clip("Die01", false);
        sm.defaultState = idle;

        AnimatorStateTransition toRun = idle.AddTransition(run);
        toRun.hasExitTime = false;
        toRun.duration = 0.15f;
        toRun.AddCondition(AnimatorConditionMode.If, 0f, "isMoving");
        AnimatorStateTransition toIdle = run.AddTransition(idle);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.15f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "isMoving");

        string[] attacks = { "Attack01", "Attack02", "Attack03" };
        for (int i = 0; i < attacks.Length; i++)
        {
            AnimatorState attack = sm.AddState(attacks[i]);
            attack.motion = Clip(attacks[i], false);
            AnimatorStateTransition any = sm.AddAnyStateTransition(attack);
            any.hasExitTime = false;
            any.duration = 0.1f;
            any.canTransitionToSelf = false;
            any.AddCondition(AnimatorConditionMode.If, 0f, "attack");
            any.AddCondition(AnimatorConditionMode.Equals, i + 1, "attackIndex");
            AnimatorStateTransition back = attack.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = 0.95f;
            back.duration = 0.15f;
        }

        AnimatorStateTransition toDie = sm.AddAnyStateTransition(die);
        toDie.hasExitTime = false;
        toDie.duration = 0.1f;
        toDie.canTransitionToSelf = false;
        toDie.AddCondition(AnimatorConditionMode.If, 0f, "die");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static CardDefinition[] CreateCards()
    {
        return new[]
        {
            Card("LaminaAfiada", "Lâmina Afiada", RunEffect.PlayerDamage, 0.15f, "Jogadores: +15% de dano", RunEffect.EnemyDamage, 0.1f, "Inimigos: +10% de dano", new Vector2Int(12, 0)),
            Card("CoracaoValente", "Coração Valente", RunEffect.PlayerMaxHealth, 20f, "Jogadores: +20 de vida máxima", RunEffect.EnemyHealth, 0.1f, "Inimigos: +10% de vida", new Vector2Int(11, 1)),
            Card("PesLigeiros", "Pés Ligeiros", RunEffect.PlayerSpeed, 0.1f, "Jogadores: +10% de velocidade", RunEffect.EnemySpeed, 0.1f, "Inimigos: +10% de velocidade", new Vector2Int(9, 2)),
            Card("Erudicao", "Erudição", RunEffect.PlayerXPGain, 0.25f, "Jogadores: +25% de XP", RunEffect.EnemyDamage, 0.05f, "Inimigos: +5% de dano", new Vector2Int(10, 3)),
            Card("PeleDeFerro", "Pele de Ferro", RunEffect.PlayerDamageTaken, -0.15f, "Jogadores: −15% de dano recebido", RunEffect.EnemyHealth, 0.1f, "Inimigos: +10% de vida", new Vector2Int(11, 0)),
            Card("Maldicao", "Maldição", RunEffect.EnemyHealth, -0.15f, "Inimigos: −15% de vida", RunEffect.EnemyDamage, 0.1f, "Inimigos: +10% de dano", new Vector2Int(13, 0)),
            Card("Grilhoes", "Grilhões", RunEffect.EnemySpeed, -0.15f, "Inimigos: −15% de velocidade", RunEffect.EnemySpeed, 0.1f, "Inimigos: +10% de velocidade", new Vector2Int(10, 2)),
            Card("Desarme", "Desarme", RunEffect.EnemyDamage, -0.15f, "Inimigos: −15% de dano", RunEffect.EnemyHealth, 0.1f, "Inimigos: +10% de vida", new Vector2Int(9, 1)),
        };
    }

    private static CardDefinition Card(string file, string title, RunEffect effect, float value, string effectText, RunEffect leftover, float leftoverValue, string leftoverText, Vector2Int cell)
    {
        string path = $"{CardFolder}/{file}.asset";
        CardDefinition card = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
        if (card == null)
        {
            card = ScriptableObject.CreateInstance<CardDefinition>();
            AssetDatabase.CreateAsset(card, path);
        }

        card.title = title;
        card.effect = effect;
        card.value = value;
        card.effectText = effectText;
        card.leftoverEffect = leftover;
        card.leftoverValue = leftoverValue;
        card.leftoverText = leftoverText;
        card.atlasCell = cell;
        EditorUtility.SetDirty(card);
        return card;
    }

    private static void FixEnemyScale(StringBuilder log)
    {
        var targets = new (string prefab, string model, float size)[]
        {
            ($"{EnemyFolder}/EnemyMeleePrefab.prefab", "Assets/Level 1 Monster Pack/Models/Slime_Level_1.fbx", 0.7f),
            ($"{EnemyFolder}/EnemyRangedPrefab.prefab", "Assets/Level 1 Monster Pack/Models/Bat_Level_1.fbx", 1.0f),
            ($"{EnemyFolder}/EnemyHeavyPrefab.prefab", "Assets/Level 1 Monster Pack/Models/Ghost_Lv1.fbx", 1.4f),
        };

        foreach ((string prefabPath, string modelPath, float target) in targets)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (prefab == null || importer == null) continue;

            GameObject instance = Object.Instantiate(prefab);
            float size;
            try
            {
                instance.transform.localScale = Vector3.one;
                Bounds b = ModelBounds(instance);
                size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            if (size <= 0.0001f) continue;
            float factor = target / size;
            if (factor > 0.8f && factor < 1.25f)
            {
                log.AppendLine($"  - Inimigo {Path.GetFileNameWithoutExtension(modelPath)}: tamanho ok ({size:0.00} m)");
                continue;
            }

            float before = importer.globalScale;
            importer.globalScale = before * factor;
            importer.SaveAndReimport();
            log.AppendLine($"  - Inimigo {Path.GetFileNameWithoutExtension(modelPath)}: {size:0.000} m → {target:0.0} m (escala de import {before} → {importer.globalScale:0.##})");
        }
    }

    private static void TuneEnemyPrefabs(StringBuilder log)
    {
        foreach (string name in new[] { "EnemyMeleePrefab", "EnemyRangedPrefab", "EnemyHeavyPrefab" })
        {
            string path = $"{EnemyFolder}/{name}.prefab";
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            if (contents == null) continue;
            try
            {
                foreach (Animator animator in contents.GetComponentsInChildren<Animator>()) animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (SkinnedMeshRenderer skinned in contents.GetComponentsInChildren<SkinnedMeshRenderer>()) skinned.updateWhenOffscreen = true;

                Bounds model = ModelBounds(contents);
                float scaleY = Mathf.Max(0.01f, contents.transform.lossyScale.y);
                float scaleXZ = Mathf.Max(0.01f, contents.transform.lossyScale.x);
                float height = Mathf.Clamp((model.max.y - contents.transform.position.y) / scaleY, 0.8f, 3f);
                float radius = Mathf.Clamp(Mathf.Max(model.size.x, model.size.z) * 0.35f / scaleXZ, 0.35f, 1.1f);
                CapsuleCollider capsule = contents.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    capsule.direction = 1;
                    capsule.height = Mathf.Max(height, radius * 2f);
                    capsule.radius = radius;
                    capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
                }

                NavMeshAgent agent = contents.GetComponent<NavMeshAgent>();
                if (agent != null)
                {
                    agent.height = height;
                    agent.radius = Mathf.Clamp(radius * 0.9f, 0.3f, 0.9f);
                }

                Transform bar = contents.transform.Find("UI_WorldCanvas");
                if (bar != null)
                {
                    Bounds b = ModelBounds(contents);
                    float top = (b.max.y - contents.transform.position.y) / Mathf.Max(0.01f, contents.transform.lossyScale.y);
                    bar.localPosition = new Vector3(0f, Mathf.Clamp(top + 0.35f, 0.9f, 3f), 0f);
                    bar.localScale = Vector3.one * 0.0045f;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        log.AppendLine("  - Inimigos: Animator não atualiza fora da tela");
    }

    private static void ConfigurePerformance(StringBuilder log)
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipeline != null)
        {
            var so = new SerializedObject(pipeline);
            SetProp(so, "m_ShadowDistance", 65f);
            SetProp(so, "m_ShadowCascadeCount", 1);
            SetProp(so, "m_SoftShadowQuality", 1);
            SetProp(so, "m_AdditionalLightShadowsSupported", false);
            SetProp(so, "m_AdditionalLightsShadowmapResolution", 512);
            SetProp(so, "m_MainLightShadowmapResolution", 2048);
            SetProp(so, "m_MSAA", 1);
            SetProp(so, "m_UseSRPBatcher", true);
            SetProp(so, "m_RequireOpaqueTexture", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        if (renderer != null)
        {
            AddPlayerSilhouette(renderer, log);

            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature != null && feature.name.Contains("ScreenSpaceAmbientOcclusion") && feature.isActive)
                {
                    feature.SetActive(false);
                    EditorUtility.SetDirty(feature);
                }
            }

            EditorUtility.SetDirty(renderer);
        }

        var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        SerializedProperty levels = quality.FindProperty("m_QualitySettings");
        for (int i = 0; i < levels.arraySize; i++)
        {
            SerializedProperty level = levels.GetArrayElementAtIndex(i);
            SetRel(level, "vSyncCount", 1);
            SetRel(level, "shadowDistance", 65f);
        }

        quality.ApplyModifiedPropertiesWithoutUndo();

        var time = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
        SetProp(time, "Maximum Allowed Timestep", 0.1f);
        time.ApplyModifiedPropertiesWithoutUndo();

        log.AppendLine("  - Desempenho: sombras 1 cascata/35 m/suaves baixas, sem sombras de luzes extras, SSAO off, MSAA off, SRP Batcher, VSync on, passo máx. 0,1 s");
    }

    private static void AddPlayerSilhouette(UniversalRendererData data, StringBuilder log)
    {
        EnsureFolder("Assets/Materials");
        Material silhouette = LoadOrCreateMaterial("Assets/Materials/PlayerSilhouette.mat", "Universal Render Pipeline/Unlit");
        MakeTransparent(silhouette, new Color(0.35f, 0.75f, 1f, 0.45f));

        RenderObjects feature = null;
        foreach (ScriptableRendererFeature existing in data.rendererFeatures)
        {
            if (existing is RenderObjects ro && existing.name == "PlayerSilhouette") feature = ro;
        }

        bool created = feature == null;
        if (created)
        {
            feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = "PlayerSilhouette";
            AssetDatabase.AddObjectToAsset(feature, data);
        }

        feature.settings.passTag = "PlayerSilhouette";
        feature.settings.Event = RenderPassEvent.AfterRenderingOpaques;
        feature.settings.filterSettings.RenderQueueType = RenderQueueType.Opaque;
        feature.settings.filterSettings.LayerMask = 1 << 8;
        feature.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material;
        feature.settings.overrideMaterial = silhouette;
        feature.settings.overrideDepthState = true;
        feature.settings.depthCompareFunction = CompareFunction.Greater;
        feature.settings.enableWrite = false;
        feature.SetActive(true);
        EditorUtility.SetDirty(feature);

        if (created)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            var so = new SerializedObject(data);
            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(data);
        log.AppendLine("  - Jogadores aparecem como silhueta azul atrás de paredes e portas");
    }

    private static void MakeTransparent(Material material, Color color)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
    }

    private static void SetProp(SerializedObject so, string name, object value)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p == null)
        {
            Debug.LogWarning($"[GameSetupBuilder] Propriedade '{name}' não encontrada em {so.targetObject.name}.");
            return;
        }

        Assign(p, value);
    }

    private static void SetRel(SerializedProperty parent, string name, object value)
    {
        SerializedProperty p = parent.FindPropertyRelative(name);
        if (p != null) Assign(p, value);
    }

    private static void Assign(SerializedProperty p, object value)
    {
        switch (value)
        {
            case float f: p.floatValue = f; break;
            case bool b: p.boolValue = b; break;
            case int i:
                if (p.propertyType == SerializedPropertyType.Enum) p.enumValueFlag = i;
                else p.intValue = i;
                break;
        }
    }

    private static void RenameGameScene(StringBuilder log)
    {
        if (File.Exists(GameScenePath) || !File.Exists(OldScenePath)) return;
        string error = AssetDatabase.MoveAsset(OldScenePath, GameScenePath);
        if (!string.IsNullOrEmpty(error)) throw new System.InvalidOperationException("Não consegui renomear a cena: " + error);
        log.AppendLine("  - SampleScene renomeada para Game (mesmo GUID)");
    }

    private static void ConfigureGameScene(GameObject boss, CardDefinition[] cards, StringBuilder log)
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        DungeonManager dungeon = Object.FindAnyObjectByType<DungeonManager>();
        if (dungeon != null) Set(dungeon, "bossPrefab", boss);

        CardDraftManager draft = FindOrCreate<CardDraftManager>("CardDraftManager");
        var draftSo = new SerializedObject(draft);
        SerializedProperty deck = draftSo.FindProperty("deck");
        deck.arraySize = cards.Length;
        for (int i = 0; i < cards.Length; i++) deck.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
        draftSo.ApplyModifiedPropertiesWithoutUndo();

        ClassSelectionLobby lobby = Object.FindAnyObjectByType<ClassSelectionLobby>();
        if (lobby != null)
        {
            var lobbySo = new SerializedObject(lobby);
            lobbySo.FindProperty("startCountdown").floatValue = 5f;
            lobbySo.ApplyModifiedPropertiesWithoutUndo();
        }

        FindOrCreate<GameHud>("GameHud");
        FindOrCreate<PauseMenu>("PauseMenu");
        FindOrCreate<EndScreen>("EndScreen");
        FindOrCreate<GameAudio>("GameAudio");

        Subterraneorum.CameraSystem.CameraManager cameraManager = Object.FindAnyObjectByType<Subterraneorum.CameraSystem.CameraManager>();
        if (cameraManager != null)
        {
            var cameraSo = new SerializedObject(cameraManager);
            float distance = 18f;
            float pitch = 55f * Mathf.Deg2Rad;
            cameraSo.FindProperty("followOffset").vector3Value = new Vector3(0f, distance * Mathf.Sin(pitch), -distance * Mathf.Cos(pitch));
            cameraSo.FindProperty("maxZoomOut").floatValue = 0f;
            cameraSo.ApplyModifiedPropertiesWithoutUndo();
        }

        Camera main = Camera.main;
        if (main != null)
        {
            var data = main.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
            {
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                data.renderShadows = true;
                EditorUtility.SetDirty(data);
            }
        }

        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (light.type != LightType.Directional) continue;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.95f, 0.86f);
            if (light.shadows == LightShadows.Soft) light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.75f;
            EditorUtility.SetDirty(light);
        }

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
        if (profile != null && profile.TryGet(out Bloom bloom))
        {
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.6f);
            EditorUtility.SetDirty(profile);
        }

        GameObject sphere = GameObject.Find("Sphere");
        if (sphere != null && sphere.transform.parent == null) Object.DestroyImmediate(sphere);

        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(rootObject);
            string path = source != null ? AssetDatabase.GetAssetPath(source) : "";
            if (path.Contains("UltimateModularRuins"))
            {
                log.AppendLine($"  - Cena: peça solta '{rootObject.name}' removida ({path})");
                Object.DestroyImmediate(rootObject);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine("  - Cena Game: chefão ligado, CardDraftManager, GameHud, PauseMenu, EndScreen, GameAudio, FXAA");
    }

    private static T FindOrCreate<T>(string name) where T : Component
    {
        T existing = Object.FindAnyObjectByType<T>();
        if (existing != null) return existing;
        return new GameObject(name).AddComponent<T>();
    }

    private static void CreateMainMenuScene(StringBuilder log)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.03f, 0.06f);
        cameraGo.AddComponent<UniversalAdditionalCameraData>();
        cameraGo.AddComponent<AudioListener>();

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();

        new GameObject("MainMenu").AddComponent<MainMenuController>();
        new GameObject("GameAudio").AddComponent<GameAudio>();

        EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        log.AppendLine($"  - Cena {MainMenuScenePath} (Jogar / Como jogar / Sair)");
    }

    private static void ConfigureBuildSettings(StringBuilder log)
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MainMenuScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true),
        };
        log.AppendLine("  - Build Settings: MainMenu (0), Game (1)");
    }

    [MenuItem("Subterraneorum/Build Windows")]
    public static void BuildWindows()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { MainMenuScenePath, GameScenePath },
            locationPathName = "Builds/Windows/Subterraneorum.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log($"[GameSetupBuilder] Build Windows: {summary.result}, {summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} erro(s), {summary.totalTime.TotalSeconds:0}s → {summary.outputPath}");
        if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    private static Material LoadOrCreateMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new System.InvalidOperationException("Shader não encontrado: " + shaderName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        return material;
    }

    private static GameObject SavePrefab(GameObject instance, string path)
    {
        try
        {
            PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static Bounds ModelBounds(GameObject go)
    {
        bool any = false;
        var result = new Bounds(go.transform.position, Vector3.zero);
        foreach (SkinnedMeshRenderer skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var baked = new Mesh();
            skinned.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            Matrix4x4 toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            foreach (Vector3 v in vertices)
            {
                Vector3 p = toWorld.MultiplyPoint3x4(v);
                if (!any)
                {
                    result = new Bounds(p, Vector3.zero);
                    any = true;
                }
                else
                {
                    result.Encapsulate(p);
                }
            }

            Object.DestroyImmediate(baked);
        }

        foreach (MeshRenderer renderer in go.GetComponentsInChildren<MeshRenderer>())
        {
            if (!any)
            {
                result = renderer.bounds;
                any = true;
            }
            else
            {
                result.Encapsulate(renderer.bounds);
            }
        }

        return any ? result : RendererBounds(go);
    }

    private static Bounds RendererBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
    }

    private static void Set(Object target, string property, Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(property);
        if (p == null) throw new System.InvalidOperationException($"Campo '{property}' não existe em {target.GetType().Name}.");
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(Object target, string property, float value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(property);
        if (p == null) throw new System.InvalidOperationException($"Campo '{property}' não existe em {target.GetType().Name}.");
        p.floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetInt(Object target, string property, int value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(property);
        if (p == null) throw new System.InvalidOperationException($"Campo '{property}' não existe em {target.GetType().Name}.");
        p.intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
