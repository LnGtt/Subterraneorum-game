using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProceduralRuins
{
    [Serializable]
    public class GeneratedRoom
    {
        public int id;
        public DungeonRoomType type = DungeonRoomType.Normal;
        public RectInt cells;
        public Bounds worldBounds;
        public Vector3 center;
        public int pathDistanceFromStart;
        public DungeonRoomMarker marker;
        public List<Vector2Int> freeCells = new List<Vector2Int>();
    }

    public class GeneratedLevel
    {
        public int levelIndex;
        public bool isFinalLevel;
        public int seed;
        public float cellSize;
        public Transform root;
        public readonly List<GeneratedRoom> rooms = new List<GeneratedRoom>();

        public GeneratedRoom StartRoom => rooms.FirstOrDefault(r => r.type == DungeonRoomType.Start);
        public GeneratedRoom ExitRoom => rooms.FirstOrDefault(r => r.type == DungeonRoomType.Exit);
        public GeneratedRoom BossRoom => rooms.FirstOrDefault(r => r.type == DungeonRoomType.Boss);
        public IEnumerable<GeneratedRoom> RoomsOfType(DungeonRoomType type) => rooms.Where(r => r.type == type);
    }

    [DisallowMultipleComponent]
    public class DungeonGenerator : MonoBehaviour
    {
        [Header("Configuração")]
        public DungeonGenerationConfig config;

        [Header("Níveis")]
        [Tooltip("Total de níveis da run. O último tem a sala do chefão em vez da escada.")]
        [Min(1)] public int levelCount = 3;
        [Tooltip("Nível usado pelo botão 'Gerar' no editor (0 = primeiro).")]
        [Min(0)] public int previewLevel = 0;

        [Header("Grid por nível")]
        [Min(10)] public int gridWidth = 28;
        [Min(10)] public int gridHeight = 28;

        [Header("BSP")]
        [Min(4)] public int minLeafSize = 7;
        [Min(3)] public int minRoomSize = 4;
        [Range(1, 8)] public int maxBspDepth = 4;

        [Header("Salas especiais")]
        [Range(0, 5)] public int treasureRoomsPerLevel = 1;
        [Tooltip("Cria uma zona segura (cura + minijogo) no meio do caminho de cada nível.")]
        public bool createSafeZone = true;

        [Header("Seed")]
        [Tooltip("-1 usa uma seed aleatória a cada geração.")]
        public int seed = -1;

        private const string RootName = "Dungeon_Generated";
        private const int EnvironmentLayerFallback = 9;
        private const int TriggerLayer = 2;

        private System.Random rng;
        private int environmentLayer;

        private bool[,] grid;
        private int[,] roomId;
        private bool[,] blocked;
        private Transform levelRoot;
        private readonly Dictionary<string, Transform> categoryRoots = new Dictionary<string, Transform>();

        private class BspNode
        {
            public RectInt bounds;
            public BspNode left;
            public BspNode right;
            public RectInt room;
            public bool IsLeaf => left == null && right == null;
        }

        [ContextMenu("Gerar Calabouço (nível de preview)")]
        public void Generate()
        {
            Generate(previewLevel);
        }

        public GeneratedLevel Generate(int levelIndex)
        {
            Clear();

            if (config == null)
            {
                Debug.LogError("[DungeonGenerator] Nenhum DungeonGenerationConfig atribuído.", this);
                return null;
            }

            int usedSeed = seed >= 0 ? seed + levelIndex * 7919 : Environment.TickCount ^ (levelIndex * 7919);
            rng = new System.Random(usedSeed);
            environmentLayer = LayerMask.NameToLayer("Environment");
            if (environmentLayer < 0) environmentLayer = EnvironmentLayerFallback;

            bool isFinal = levelIndex >= levelCount - 1;
            var level = new GeneratedLevel
            {
                levelIndex = levelIndex,
                isFinalLevel = isFinal,
                seed = usedSeed,
                cellSize = config.cellSize,
            };

            levelRoot = new GameObject(RootName).transform;
            levelRoot.SetParent(transform, false);
            level.root = levelRoot;
            categoryRoots.Clear();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (BuildLayout(level))
                {
                    break;
                }

                level.rooms.Clear();
            }

            if (level.rooms.Count < 2)
            {
                Debug.LogError("[DungeonGenerator] Não foi possível gerar ao menos 2 salas. Aumente o grid ou reduza Min Leaf Size.", this);
                return level;
            }

            AssignRoomTypes(level);
            PlaceFloor(level);
            PlaceWallsAndDoors();
            PlaceProps(level);
            PlaceRoomMarkers(level);
            PlaceTransitions(level);

            Debug.Log($"[DungeonGenerator] Nível {levelIndex + 1}/{levelCount} gerado: {level.rooms.Count} salas, seed {usedSeed}.");
            return level;
        }

        [ContextMenu("Limpar")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != RootName) continue;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(child.gameObject);
                    continue;
                }
