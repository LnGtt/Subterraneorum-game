using System.Collections.Generic;
using ProceduralRuins;
using Subterraneorum.DungeonSystem;
using UnityEngine;
using UnityEngine.UI;

public class Minimap : MonoBehaviour
{
    private const float Size = 360f;
    private const float RefreshInterval = 0.1f;

    private RectTransform panel;
    private RectTransform mapArea;
    private RawImage mapImage;
    private Texture2D texture;
    private DungeonGenerator generator;
    private DungeonManager dungeon;
    private Vector2Int gridSize;
    private float nextRefresh;
    private bool dirty;

    private readonly List<Image> playerDots = new List<Image>();
    private readonly List<Image> enemyDots = new List<Image>();
    private Image bossDot;

    private static readonly Color Empty = new Color(0f, 0f, 0f, 0f);
    private static readonly Color Corridor = new Color(0.55f, 0.53f, 0.48f, 1f);
    private static readonly Color RoomNormal = new Color(0.68f, 0.65f, 0.58f, 1f);
    private static readonly Color RoomStart = new Color(0.45f, 0.75f, 0.45f, 1f);
    private static readonly Color RoomExit = new Color(0.95f, 0.85f, 0.35f, 1f);
    private static readonly Color RoomBoss = new Color(0.85f, 0.3f, 0.25f, 1f);
    private static readonly Color RoomSafe = new Color(0.45f, 0.65f, 0.95f, 1f);
    private static readonly Color RoomTreasure = new Color(0.9f, 0.7f, 0.3f, 1f);

    public void Build(RectTransform canvas)
    {
        UiTheme theme = UiTheme.Current;
        Image frame = UiFactory.Panel(canvas, "Minimap", theme != null ? theme.panelDark : null, new Color(1f, 1f, 1f, 0.95f));
        panel = frame.rectTransform;
        UiFactory.Place(panel, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(Size + 24f, Size + 60f), new Vector2(1f, 1f));

        Text title = UiFactory.Label(panel, "Title", "MAPA", 26, UiFactory.Gold, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(Size, 30f), new Vector2(0.5f, 1f));

        Image background = UiFactory.Box(panel, "Background", new Color(0.05f, 0.04f, 0.06f, 0.92f));
        UiFactory.Region(background.rectTransform, Vector2.zero, Vector2.one);
        background.rectTransform.offsetMin = new Vector2(12f, 12f);
        background.rectTransform.offsetMax = new Vector2(-12f, -46f);

        mapArea = UiFactory.Empty(panel, "Map");
        UiFactory.Region(mapArea, Vector2.zero, Vector2.one);
        mapArea.offsetMin = new Vector2(12f, 12f);
        mapArea.offsetMax = new Vector2(-12f, -46f);
        var imageGo = new GameObject("Rooms", typeof(RectTransform), typeof(RawImage));
        imageGo.transform.SetParent(mapArea, false);
        mapImage = imageGo.GetComponent<RawImage>();
        mapImage.raycastTarget = false;
        UiFactory.Stretch(mapImage.rectTransform);

        bossDot = Dot("Boss", new Color(1f, 0.15f, 0.1f), 18f);
        bossDot.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        DungeonManager.OnDungeonGenerationCompleted += Rebuild;
        DungeonRoomMarker.OnFirstPlayerEntered += HandleRoomVisited;
    }

    private void OnDisable()
    {
        DungeonManager.OnDungeonGenerationCompleted -= Rebuild;
        DungeonRoomMarker.OnFirstPlayerEntered -= HandleRoomVisited;
    }

    private void OnDestroy()
    {
        if (texture != null) Destroy(texture);
    }

    private void HandleRoomVisited(DungeonRoomMarker marker, GameObject player) => dirty = true;

