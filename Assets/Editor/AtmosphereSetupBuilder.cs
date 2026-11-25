using System.Collections.Generic;
using System.IO;
using System.Text;
using ProceduralRuins;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class AtmosphereSetupBuilder
{
    private const string MaterialFolder = "Assets/Materials/Environment";
    private const string PrefabFolder = "Assets/prefabs/Environment";
    private const string TorchModelPath = "Assets/prefabs/UltimateModularRuins/Torch.fbx";
    private const string ConfigPath = "Assets/DungeonGenerationConfig.asset";
    private const string FlameMaterialPath = MaterialFolder + "/TorchFlame.mat";
    private const string SparkMaterialPath = "Assets/Resources/Subterraneorum/SparkMaterial.mat";
    private const string GroundMaterialPath = MaterialFolder + "/SurfaceGrass.mat";
    public const string TorchPrefabPath = PrefabFolder + "/TorchFlamePrefab.prefab";

    [MenuItem("Subterraneorum/Configurar Atmosfera e Tochas")]
    public static void Build()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new StringBuilder("[AtmosphereSetupBuilder]\n");
        EnsureFolder(MaterialFolder);
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/Resources/Subterraneorum");

        Material flame = CreateFlameMaterial(FlameMaterialPath);
        CreateFlameMaterial(SparkMaterialPath);
        log.AppendLine($"  - {SparkMaterialPath}: faíscas do efeito de subir de nível");
        Material grass = CreateGrassMaterial();
        GameObject torch = CreateTorchPrefab(flame, log);
        DungeonGenerationConfig config = AssignTorchToConfig(torch, log);
        ConfigureGameScene(grass, config, log);

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    private static Material CreateFlameMaterial(string path)
    {
        Material mat = LoadOrCreateMaterial(path, "Universal Render Pipeline/Particles/Unlit");

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        Texture2D particle = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        if (particle != null)
        {
            mat.SetTexture("_BaseMap", particle);
            mat.SetTexture("_MainTex", particle);
        }

        mat.SetColor("_BaseColor", new Color(1.7f, 1.2f, 0.75f, 1f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material CreateGrassMaterial()
    {
        Material mat = LoadOrCreateMaterial(GroundMaterialPath, "Universal Render Pipeline/Lit");
        Color green = new Color(0.30f, 0.46f, 0.21f);
        mat.SetColor("_BaseColor", green);
        mat.SetColor("_Color", green);
        mat.SetFloat("_Smoothness", 0.05f);
        mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material LoadOrCreateMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (shader != null && mat.shader != shader)
        {
            mat.shader = shader;
        }

        return mat;
    }

    private static GameObject CreateTorchPrefab(Material flameMaterial, StringBuilder log)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(TorchModelPath);
        if (model == null) throw new FileNotFoundException($"{TorchModelPath} não encontrado.");

        var root = new GameObject("TorchFlamePrefab");
        var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        modelInstance.transform.localPosition = Vector3.zero;

        Vector3 tip = FlameTip(modelInstance, root.transform);

        var flame = new GameObject("Flame");
        flame.transform.SetParent(root.transform, false);
        flame.transform.localPosition = tip;
        ConfigureFlame(flame.AddComponent<ParticleSystem>(), flameMaterial);

        var embers = new GameObject("Embers");
        embers.transform.SetParent(flame.transform, false);
        ConfigureEmbers(embers.AddComponent<ParticleSystem>(), flameMaterial);

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = tip + Vector3.up * 0.15f;
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.62f, 0.28f);
        light.range = 9f;
        light.intensity = 2.6f;
        light.shadows = LightShadows.None;

        LightFlicker flicker = lightGo.AddComponent<LightFlicker>();
        var so = new SerializedObject(flicker);
        so.FindProperty("minIntensity").floatValue = 2f;
        so.FindProperty("maxIntensity").floatValue = 3.2f;
        so.FindProperty("speed").floatValue = 5f;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, TorchPrefabPath);
        Object.DestroyImmediate(root);
        log.AppendLine($"  - {TorchPrefabPath}: Torch + chama + brasas + luz (chama em {tip})");
        return prefab;
    }

    private static Vector3 FlameTip(GameObject modelInstance, Transform root)
    {
        var points = new List<Vector3>();
        foreach (MeshFilter mf in modelInstance.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            foreach (Vector3 v in mf.sharedMesh.vertices) points.Add(root.InverseTransformPoint(mf.transform.TransformPoint(v)));
        }

        if (points.Count == 0) return Vector3.up;

        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 p in points)
        {
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        float threshold = maxY - (maxY - minY) * 0.12f;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Vector3 p in points)
        {
            if (p.y < threshold) continue;
            sum += p;
            count++;
        }

        Vector3 top = sum / Mathf.Max(1, count);
        return new Vector3(top.x, maxY - 0.03f, top.z);
    }

    private static void ConfigureFlame(ParticleSystem ps, Material material)
    {
        ParticleSystem.MainModule main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.26f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 60;
        main.prewarm = true;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 30f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.04f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f),
                new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.4f),
                new GradientColorKey(new Color(0.75f, 0.18f, 0.04f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.12f),
                new GradientAlphaKey(0.7f, 0.55f),
                new GradientAlphaKey(0f, 1f),
            });
        color.color = gradient;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 2f;
        noise.scrollSpeed = 1.5f;

        ConfigureRenderer(ps, material);
    }

    private static void ConfigureEmbers(ParticleSystem ps, Material material)
    {
        ParticleSystem.MainModule main = ps.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
        main.startColor = new Color(1f, 0.6f, 0.2f);
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 20;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 5f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.05f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 1.2f;

        ConfigureRenderer(ps, material);
    }

    private static void ConfigureRenderer(ParticleSystem ps, Material material)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static DungeonGenerationConfig AssignTorchToConfig(GameObject torch, StringBuilder log)
    {
        DungeonGenerationConfig config = AssetDatabase.LoadAssetAtPath<DungeonGenerationConfig>(ConfigPath);
        if (config == null)
        {
            log.AppendLine($"  ! {ConfigPath} não encontrado: tocha não ligada ao gerador.");
            return null;
        }

        config.torchPrefab = torch;
        config.torchesPerRoomMin = 1;
        config.torchesPerRoomMax = 2;
        config.extraTorchesUnderground = 1;

        config.floorHoleChance = 0f;
        config.floorTileOverlap = 1.012f;

        int removed = config.wallDecorPrefabs.RemoveAll(p => p != null && AssetDatabase.GetAssetPath(p) == TorchModelPath);
        EditorUtility.SetDirty(config);
        log.AppendLine($"  - Config: torchPrefab = TorchFlamePrefab (1–2 por sala); {removed} tocha(s) apagada(s) tirada(s) da decoração de parede");
        return config;
    }

    private static void ConfigureGameScene(Material grass, DungeonGenerationConfig config, StringBuilder log)
    {
        Scene scene = EditorSceneManager.OpenScene(GameSetupBuilder.ResolveGameScene(), OpenSceneMode.Single);

        LevelAtmosphere atmosphere = Object.FindAnyObjectByType<LevelAtmosphere>();
        if (atmosphere == null) atmosphere = new GameObject("LevelAtmosphere").AddComponent<LevelAtmosphere>();

        atmosphere.ResetPresets();
        EditorUtility.SetDirty(atmosphere);

        DungeonGenerator generator = Object.FindAnyObjectByType<DungeonGenerator>();
        Vector3 center = generator != null ? generator.transform.position : Vector3.zero;

        Transform groundTransform = atmosphere.transform.Find("SurfaceGround");
        GameObject ground = groundTransform != null ? groundTransform.gameObject : null;
        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "SurfaceGround";
            ground.transform.SetParent(atmosphere.transform, false);
        }

        foreach (Collider c in ground.GetComponents<Collider>()) Object.DestroyImmediate(c);
        ground.layer = 0;
        ground.transform.position = new Vector3(center.x, center.y - 0.2f, center.z);
        ground.transform.localScale = new Vector3(80f, 1f, 80f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = grass;

        Light sun = null;
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (light.type == LightType.Directional) { sun = light; break; }
        }

        var so = new SerializedObject(atmosphere);
        so.FindProperty("sun").objectReferenceValue = sun;
        so.FindProperty("targetCamera").objectReferenceValue = Camera.main;
        so.FindProperty("surfaceGround").objectReferenceValue = ground;
        so.FindProperty("vegetationConfig").objectReferenceValue = config;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine($"  - Cena {scene.name}: LevelAtmosphere (sol={(sun != null ? sun.name : "nenhum")}, câmera={(Camera.main != null ? Camera.main.name : "nenhuma")}) + SurfaceGround 800 m");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