#endif
                child.gameObject.SetActive(false);
                child.name = RootName + "_Destroyed";
                Destroy(child.gameObject);
            }
        }

        public Vector3 CellToWorld(int x, int y)
        {
            return transform.position + new Vector3(x * config.cellSize, 0f, y * config.cellSize);
        }

        public Vector2Int GridSize => grid != null ? new Vector2Int(grid.GetLength(0), grid.GetLength(1)) : Vector2Int.zero;

        public float CellSize => config != null ? config.cellSize : 4f;

        public bool IsFloor(int x, int y) => grid != null && IsAlive(x, y);

        public int RoomIdAt(int x, int y) => roomId != null && InGrid(x, y) ? roomId[x, y] : -1;

        private bool BuildLayout(GeneratedLevel level)
        {
            grid = new bool[gridWidth, gridHeight];
            roomId = new int[gridWidth, gridHeight];
            blocked = new bool[gridWidth, gridHeight];
            for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                roomId[x, y] = -1;

            BspNode bspRoot = BuildBsp(new RectInt(1, 1, gridWidth - 2, gridHeight - 2), 0);
            CarveRooms(bspRoot, level);
            if (level.rooms.Count < 2)
            {
                return false;
            }

            ConnectRooms(bspRoot);
            return true;
        }

        private BspNode BuildBsp(RectInt bounds, int depth)
        {
            var node = new BspNode { bounds = bounds };

            bool canSplitH = bounds.height >= minLeafSize * 2;
            bool canSplitV = bounds.width >= minLeafSize * 2;
            if (depth >= maxBspDepth || (!canSplitH && !canSplitV))
            {
                return node;
            }

            bool splitHorizontally;
            if (canSplitH && canSplitV)
            {
                float ratio = bounds.height / (float)bounds.width;
                splitHorizontally = ratio > 1.25f || (ratio >= 0.8f && Chance(0.5f));
            }
            else
            {
                splitHorizontally = canSplitH;
            }

            if (splitHorizontally)
            {
                int splitY = rng.Next(bounds.yMin + minLeafSize, bounds.yMax - minLeafSize + 1);
                node.left = BuildBsp(new RectInt(bounds.x, bounds.y, bounds.width, splitY - bounds.y), depth + 1);
                node.right = BuildBsp(new RectInt(bounds.x, splitY, bounds.width, bounds.yMax - splitY), depth + 1);
            }
            else
            {
                int splitX = rng.Next(bounds.xMin + minLeafSize, bounds.xMax - minLeafSize + 1);
                node.left = BuildBsp(new RectInt(bounds.x, bounds.y, splitX - bounds.x, bounds.height), depth + 1);
                node.right = BuildBsp(new RectInt(splitX, bounds.y, bounds.xMax - splitX, bounds.height), depth + 1);
            }

            return node;
        }

        private void CarveRooms(BspNode node, GeneratedLevel level)
        {
            if (!node.IsLeaf)
            {
                CarveRooms(node.left, level);
                CarveRooms(node.right, level);
                return;
            }

            int availableW = node.bounds.width - 2;
            int availableH = node.bounds.height - 2;
            if (availableW < minRoomSize || availableH < minRoomSize) return;

            int roomW = rng.Next(minRoomSize, availableW + 1);
            int roomH = rng.Next(minRoomSize, availableH + 1);
            int roomX = node.bounds.x + 1 + rng.Next(0, availableW - roomW + 1);
            int roomY = node.bounds.y + 1 + rng.Next(0, availableH - roomH + 1);

            var cells = new RectInt(roomX, roomY, roomW, roomH);
            node.room = cells;

            int id = level.rooms.Count;
            level.rooms.Add(new GeneratedRoom { id = id, cells = cells });

            for (int x = cells.xMin; x < cells.xMax; x++)
            for (int y = cells.yMin; y < cells.yMax; y++)
            {
                grid[x, y] = true;
                roomId[x, y] = id;
            }
        }

        private RectInt GetRepresentativeRoom(BspNode node)
        {
            if (node.IsLeaf) return node.room;

            BspNode first = Chance(0.5f) ? node.left : node.right;
            BspNode second = first == node.left ? node.right : node.left;
            RectInt room = GetRepresentativeRoom(first);
            if (room.width == 0) room = GetRepresentativeRoom(second);
            return room;
        }

        private void ConnectRooms(BspNode node)
        {
            if (node.IsLeaf) return;

            ConnectRooms(node.left);
            ConnectRooms(node.right);

            RectInt roomA = GetRepresentativeRoom(node.left);
            RectInt roomB = GetRepresentativeRoom(node.right);
            if (roomA.width == 0 || roomB.width == 0) return;

            Vector2Int a = new Vector2Int(roomA.x + roomA.width / 2, roomA.y + roomA.height / 2);
            Vector2Int b = new Vector2Int(roomB.x + roomB.width / 2, roomB.y + roomB.height / 2);

            if (Chance(0.5f))
            {
                CarveLine(a.x, b.x, a.y, true);
                CarveLine(a.y, b.y, b.x, false);
            }
            else
            {
                CarveLine(a.y, b.y, a.x, false);
                CarveLine(a.x, b.x, b.y, true);
            }
        }

        private void CarveLine(int from, int to, int fixedCoord, bool horizontal)
        {
            int min = Mathf.Min(from, to), max = Mathf.Max(from, to);
            for (int i = min; i <= max; i++)
            {
                int x = horizontal ? i : fixedCoord;
                int y = horizontal ? fixedCoord : i;
                if (InGrid(x, y)) grid[x, y] = true;
            }
        }

        private void AssignRoomTypes(GeneratedLevel level)
        {
            float cs = config.cellSize;
            foreach (GeneratedRoom room in level.rooms)
            {
                Vector3 min = CellToWorld(room.cells.xMin, room.cells.yMin) - new Vector3(cs * 0.5f, 0f, cs * 0.5f);
                Vector3 size = new Vector3(room.cells.width * cs, 4f, room.cells.height * cs);
                room.worldBounds = new Bounds(min + size * 0.5f, size);
                room.center = new Vector3(room.worldBounds.center.x, transform.position.y, room.worldBounds.center.z);
            }

            Vector2 corner = new Vector2(Chance(0.5f) ? 0 : gridWidth, Chance(0.5f) ? 0 : gridHeight);
            GeneratedRoom start = level.rooms.OrderBy(r => Vector2.Distance(corner, r.cells.center)).First();
            start.type = DungeonRoomType.Start;

            int[,] distance = BfsDistances(Vector2Int.FloorToInt(start.cells.center));
            foreach (GeneratedRoom room in level.rooms)
            {
                Vector2Int c = Vector2Int.FloorToInt(room.cells.center);
                room.pathDistanceFromStart = distance[c.x, c.y] < 0 ? int.MaxValue : distance[c.x, c.y];
            }

            List<GeneratedRoom> reachable = level.rooms.Where(r => r.pathDistanceFromStart != int.MaxValue).ToList();
            if (reachable.Count < level.rooms.Count)
            {
                Debug.LogWarning($"[DungeonGenerator] {level.rooms.Count - reachable.Count} sala(s) inalcançável(is) — ignoradas para Exit/Boss.");
            }

            GeneratedRoom farthest = reachable.Where(r => r != start).OrderByDescending(r => r.pathDistanceFromStart).FirstOrDefault();
            if (farthest != null)
            {
                farthest.type = level.isFinalLevel ? DungeonRoomType.Boss : DungeonRoomType.Exit;
            }

            if (createSafeZone && farthest != null)
            {
                float half = farthest.pathDistanceFromStart * 0.5f;
                GeneratedRoom safe = reachable
                    .Where(r => r.type == DungeonRoomType.Normal)
                    .OrderBy(r => Mathf.Abs(r.pathDistanceFromStart - half))
                    .FirstOrDefault();
                if (safe != null) safe.type = DungeonRoomType.SafeZone;
            }

            List<GeneratedRoom> normals = reachable.Where(r => r.type == DungeonRoomType.Normal).ToList();
            Shuffle(normals);
            for (int i = 0; i < treasureRoomsPerLevel && i < normals.Count - 1; i++)
            {
                normals[i].type = DungeonRoomType.Treasure;
            }
        }

        private int[,] BfsDistances(Vector2Int from)
        {
            var dist = new int[gridWidth, gridHeight];
            for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                dist[x, y] = -1;

            var queue = new Queue<Vector2Int>();
            dist[from.x, from.y] = 0;
            queue.Enqueue(from);
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                foreach (Vector2Int d in dirs)
                {
                    Vector2Int n = c + d;
                    if (!IsAlive(n.x, n.y) || dist[n.x, n.y] >= 0) continue;
                    dist[n.x, n.y] = dist[c.x, c.y] + 1;
                    queue.Enqueue(n);
                }
            }

            return dist;
        }

        private void PlaceFloor(GeneratedLevel level)
        {
            float cs = config.cellSize;
            Transform colliderRoot = GetCategory("Colliders");

            for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                if (!grid[x, y]) continue;

                Vector3 pos = CellToWorld(x, y);
                GeneratedRoom room = roomId[x, y] >= 0 ? level.rooms[roomId[x, y]] : null;

                bool canHaveHole = room != null
                    && (room.type == DungeonRoomType.Normal || room.type == DungeonRoomType.Treasure)
                    && IsRoomInterior(room, x, y);

                GameObject floorPrefab = null;
                if (canHaveHole && Chance(config.floorHoleChance))
                {
                    floorPrefab = Pick(config.floorHolePrefabs);
                    if (floorPrefab != null)
                    {
                        blocked[x, y] = true;
                        CreateBoxCollider(colliderRoot, "HoleBlocker", pos + Vector3.up * 1f, new Vector3(cs * 0.9f, 2f, cs * 0.9f), Quaternion.identity);
                    }
                }

                if (floorPrefab == null && room != null && room.type == DungeonRoomType.Treasure)
                {
                    floorPrefab = Pick(config.floorDecorativePrefabs);
                }

                if (floorPrefab == null) floorPrefab = Pick(config.floorPrefabs);
                if (floorPrefab != null)
                {
                    float yaw = rng.Next(0, 4) * 90f;
                    GameObject tile = SpawnPiece(floorPrefab, pos, yaw, config.floorRotationCorrection, "Floor", 1f);
                    if (tile != null) ScaleHorizontally(tile, config.floorTileOverlap);
                }

                CreateBoxCollider(colliderRoot, "FloorCollider", pos + Vector3.down * 0.1f, new Vector3(cs, 0.2f, cs), Quaternion.identity);
            }
        }

        private void PlaceWallsAndDoors()
        {
            float half = config.cellSize * 0.5f;
            Transform colliderRoot = GetCategory("Colliders");

            for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
            {
                if (!grid[x, y]) continue;
                Vector3 center = CellToWorld(x, y);

                HandleEdge(x, y, 0, 1, 0f, center + new Vector3(0f, 0f, half), colliderRoot);
                HandleEdge(x, y, 1, 0, 90f, center + new Vector3(half, 0f, 0f), colliderRoot);
                HandleEdge(x, y, 0, -1, 180f, center + new Vector3(0f, 0f, -half), colliderRoot);
                HandleEdge(x, y, -1, 0, 270f, center + new Vector3(-half, 0f, 0f), colliderRoot);
            }
        }

        private void HandleEdge(int x, int y, int dx, int dy, float yaw, Vector3 pos, Transform colliderRoot)
        {
            int nx = x + dx, ny = y + dy;
            float cs = config.cellSize;

            if (!IsAlive(nx, ny))
            {
                GameObject prefab = Chance(config.wallWindowChance) ? Pick(config.wallWindowPrefabs) : null;
                if (prefab == null && Chance(config.wallBrokenChance)) prefab = Pick(config.wallBrokenPrefabs);
                if (prefab == null) prefab = Pick(config.wallPrefabs);

                bool facesCamera = dy == -1;
                float heightScale = facesCamera ? config.cameraFacingWallHeightScale : 1f;
                if (prefab != null)
                {
                    GameObject wall = SpawnPiece(prefab, pos, yaw + config.wallRotationOffset, config.wallRotationCorrection, "Walls", heightScale);
                    if (!facesCamera) FitHeight(wall, cs);
                }

                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                float height = config.wallColliderHeight;
                CreateBoxCollider(colliderRoot, "WallCollider", pos + Vector3.up * (height * 0.5f),
                    new Vector3(cs + config.wallColliderThickness, height, config.wallColliderThickness), rot);
                return;
            }

            if (dx < 0 || dy < 0) return;

            int myRoom = roomId[x, y];
            int otherRoom = roomId[nx, ny];
            bool sameRoom = myRoom >= 0 && myRoom == otherRoom;
            bool bothCorridor = myRoom < 0 && otherRoom < 0;
            if (sameRoom || bothCorridor) return;

            if (dx == 0 || !Chance(config.doorwayDoorChance)) return;

            int corridorX = myRoom < 0 ? x : nx;
            int corridorY = myRoom < 0 ? y : ny;
            bool isChokepoint = !IsAlive(corridorX, corridorY + 1) && !IsAlive(corridorX, corridorY - 1);
            if (!isChokepoint) return;

            GameObject door = Pick(config.doorPrefabs);
            if (door != null)
            {
                GameObject doorInstance = SpawnPiece(door, pos, yaw + config.wallRotationOffset, Vector3.zero, "Doors", 1f);
                FitHeight(doorInstance, cs);

                if (doorInstance != null && Application.isPlaying)
                {
                    doorInstance.AddComponent<Subterraneorum.DungeonSystem.DungeonDoor>().Setup(yaw, cs);
                }
            }
        }

        private void PlaceProps(GeneratedLevel level)
        {
            float cs = config.cellSize;
            float half = cs * 0.5f;

            foreach (GeneratedRoom room in level.rooms)
            {
                bool keepClear = room.type == DungeonRoomType.Start || room.type == DungeonRoomType.Exit || room.type == DungeonRoomType.Boss;

                if (!keepClear && Chance(config.propDensity))
                {
                    List<Vector2Int> interior = InteriorCells(room).Where(c => !blocked[c.x, c.y]).ToList();
                    Shuffle(interior);
                    int count = Mathf.Min(rng.Next(1, config.propsPerRoomMax + 1), interior.Count / 3);
                    for (int i = 0; i < count; i++)
                    {
                        GameObject prop = Pick(config.furniturePropPrefabs);
                        if (prop == null) break;

                        Vector2Int cell = interior[i];
                        Vector3 jitter = new Vector3(RandomRange(-0.25f, 0.25f) * cs, 0f, RandomRange(-0.25f, 0.25f) * cs);
                        GameObject instance = SpawnPiece(prop, CellToWorld(cell.x, cell.y) + jitter, rng.Next(0, 4) * 90f, Vector3.zero, "Props", 1f);
                        if (instance != null && !FitsInsideRoom(instance, room))
                        {
                            DestroyPiece(instance);
                            continue;
                        }

                        if (instance != null && AddBoundsCollider(instance, 0.8f))
                        {
                            blocked[cell.x, cell.y] = true;
                        }
                    }
                }

                int decorCount = rng.Next(1, 4);
                for (int i = 0; i < decorCount; i++)
                {
                    GameObject decor = Pick(config.wallDecorPrefabs);
                    if (decor == null) break;
                    if (!TryPickWallSpot(room, out Vector2Int cell, out Vector2Int outward)) break;

                    Vector3 pos = CellToWorld(cell.x, cell.y) + new Vector3(outward.x, 0f, outward.y) * (half * 0.85f);
                    float yaw = Mathf.Atan2(-outward.x, -outward.y) * Mathf.Rad2Deg;
                    GameObject decorInstance = SpawnPiece(decor, pos, yaw, Vector3.zero, "Props", 1f);

                    if (decorInstance != null && (!FitsInsideRoom(decorInstance, room) || TopAbove(decorInstance, cs))) DestroyPiece(decorInstance);
                }

                PlaceTorches(room, half, cs, level.levelIndex);

                foreach (Vector2Int c in InteriorCells(room))
                {
                    if (!blocked[c.x, c.y]) room.freeCells.Add(c);
                }

                if (room.freeCells.Count == 0)
                {
                    for (int x = room.cells.xMin; x < room.cells.xMax; x++)
                    for (int y = room.cells.yMin; y < room.cells.yMax; y++)
                        if (!blocked[x, y]) room.freeCells.Add(new Vector2Int(x, y));
                }
            }
        }

        private static void ScaleHorizontally(GameObject instance, float factor)
        {
            if (Mathf.Approximately(factor, 1f)) return;

            Transform t = instance.transform;
            Vector3 localUp = Quaternion.Inverse(t.rotation) * Vector3.up;
            Vector3 abs = new Vector3(Mathf.Abs(localUp.x), Mathf.Abs(localUp.y), Mathf.Abs(localUp.z));
            Vector3 scale = t.localScale;
            if (abs.x >= abs.y && abs.x >= abs.z) { scale.y *= factor; scale.z *= factor; }
            else if (abs.y >= abs.z) { scale.x *= factor; scale.z *= factor; }
            else { scale.x *= factor; scale.y *= factor; }
            t.localScale = scale;
        }

        private void PlaceTorches(GeneratedRoom room, float half, float cs, int levelIndex)
        {
            if (config.torchPrefab == null) return;

            int min = Mathf.Min(config.torchesPerRoomMin, config.torchesPerRoomMax);
            int max = Mathf.Max(config.torchesPerRoomMin, config.torchesPerRoomMax);
            int count = rng.Next(min, max + 1) + (levelIndex > 0 ? config.extraTorchesUnderground : 0);
            var used = new HashSet<Vector2Int>();

            for (int i = 0, placed = 0; i < count * 3 && placed < count; i++)
            {
                if (!TryPickWallSpot(room, out Vector2Int cell, out Vector2Int outward) || !used.Add(cell)) continue;

                Vector3 pos = CellToWorld(cell.x, cell.y) + new Vector3(outward.x, 0f, outward.y) * (half * 0.85f);
                float yaw = Mathf.Atan2(-outward.x, -outward.y) * Mathf.Rad2Deg;
                GameObject torch = SpawnPiece(config.torchPrefab, pos, yaw, Vector3.zero, "Lights", 1f);
                if (torch == null) continue;
                if (!FitsInsideRoom(torch, room) || TopAbove(torch, cs)) DestroyPiece(torch);
                else placed++;
            }
        }

        private static List<Renderer> SolidRenderers(GameObject instance)
        {
            var list = new List<Renderer>();
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
            {
                if (!(r is ParticleSystemRenderer)) list.Add(r);
            }

            return list;
        }

        private static bool FitsInsideRoom(GameObject instance, GeneratedRoom room)
        {
            List<Renderer> renderers = SolidRenderers(instance);
            if (renderers.Count == 0) return true;

            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

            const float margin = 0.35f;
            Bounds room2D = room.worldBounds;
            return b.min.x >= room2D.min.x - margin && b.max.x <= room2D.max.x + margin
                && b.min.z >= room2D.min.z - margin && b.max.z <= room2D.max.z + margin;
        }

        private static bool TopAbove(GameObject instance, float height)
        {
            foreach (Renderer r in SolidRenderers(instance))
            {
                if (r.bounds.max.y > height + 0.2f) return true;
            }

            return false;
        }

        private static void DestroyPiece(GameObject instance)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }

        private bool TryPickWallSpot(GeneratedRoom room, out Vector2Int cell, out Vector2Int outward)
        {
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.left, Vector2Int.right };
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int x = rng.Next(room.cells.xMin, room.cells.xMax);
                int y = rng.Next(room.cells.yMin, room.cells.yMax);
                Vector2Int d = dirs[rng.Next(dirs.Length)];
                if (!IsAlive(x + d.x, y + d.y))
                {
                    cell = new Vector2Int(x, y);
                    outward = d;
                    return true;
                }
            }

            cell = default;
            outward = default;
            return false;
        }

        private void PlaceRoomMarkers(GeneratedLevel level)
        {
            Transform parent = GetCategory("RoomMarkers");
            foreach (GeneratedRoom room in level.rooms)
            {
                var go = new GameObject($"Room_{room.id}_{room.type}");
                go.layer = TriggerLayer;
                go.transform.SetParent(parent, false);
                go.transform.position = room.worldBounds.center;

                BoxCollider trigger = go.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(room.worldBounds.size.x - 0.5f, 4f, room.worldBounds.size.z - 0.5f);

                DungeonRoomMarker marker = go.AddComponent<DungeonRoomMarker>();
                marker.roomType = room.type;
                marker.bounds = room.cells;
                marker.roomId = room.id;
                room.marker = marker;
            }
        }

        private void PlaceTransitions(GeneratedLevel level)
        {
            GeneratedRoom exit = level.ExitRoom;
            if (exit != null)
            {
                GameObject stairs = Pick(config.stairsPrefabs);
                if (stairs != null) SpawnPiece(stairs, exit.center, 0f, Vector3.zero, "Transitions", 1f);
            }

            GeneratedRoom start = level.StartRoom;
            if (start != null && level.levelIndex > 0 && config.trapdoorPrefab != null)
            {
                SpawnPiece(config.trapdoorPrefab, start.center, 0f, Vector3.zero, "Transitions", 1f);
            }
        }

        private GameObject SpawnPiece(GameObject prefab, Vector3 pos, float yaw, Vector3 extraCorrection, string category, float heightScale)
        {
            if (prefab == null) return null;

            Transform parent = GetCategory(category);
            GameObject instance;
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
            else
            {
                instance = Instantiate(prefab, parent);
            }
#else
            instance = Instantiate(prefab, parent);
#endif
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(extraCorrection) * prefab.transform.localRotation;
            instance.transform.SetPositionAndRotation(pos, rotation);

            Vector3 scale = prefab.transform.localScale * config.pieceScale;
            if (!Mathf.Approximately(heightScale, 1f))
            {
                Vector3 localUp = Quaternion.Inverse(rotation) * Vector3.up;
                Vector3 abs = new Vector3(Mathf.Abs(localUp.x), Mathf.Abs(localUp.y), Mathf.Abs(localUp.z));
                if (abs.x >= abs.y && abs.x >= abs.z) scale.x *= heightScale;
                else if (abs.y >= abs.z) scale.y *= heightScale;
                else scale.z *= heightScale;
            }

            instance.transform.localScale = scale;
            SetLayerRecursively(instance, environmentLayer);

            foreach (Collider c in instance.GetComponentsInChildren<Collider>())
            {
                SafeDestroy(c);
            }

            return instance;
        }

        private static void FitHeight(GameObject instance, float maxHeight)
        {
            if (instance == null) return;
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y <= maxHeight * 1.05f) return;

            float factor = maxHeight / b.size.y;
            float floorY = b.min.y;
            Vector3 localUp = Quaternion.Inverse(instance.transform.rotation) * Vector3.up;
            Vector3 abs = new Vector3(Mathf.Abs(localUp.x), Mathf.Abs(localUp.y), Mathf.Abs(localUp.z));
            Vector3 scale = instance.transform.localScale;
            if (abs.x >= abs.y && abs.x >= abs.z) scale.x *= factor;
            else if (abs.y >= abs.z) scale.y *= factor;
            else scale.z *= factor;
            instance.transform.localScale = scale;

            b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            instance.transform.position += Vector3.up * (floorY - b.min.y);
        }

        private void CreateBoxCollider(Transform parent, string name, Vector3 position, Vector3 size, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.layer = environmentLayer;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.isStatic = !Application.isPlaying;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
        }

        private bool AddBoundsCollider(GameObject instance, float footprintScale)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.y < 0.3f) return false;

            var go = new GameObject("PropCollider");
            go.layer = environmentLayer;
            go.transform.SetParent(instance.transform.parent, false);
            go.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(bounds.size.x * footprintScale, bounds.size.y, bounds.size.z * footprintScale);
            return true;
        }

        private Transform GetCategory(string categoryName)
        {
            if (categoryRoots.TryGetValue(categoryName, out Transform t) && t != null) return t;

            var go = new GameObject(categoryName);
            go.transform.SetParent(levelRoot, false);
            categoryRoots[categoryName] = go.transform;
            return go.transform;
        }

        private bool InGrid(int x, int y) => x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;
        private bool IsAlive(int x, int y) => InGrid(x, y) && grid[x, y];

        private static bool IsRoomInterior(GeneratedRoom room, int x, int y)
        {
            return x > room.cells.xMin && x < room.cells.xMax - 1 && y > room.cells.yMin && y < room.cells.yMax - 1;
        }

        private static IEnumerable<Vector2Int> InteriorCells(GeneratedRoom room)
        {
            for (int x = room.cells.xMin + 1; x < room.cells.xMax - 1; x++)
            for (int y = room.cells.yMin + 1; y < room.cells.yMax - 1; y++)
                yield return new Vector2Int(x, y);
        }

        private bool Chance(float probability) => rng.NextDouble() < probability;
        private float RandomRange(float min, float max) => (float)(min + rng.NextDouble() * (max - min));

        private GameObject Pick(List<GameObject> list)
        {
            if (list == null || list.Count == 0) return null;
            GameObject picked = list[rng.Next(list.Count)];
            return picked;
        }

        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(obj);
                return;
            }
#endif
            Destroy(obj);
        }
    }
}
