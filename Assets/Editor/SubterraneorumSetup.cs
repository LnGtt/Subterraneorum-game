using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProceduralRuins;
using Subterraneorum.DungeonSystem;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class SubterraneorumSetup
{
    private const string ConfigPath = "Assets/DungeonGenerationConfig.asset";

    public const int FloorCount = 3;
    private const string EnemyFolder = "Assets/prefabs/Enemies";

    [MenuItem("Subterraneorum/Configurar Cena de Jogo")]
    public static void ConfigureGameScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EditorSceneManager.OpenScene(GameSetupBuilder.ResolveGameScene(), OpenSceneMode.Single);
        var log = new StringBuilder("[SubterraneorumSetup] Cena configurada:\n");

        foreach (string name in new[] { "NavigationGround", "Navigation", "RoomBounds_Sala01", "EnemyMeleeInstance", "EnemyRangedInstance", "EnemyHeavyInstance" })
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
                log.AppendLine($"  - removido {name}");
            }
        }

        DungeonGenerator generator = Object.FindAnyObjectByType<DungeonGenerator>();
        if (generator == null)
        {
            generator = new GameObject("DungeonGenerator").AddComponent<DungeonGenerator>();
            log.AppendLine("  - criado DungeonGenerator");
        }

        generator.transform.position = Vector3.zero;
        generator.config = AssetDatabase.LoadAssetAtPath<DungeonGenerationConfig>(ConfigPath);
        generator.levelCount = FloorCount;
        generator.previewLevel = 0;
        generator.seed = -1;
        generator.Clear();
        EditorUtility.SetDirty(generator);
        log.AppendLine($"  - gerador: config={(generator.config != null ? "ok" : "NÃO ENCONTRADA")}, {FloorCount} níveis, dungeon de preview limpa");

        NavMeshSurface surface = generator.GetComponent<NavMeshSurface>();
        if (surface == null) surface = generator.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = 1 << LayerMask.NameToLayer("Environment");
        surface.navMeshData = null;
        EditorUtility.SetDirty(surface);

        DungeonManager manager = Object.FindAnyObjectByType<DungeonManager>();
        if (manager == null)
        {
            manager = new GameObject("DungeonManager").AddComponent<DungeonManager>();
            log.AppendLine("  - criado DungeonManager");
        }

        var so = new SerializedObject(manager);
        so.FindProperty("generator").objectReferenceValue = generator;
        so.FindProperty("navMeshSurface").objectReferenceValue = surface;
        so.FindProperty("meleePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{EnemyFolder}/EnemyMeleePrefab.prefab");
        so.FindProperty("rangedPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{EnemyFolder}/EnemyRangedPrefab.prefab");
        so.FindProperty("heavyPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{EnemyFolder}/EnemyHeavyPrefab.prefab");
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("  - DungeonManager ligado ao gerador, NavMeshSurface e prefabs de inimigo");

        TunePrefab($"{EnemyFolder}/EnemyMeleePrefab.prefab", typeof(EnemyMelee), ("attackRadius", 2.2f), ("attackRange", 1.6f));
        TunePrefab($"{EnemyFolder}/EnemyRangedPrefab.prefab", typeof(EnemyRanged), ("attackCooldown", 1.4f));
        TunePrefab($"{EnemyFolder}/EnemyHeavyPrefab.prefab", typeof(EnemyHeavy), ("slamRadius", 2.8f), ("attackRange", 1.8f));
        SetAgentSpeed($"{EnemyFolder}/EnemyRangedPrefab.prefab", 4.5f);
        SetAgentSpeed($"{EnemyFolder}/EnemyHeavyPrefab.prefab", 5f);
        log.AppendLine("  - prefabs de inimigo ajustados (cone melee, área heavy, velocidades)");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    [MenuItem("Subterraneorum/Validar Geração (2 níveis)")]
    public static void ValidateGeneration()
    {
        var report = new StringBuilder("[SubterraneorumValidate]\n");
        DungeonGenerationConfig config = AssetDatabase.LoadAssetAtPath<DungeonGenerationConfig>(ConfigPath);
        if (config == null)
        {
            Debug.LogError("[SubterraneorumValidate] Config não encontrada em " + ConfigPath);
            return;
        }

        report.AppendLine($"cellSize={config.cellSize} pieceScale={config.pieceScale}");
        ReportSizes(report, "floor", config.floorPrefabs, config);
        ReportSizes(report, "floorDecor", config.floorDecorativePrefabs, config);
        ReportSizes(report, "floorHole", config.floorHolePrefabs, config);
        ReportSizes(report, "wall", config.wallPrefabs, config);
        ReportSizes(report, "wallBroken", config.wallBrokenPrefabs, config);
        ReportSizes(report, "wallWindow", config.wallWindowPrefabs, config);
        ReportSizes(report, "door", config.doorPrefabs, config);
        ReportSizes(report, "stairs", config.stairsPrefabs, config);

        var temp = new GameObject("TempGenerator");
        try
        {
            DungeonGenerator generator = temp.AddComponent<DungeonGenerator>();
            generator.config = config;
            generator.levelCount = FloorCount;

            for (int run = 0; run < 6; run++)
            {
                for (int level = 0; level < FloorCount; level++)
                {
                    generator.seed = 1000 + run;
                    GeneratedLevel result = generator.Generate(level);
                    if (result == null)
                    {
                        report.AppendLine("  generate retornou null");
                        continue;
                    }

                    string types = string.Join(",", result.rooms.GroupBy(r => r.type).Select(g => $"{g.Key}:{g.Count()}"));
                    int unreachable = result.rooms.Count(r => r.pathDistanceFromStart == int.MaxValue);
                    int walls = CountChildren(result.root, "Walls");
                    int doors = CountChildren(result.root, "Doors");
                    int colliders = result.root.GetComponentsInChildren<Collider>().Length;
                    bool hasGoal = result.isFinalLevel ? result.BossRoom != null : result.ExitRoom != null;
                    int noFree = result.rooms.Count(r => r.freeCells.Count == 0);
                    report.AppendLine($"  seed {1000 + run} nivel {level + 1}: salas={result.rooms.Count} [{types}] inalcancaveis={unreachable} objetivo={(hasGoal ? "ok" : "FALTANDO")} paredes={walls} portas={doors} colisores={colliders} salasSemCelulaLivre={noFree}");

                    NavMeshSurface surface = temp.GetComponent<NavMeshSurface>();
                    if (surface == null) surface = temp.AddComponent<NavMeshSurface>();
                    surface.collectObjects = CollectObjects.Children;
                    surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                    surface.layerMask = 1 << LayerMask.NameToLayer("Environment");
                    Physics.SyncTransforms();
                    surface.BuildNavMesh();

                    GeneratedRoom goal = result.isFinalLevel ? result.BossRoom : result.ExitRoom;
                    if (goal != null && NavMesh.SamplePosition(result.StartRoom.center, out NavMeshHit a, 3f, NavMesh.AllAreas)
                        && NavMesh.SamplePosition(goal.center, out NavMeshHit b, 3f, NavMesh.AllAreas))
                    {
                        var path = new NavMeshPath();
                        NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
                        report.AppendLine($"      navmesh start->objetivo: {path.status}");
                    }
                    else
                    {
                        report.AppendLine("      navmesh: não foi possível amostrar start/objetivo");
                    }

                    surface.RemoveData();
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }

        Debug.Log(report.ToString());
    }

    private static int CountChildren(Transform root, string category)
    {
        Transform t = root.Find(category);
        return t != null ? t.childCount : 0;
    }

    private static void ReportSizes(StringBuilder report, string label, List<GameObject> prefabs, DungeonGenerationConfig config)
    {
        foreach (GameObject prefab in prefabs.Where(p => p != null))
        {
            GameObject instance = Object.Instantiate(prefab);
            instance.transform.rotation = prefab.transform.localRotation;
            instance.transform.localScale = prefab.transform.localScale * config.pieceScale;
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                report.AppendLine($"  {label,-10} {prefab.name,-32} size={bounds.size} center={bounds.center} rootRot={prefab.transform.localEulerAngles}");
            }

            Object.DestroyImmediate(instance);
        }
    }

    private static void TunePrefab(string path, System.Type componentType, params (string property, float value)[] values)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        if (contents == null) return;

        try
        {
            Component component = contents.GetComponent(componentType);
            if (component == null) return;

            var so = new SerializedObject(component);
            foreach ((string property, float value) in values)
            {
                SerializedProperty prop = so.FindProperty(property);
                if (prop != null) prop.floatValue = value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void SetAgentSpeed(string path, float speed)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        if (contents == null) return;

        try
        {
            NavMeshAgent agent = contents.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.speed = speed;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