    private void Rebuild()
    {
        if (generator == null) generator = FindAnyObjectByType<DungeonGenerator>();
        if (dungeon == null) dungeon = FindAnyObjectByType<DungeonManager>();
        if (generator == null || dungeon == null || dungeon.CurrentLevel == null || panel == null) return;

        gridSize = generator.GridSize;
        if (gridSize.x <= 0 || gridSize.y <= 0) return;

        if (texture == null || texture.width != gridSize.x || texture.height != gridSize.y)
        {
            if (texture != null) Destroy(texture);
            texture = new Texture2D(gridSize.x, gridSize.y, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            mapImage.texture = texture;
        }

        float aspect = gridSize.x / (float)gridSize.y;
        mapImage.rectTransform.anchorMin = aspect >= 1f ? new Vector2(0f, 0.5f - 0.5f / aspect) : new Vector2(0.5f - 0.5f * aspect, 0f);
        mapImage.rectTransform.anchorMax = aspect >= 1f ? new Vector2(1f, 0.5f + 0.5f / aspect) : new Vector2(0.5f + 0.5f * aspect, 1f);
        mapImage.rectTransform.offsetMin = mapImage.rectTransform.offsetMax = Vector2.zero;

        dirty = true;
        panel.gameObject.SetActive(true);
    }

    private void Paint()
    {
        dirty = false;
        GeneratedLevel level = dungeon.CurrentLevel;
        var rooms = new Dictionary<int, GeneratedRoom>();
        foreach (GeneratedRoom room in level.rooms) rooms[room.id] = room;

        var pixels = new Color32[gridSize.x * gridSize.y];
        for (int y = 0; y < gridSize.y; y++)
        for (int x = 0; x < gridSize.x; x++)
        {
            Color color = Empty;
            if (generator.IsFloor(x, y))
            {
                int id = generator.RoomIdAt(x, y);
                if (id >= 0 && rooms.TryGetValue(id, out GeneratedRoom room))
                {
                    color = RoomColor(room.type);
                    bool visited = room.marker != null && room.marker.Visited;
                    if (!visited && room.type != DungeonRoomType.Start) color = Color.Lerp(color, Color.black, 0.55f);
                }
                else
                {
                    color = Corridor;
                }
            }

            pixels[y * gridSize.x + x] = color;
        }

        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    private static Color RoomColor(DungeonRoomType type)
    {
        switch (type)
        {
            case DungeonRoomType.Start: return RoomStart;
            case DungeonRoomType.Exit: return RoomExit;
            case DungeonRoomType.Boss: return RoomBoss;
            case DungeonRoomType.SafeZone: return RoomSafe;
            case DungeonRoomType.Treasure: return RoomTreasure;
            default: return RoomNormal;
        }
    }

    private void Update()
    {
        if (panel == null || !panel.gameObject.activeSelf || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        if (dirty) Paint();
        UpdateMarkers();
    }

    private void UpdateMarkers()
    {
        int p = 0;
        foreach (PlayerStatus player in PlayerRegistry.Players)
        {
            if (player == null) continue;
            if (p >= playerDots.Count)
            {
                Image dot = Dot("Player", Color.white, 16f);
                Outline outline = dot.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(3f, -3f);
                Outline outline2 = dot.gameObject.AddComponent<Outline>();
                outline2.effectColor = Color.white;
                outline2.effectDistance = new Vector2(-3f, 3f);
                playerDots.Add(dot);
            }
            PlayerIdentity identity = player.GetComponent<PlayerIdentity>();
            Color color = identity != null && identity.Class != null ? identity.Class.color : Color.white;
            Place(playerDots[p], player.transform.position, player.IsDead ? Color.gray : color);
            p++;
        }

        for (int i = p; i < playerDots.Count; i++) playerDots[i].gameObject.SetActive(false);

        int e = 0;
        bool bossShown = false;
        IReadOnlyList<EnemyStatus> enemies = EnemyStatus.All;
        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyStatus enemy = enemies[i];
            if (enemy == null || enemy.IsDead) continue;
            if (enemy.IsBoss)
            {
                Place(bossDot, enemy.transform.position, bossDot.color);
                bossShown = true;
                continue;
            }

            if (e >= enemyDots.Count) enemyDots.Add(Dot("Enemy", new Color(1f, 0.3f, 0.25f), 7f));
            Place(enemyDots[e], enemy.transform.position, enemyDots[e].color);
            e++;
        }

        for (int i = e; i < enemyDots.Count; i++) enemyDots[i].gameObject.SetActive(false);
        if (!bossShown) bossDot.gameObject.SetActive(false);
        bossDot.transform.SetAsLastSibling();
        foreach (Image dot in playerDots) dot.transform.SetAsLastSibling();
    }

    private void Place(Image dot, Vector3 world, Color color)
    {
        float cell = generator.CellSize;
        Vector3 local = world - generator.transform.position;
        float u = (local.x / cell + 0.5f) / gridSize.x;
        float v = (local.z / cell + 0.5f) / gridSize.y;
        RectTransform map = mapImage.rectTransform;
        Vector2 min = map.anchorMin, max = map.anchorMax;
        Vector2 anchor = new Vector2(Mathf.Lerp(min.x, max.x, Mathf.Clamp01(u)), Mathf.Lerp(min.y, max.y, Mathf.Clamp01(v)));
        dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = anchor;
        dot.rectTransform.anchoredPosition = Vector2.zero;
        dot.color = color;
        if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
    }

    private Image Dot(string name, Color color, float size)
    {
        Image dot = UiFactory.Box(mapArea, name, color);
        dot.rectTransform.sizeDelta = new Vector2(size, size);
        dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        dot.gameObject.SetActive(false);
        return dot;
    }
}
