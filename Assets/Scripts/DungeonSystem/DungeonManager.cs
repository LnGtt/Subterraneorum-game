using System;
using System.Collections;
using System.Collections.Generic;
using ProceduralRuins;
using Subterraneorum.CameraSystem;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Subterraneorum.DungeonSystem
{
    public class DungeonManager : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private DungeonGenerator generator;
        [SerializeField] private NavMeshSurface navMeshSurface;

        [Header("Inimigos")]
        [SerializeField] private GameObject meleePrefab;
        [SerializeField] private GameObject rangedPrefab;
        [SerializeField] private GameObject heavyPrefab;
        [SerializeField] private GameObject bossPrefab;
        [Tooltip("Melee por sala normal (mín, máx) no nível 1. Ganha +1 a cada 'Extra Melee Every Levels' andares.")]
        [SerializeField] private Vector2Int meleePerRoom = new Vector2Int(2, 3);
        [Tooltip("A cada quantos andares a sala ganha +1 melee (2 = +1 no 3º andar).")]
        [SerializeField, Min(1)] private int extraMeleeEveryLevels = 2;
        [Tooltip("Ranged por sala normal (mín, máx). O heavy (do nível 2 em diante) usa a mesma quantidade.")]
        [SerializeField] private Vector2Int rangedPerRoom = new Vector2Int(0, 1);
        [Tooltip("Vida extra dos inimigos por andar (0.15 = +15% no 2º andar, +30% no 3º). Não vale para o chefão.")]
        [SerializeField] private float enemyHealthPerLevel = 0.15f;
        [Tooltip("Inimigos extras por jogador adicional (0.35 = +35% com 2 jogadores).")]
        [SerializeField] private float enemiesPerExtraPlayer = 0.35f;
        [Tooltip("Vida extra do chefão por jogador adicional (0.6 = +60% com 2 jogadores). O chefão NÃO ganha a vida extra por nível: ele sempre está no último andar, e com 5 andares isso mais que dobraria a vida dele.")]
        [SerializeField] private float bossHealthPerExtraPlayer = 0.6f;

        [Header("Fluxo")]
        [Tooltip("Gera o nível 1 no Start. Desligado quando a cena tem o ClassSelectionLobby, que chama StartRun() ao fim da seleção de classes.")]
        [SerializeField] private bool generateOnStart = true;
        [Tooltip("Ao descer de nível, jogadores mortos voltam com esta % de vida (0 = não revive).")]
        [SerializeField, Range(0f, 1f)] private float reviveOnNewLevel = 0.3f;

        private GeneratedLevel currentLevel;
        private Transform enemiesRoot;
        private DungeonExitTrigger exitTrigger;
        private bool bossSpawned;
        private bool runEnded;
        private bool runStarted;
        private bool levelReady;
        private readonly HashSet<PlayerStatus> trackedPlayers = new HashSet<PlayerStatus>();

        public int CurrentLevelIndex { get; private set; }

        public int LevelCount => generator != null ? Mathf.Max(1, generator.levelCount) : 1;

        public bool IsOnFinalLevel => currentLevel != null && currentLevel.isFinalLevel;
        public GeneratedLevel CurrentLevel => currentLevel;

        public static event Action OnDungeonGenerationCompleted;
        public static event Action<int> OnLevelStarted;
        public static event Action OnVictory;
        public static event Action OnDefeat;
        public static event Action<EnemyStatus> OnBossSpawned;

        public bool RunEnded => runEnded;

        private void Awake()
        {
            TeamModifiers.ResetAll();
            EnemyModifiers.ResetAll();
            GamePause.ForceRelease();

            if (generator == null) generator = FindAnyObjectByType<DungeonGenerator>();
            if (navMeshSurface == null && generator != null) navMeshSurface = generator.GetComponent<NavMeshSurface>();
            if (navMeshSurface == null) navMeshSurface = GetComponent<NavMeshSurface>();
        }

        private void OnEnable()
        {
            DungeonRoomMarker.OnFirstPlayerEntered += HandleRoomEntered;
            PlayerRegistry.OnPlayerRegistered += HandlePlayerRegistered;
            PlayerRegistry.OnPlayerUnregistered += Untrack;
        }

        private void OnDisable()
        {
            DungeonRoomMarker.OnFirstPlayerEntered -= HandleRoomEntered;
            PlayerRegistry.OnPlayerRegistered -= HandlePlayerRegistered;
            PlayerRegistry.OnPlayerUnregistered -= Untrack;

            foreach (PlayerStatus player in trackedPlayers)
            {
                if (player != null) player.OnPlayerDied -= CheckDefeat;
            }

            trackedPlayers.Clear();
        }

        private void Start()
        {
            foreach (PlayerStatus player in PlayerRegistry.Players)
            {
                Track(player);
            }

            if (generateOnStart)
            {
                StartRun();
            }
        }

        public void StartRun()
        {
            if (runStarted) return;
            runStarted = true;
            StartCoroutine(LoadLevelRoutine(0));
        }

        private void Track(PlayerStatus player)
        {
            if (player != null && trackedPlayers.Add(player))
            {
                player.OnPlayerDied += CheckDefeat;
            }
        }

        private void Untrack(PlayerStatus player)
        {
            if (player != null && trackedPlayers.Remove(player))
            {
                player.OnPlayerDied -= CheckDefeat;
            }
        }

        private void HandlePlayerRegistered(PlayerStatus player)
        {
            Track(player);
            if (!levelReady || currentLevel == null || currentLevel.StartRoom == null) return;

            IReadOnlyList<PlayerStatus> players = PlayerRegistry.Players;
            int index = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == player) index = i;
            }

            Vector3 position = StartRoomSlot(index, players.Count);

            player.transform.SetPositionAndRotation(position, Quaternion.identity);
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.rotation = Quaternion.identity;
            }

            Physics.SyncTransforms();
            Debug.Log($"[DungeonManager] {player.name} entrou no meio da partida: posicionado na sala Start ({position}).");
        }

        public void GoToNextLevel()
        {
            if (runEnded) return;
            StartCoroutine(LoadLevelRoutine(CurrentLevelIndex + 1));
        }

        [ContextMenu("Debug: Ir para o último andar")]
        public void GoToFinalLevel()
        {
            if (runEnded) return;
            StartCoroutine(LoadLevelRoutine(LevelCount - 1));
        }

        private IEnumerator LoadLevelRoutine(int levelIndex)
        {
            if (generator == null)
            {
                Debug.LogError("[DungeonManager] Nenhum DungeonGenerator na cena.", this);
                yield break;
            }

            CurrentLevelIndex = levelIndex;
            levelReady = false;
            bossSpawned = false;
            ClearEnemies();

            currentLevel = generator.Generate(levelIndex);
            if (currentLevel == null || currentLevel.StartRoom == null)
            {
                yield break;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            BakeNavMesh();
            PlacePlayers();
            levelReady = true;
            SpawnEnemies();
            CreateExit();
            CreateSafeZones();

            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.SnapToTargets();
            }

            OnDungeonGenerationCompleted?.Invoke();
            OnLevelStarted?.Invoke(levelIndex);
        }

        private void BakeNavMesh()
        {
            if (navMeshSurface == null)
            {
                Debug.LogError("[DungeonManager] Nenhum NavMeshSurface configurado: inimigos não vão andar.", this);
                return;
            }

            navMeshSurface.collectObjects = navMeshSurface.transform == generator.transform ? CollectObjects.Children : CollectObjects.All;
            navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            int env = LayerMask.NameToLayer("Environment");
            if (env >= 0) navMeshSurface.layerMask = 1 << env;

            navMeshSurface.RemoveData();
            navMeshSurface.BuildNavMesh();
        }

        public GeneratedRoom RoomAt(Vector3 position)
        {
            if (currentLevel == null) return null;
            foreach (GeneratedRoom room in currentLevel.rooms)
            {
                Bounds b = room.worldBounds;
                if (position.x >= b.min.x && position.x <= b.max.x && position.z >= b.min.z && position.z <= b.max.z) return room;
            }

            return null;
        }

        public int AliveEnemiesInRoom(int roomId)
        {
            int count = 0;
            IReadOnlyList<EnemyStatus> enemies = EnemyStatus.All;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStatus enemy = enemies[i];
                if (enemy != null && !enemy.IsDead && enemy.HomeRoomId == roomId) count++;
            }

            return count;
        }

        private Vector3 StartRoomSlot(int index, int count)
        {
            GeneratedRoom start = currentLevel.StartRoom;
            float angle = count > 1 ? index * Mathf.PI * 2f / count : 0f;
            Vector3 offset = count > 1 ? new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f : Vector3.zero;
            Vector3 position = SampleNavMesh(start.center + offset, 3f, start.center);
            position.y = start.center.y;
            return position;
        }

        private void PlacePlayers()
        {
            IReadOnlyList<PlayerStatus> players = PlayerRegistry.Players;
            int count = players.Count;

            for (int i = 0; i < count; i++)
            {
                PlayerStatus player = players[i];
                if (player == null) continue;

                if (player.IsDead && reviveOnNewLevel > 0f && CurrentLevelIndex > 0)
                {
                    player.Revive(reviveOnNewLevel);
                }

                Vector3 position = StartRoomSlot(i, count);

                PlayerMovement movement = player.GetComponent<PlayerMovement>();
                if (movement != null)
                {
                    movement.Teleport(position, Quaternion.identity);
                }
                else
                {
                    player.transform.SetPositionAndRotation(position, Quaternion.identity);
                }
            }

            Physics.SyncTransforms();
        }

        private void SpawnEnemies()
        {
            int level = CurrentLevelIndex;
            float playerFactor = 1f + enemiesPerExtraPlayer * Mathf.Max(0, PlayerRegistry.Players.Count - 1);
            float healthScale = 1f + enemyHealthPerLevel * level;
            int extraMelee = level / Mathf.Max(1, extraMeleeEveryLevels);

            foreach (GeneratedRoom room in currentLevel.rooms)
            {
                if (room.type != DungeonRoomType.Normal && room.type != DungeonRoomType.Treasure) continue;

                int melee = Mathf.RoundToInt(UnityEngine.Random.Range(meleePerRoom.x, meleePerRoom.y + 1) * playerFactor) + extraMelee;
                int ranged = Mathf.RoundToInt(UnityEngine.Random.Range(rangedPerRoom.x, rangedPerRoom.y + 1) * playerFactor);
                int heavy = level >= 1 ? ranged : 0;

                List<Vector2Int> cells = new List<Vector2Int>(room.freeCells);
                Shuffle(cells);
                int cellIndex = 0;

                cellIndex = SpawnGroup(meleePrefab, melee, room, cells, cellIndex, healthScale);
                cellIndex = SpawnGroup(rangedPrefab, ranged, room, cells, cellIndex, healthScale);
                SpawnGroup(heavyPrefab, heavy, room, cells, cellIndex, healthScale);
            }
        }

        private int SpawnGroup(GameObject prefab, int amount, GeneratedRoom room, List<Vector2Int> cells, int cellIndex, float healthScale)
        {
            if (prefab == null || amount <= 0 || cells.Count == 0) return cellIndex;

            for (int i = 0; i < amount; i++)
            {
                Vector2Int cell = cells[cellIndex % cells.Count];
                cellIndex++;
                Vector3 jitter = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f));
                Vector3 desired = generator.CellToWorld(cell.x, cell.y) + jitter;
                if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;

                GameObject enemy = Instantiate(prefab, hit.position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), enemiesRoot);
                EnemyStatus status = enemy.GetComponent<EnemyStatus>();
                if (status != null)
                {
                    status.HomeRoomId = room.id;
                    if (!Mathf.Approximately(healthScale, 1f)) status.ScaleHealth(healthScale);
                }
            }

            return cellIndex;
        }

        private void CreateExit()
        {
            GeneratedRoom exitRoom = currentLevel.ExitRoom;
            if (exitRoom == null) return;

            var go = new GameObject("LevelExit_Stairs") { layer = 2 };
            go.transform.SetParent(currentLevel.root, false);
            go.transform.position = exitRoom.center + Vector3.up;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(currentLevel.cellSize * 2f, 2f, currentLevel.cellSize * 2f);

            exitTrigger = go.AddComponent<DungeonExitTrigger>();
            exitTrigger.OnAllPlayersReachedExit += GoToNextLevel;
        }

        private void CreateSafeZones()
        {
            foreach (GeneratedRoom room in currentLevel.RoomsOfType(DungeonRoomType.SafeZone))
            {
                var go = new GameObject("SafeZone") { layer = 2 };
                go.transform.SetParent(currentLevel.root, false);
                go.transform.position = room.worldBounds.center;
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(room.worldBounds.size.x, 4f, room.worldBounds.size.z);
                go.AddComponent<SafeZone>();
                go.AddComponent<SafeZoneMinigame>();

                var lightGo = new GameObject("SafeZoneLight");
                lightGo.transform.SetParent(go.transform, false);
                lightGo.transform.localPosition = Vector3.up * 3f;
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.85f, 0.55f);
                light.range = Mathf.Max(room.worldBounds.size.x, room.worldBounds.size.z);
                light.intensity = 3f;
            }
        }

        private void HandleRoomEntered(DungeonRoomMarker marker, GameObject player)
        {
            if (marker.roomType != DungeonRoomType.Boss) return;
            SpawnBossNow();
        }

        public void SpawnBossNow()
        {
            GeneratedRoom bossRoom = currentLevel?.BossRoom;
            if (bossSpawned || bossRoom == null) return;

            bossSpawned = true;
            if (bossPrefab == null)
            {
                Debug.LogWarning("[DungeonManager] Sala do chefão alcançada, mas nenhum Boss Prefab foi configurado.", this);
                return;
            }

            Vector3 pos = SampleNavMesh(bossRoom.center, 4f, bossRoom.center);
            GameObject boss = Instantiate(bossPrefab, pos, Quaternion.Euler(0f, 180f, 0f), enemiesRoot);
            EnemyStatus status = boss.GetComponent<EnemyStatus>();
            if (status != null)
            {
                float players = Mathf.Max(1, PlayerRegistry.Players.Count);
                float scale = 1f + bossHealthPerExtraPlayer * (players - 1f);
                if (!Mathf.Approximately(scale, 1f)) status.ScaleHealth(scale);
                status.OnEnemyDied += _ => Victory();
                OnBossSpawned?.Invoke(status);

                if (CameraManager.Instance != null)
                {
                    CameraManager.Instance.AddTarget(boss.transform, 0.8f, 4f);
                    status.OnEnemyDied += dead =>
                    {
                        if (CameraManager.Instance != null && dead != null) CameraManager.Instance.RemoveTarget(dead.transform);
                    };
                }
            }

            Debug.Log($"[DungeonManager] Chefão apareceu na sala Boss ({pos}).");
        }

        private void CheckDefeat()
        {
            if (runEnded || PlayerRegistry.AliveCount > 0) return;

            runEnded = true;
            GameAudio.Play(GameSound.Defeat);
            Debug.Log("[DungeonManager] Todos os jogadores caíram — derrota.");
            OnDefeat?.Invoke();
        }

        private void Victory()
        {
            if (runEnded) return;

            runEnded = true;
            GameAudio.Play(GameSound.Victory);
            Debug.Log("[DungeonManager] Chefão derrotado — vitória!");
            OnVictory?.Invoke();
        }

        private void ClearEnemies()
        {
            if (enemiesRoot != null)
            {
                Destroy(enemiesRoot.gameObject);
            }

            enemiesRoot = new GameObject("Enemies").transform;
            enemiesRoot.SetParent(transform, false);
        }

        private static Vector3 SampleNavMesh(Vector3 desired, float radius, Vector3 fallback)
        {
            return NavMesh.SamplePosition(desired, out NavMeshHit hit, radius, NavMesh.AllAreas) ? hit.position : fallback;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
