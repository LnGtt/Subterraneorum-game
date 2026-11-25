using System;
using ProceduralRuins;
using Subterraneorum.DungeonSystem;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LevelAtmosphere : MonoBehaviour
{
    [Serializable]
    public class Preset
    {
        public Color skyAmbient = Color.gray;
        public Color equatorAmbient = Color.gray;
        public Color groundAmbient = Color.gray;
        public Color fogColor = Color.gray;
        [Min(0f)] public float fogDensity = 0.02f;
        public Color sunColor = Color.white;
        [Min(0f)] public float sunIntensity = 1f;
        [Min(0f)] public float torchMultiplier = 1f;
        public bool showSurfaceGround;

        [Header("Correção de cor (pós-processamento)")]
        [Tooltip("Multiplica a cor da imagem inteira. Branco = sem efeito.")]
        public Color colorFilter = Color.white;
        [Tooltip("Temperatura da imagem: positivo = mais quente/laranja.")]
        [Range(-100f, 100f)] public float temperature;
        [Tooltip("Exposição extra (EV): positivo = imagem um pouco mais clara.")]
        [Range(-1f, 1f)] public float postExposure;
    }

    [SerializeField] private Light sun;
    [SerializeField] private Camera targetCamera;
    [Tooltip("Chão de grama grande em volta da dungeon (só aparece na superfície). Sem colisor, fora do NavMesh.")]
    [SerializeField] private GameObject surfaceGround;

    [Header("Nível 1 — superfície (planície)")]
    [SerializeField] private Preset surface = DefaultSurface();

    [Header("Níveis 2+ — subsolo (luz ambiente fraca + tochas)")]
    [SerializeField] private Preset underground = DefaultUnderground();

    [Tooltip("Quanto cada andar abaixo do 2º escurece a luz ambiente e a direcional (0,08 = 8%).")]
    [SerializeField, Range(0f, 0.3f)] private float extraDarkeningPerLevel = 0.08f;
    [Tooltip("Limite do escurecimento extra, para o fundo nunca ficar preto demais.")]
    [SerializeField, Range(0.3f, 1f)] private float minimumBrightness = 0.75f;

    private static Preset DefaultSurface() => new Preset
    {
        skyAmbient = new Color(0.62f, 0.74f, 0.62f),
        equatorAmbient = new Color(0.42f, 0.55f, 0.36f),
        groundAmbient = new Color(0.22f, 0.30f, 0.17f),
        fogColor = new Color(0.56f, 0.70f, 0.52f),
        fogDensity = 0.012f,
        sunColor = new Color(1f, 0.96f, 0.84f),
        sunIntensity = 1.15f,
        torchMultiplier = 0.45f,
        showSurfaceGround = true,
    };

    private static Preset DefaultUnderground() => new Preset
    {
        skyAmbient = new Color(0.62f, 0.45f, 0.30f),
        equatorAmbient = new Color(0.50f, 0.35f, 0.22f),
        groundAmbient = new Color(0.27f, 0.18f, 0.11f),
        fogColor = new Color(0.13f, 0.075f, 0.04f),
        fogDensity = 0.02f,
        sunColor = new Color(1f, 0.74f, 0.48f),
        sunIntensity = 0.6f,
        torchMultiplier = 1.2f,
        showSurfaceGround = false,
        colorFilter = new Color(1f, 0.9f, 0.76f),
        temperature = 18f,
        postExposure = 0.15f,
    };

    [ContextMenu("Restaurar clima padrão")]
    public void ResetPresets()
    {
        surface = DefaultSurface();
        underground = DefaultUnderground();
        extraDarkeningPerLevel = 0.08f;
        minimumBrightness = 0.75f;
    }

    [Header("Floresta em volta da dungeon (só na superfície)")]
    [Tooltip("Config de onde vêm as árvores e arbustos (lista Vegetação) e a escala das peças.")]
    [SerializeField] private DungeonGenerationConfig vegetationConfig;
    [SerializeField, Min(0)] private int vegetationCount = 90;
    [Tooltip("Distância mínima (m) entre a vegetação e as salas.")]
    [SerializeField, Min(0f)] private float innerMargin = 5f;
    [Tooltip("Folga maior do lado da câmera (sul), para árvores não taparem os jogadores.")]
    [SerializeField, Min(0f)] private float cameraSideMargin = 14f;
    [Tooltip("Até onde (m, além das salas) a vegetação se espalha.")]
    [SerializeField, Min(1f)] private float outerDistance = 45f;

    private Transform vegetationRoot;
    private DungeonManager manager;

    private Volume gradingVolume;
    private VolumeProfile gradingProfile;
    private ColorAdjustments colorAdjustments;
    private WhiteBalance whiteBalance;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (sun == null) sun = FindDirectionalLight();
    }

    private void OnEnable()
    {
        DungeonManager.OnLevelStarted += Apply;
    }

    private void OnDisable()
    {
        DungeonManager.OnLevelStarted -= Apply;
    }

    private void Start()
    {
        manager = FindAnyObjectByType<DungeonManager>();
        Apply(manager != null ? manager.CurrentLevelIndex : 0);
    }

    public void Apply(int levelIndex)
    {
        Preset preset = levelIndex <= 0 ? surface : underground;
        float brightness = levelIndex <= 1 ? 1f : Mathf.Max(minimumBrightness, 1f - extraDarkeningPerLevel * (levelIndex - 1));

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = preset.skyAmbient * brightness;
        RenderSettings.ambientEquatorColor = preset.equatorAmbient * brightness;
        RenderSettings.ambientGroundColor = preset.groundAmbient * brightness;

        RenderSettings.fog = preset.fogDensity > 0f;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = preset.fogColor * brightness;
        RenderSettings.fogDensity = preset.fogDensity;

        if (sun != null)
        {
            sun.color = preset.sunColor;
            sun.intensity = preset.sunIntensity * brightness;
        }

        if (targetCamera != null)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = preset.fogColor * brightness;
        }

        if (surfaceGround != null) surfaceGround.SetActive(preset.showSurfaceGround);
        LightFlicker.GlobalMultiplier = preset.torchMultiplier;
        ApplyColorGrading(preset);

        PlaceVegetation(preset.showSurfaceGround);
    }

    private void ApplyColorGrading(Preset preset)
    {
        if (gradingVolume == null)
        {
            var go = new GameObject("AtmosphereColorGrading");
            go.transform.SetParent(transform, false);
            gradingVolume = go.AddComponent<Volume>();
            gradingVolume.isGlobal = true;
            gradingVolume.priority = 50f;

            gradingProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            gradingProfile.name = "AtmosphereColorGrading (runtime)";
            colorAdjustments = gradingProfile.Add<ColorAdjustments>();
            whiteBalance = gradingProfile.Add<WhiteBalance>();
            gradingVolume.sharedProfile = gradingProfile;
        }

        colorAdjustments.colorFilter.Override(preset.colorFilter);
        colorAdjustments.postExposure.Override(preset.postExposure);
        whiteBalance.temperature.Override(preset.temperature);
    }

    private void OnDestroy()
    {
        if (gradingProfile != null) Destroy(gradingProfile);
    }

    private void PlaceVegetation(bool show)
    {
        if (vegetationRoot != null) Destroy(vegetationRoot.gameObject);
        vegetationRoot = null;

        if (!show || vegetationConfig == null || vegetationConfig.vegetationPropPrefabs.Count == 0 || vegetationCount <= 0) return;

        if (manager == null) manager = FindAnyObjectByType<DungeonManager>();
        GeneratedLevel level = manager != null ? manager.CurrentLevel : null;
        if (level == null || level.rooms.Count == 0) return;

        Bounds dungeon = level.rooms[0].worldBounds;
        foreach (GeneratedRoom room in level.rooms) dungeon.Encapsulate(room.worldBounds);

        float groundY = surfaceGround != null ? surfaceGround.transform.position.y : dungeon.min.y;
        vegetationRoot = new GameObject("SurfaceVegetation").transform;
        vegetationRoot.SetParent(transform, false);

        var rng = new System.Random(level.seed ^ 0x5EED);
        float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        int placed = 0;
        for (int attempt = 0; attempt < vegetationCount * 6 && placed < vegetationCount; attempt++)
        {
            float x = Range(dungeon.min.x - outerDistance, dungeon.max.x + outerDistance);
            float z = Range(dungeon.min.z - outerDistance, dungeon.max.z + outerDistance);
            bool nearRooms = x > dungeon.min.x - innerMargin && x < dungeon.max.x + innerMargin
                && z > dungeon.min.z - cameraSideMargin && z < dungeon.max.z + innerMargin;
            if (nearRooms) continue;

            GameObject prefab = vegetationConfig.vegetationPropPrefabs[rng.Next(vegetationConfig.vegetationPropPrefabs.Count)];
            if (prefab == null) continue;

            Quaternion rotation = Quaternion.Euler(0f, Range(0f, 360f), 0f) * prefab.transform.localRotation;
            GameObject plant = Instantiate(prefab, new Vector3(x, groundY, z), rotation, vegetationRoot);
            plant.transform.localScale = prefab.transform.localScale * vegetationConfig.pieceScale * Range(0.8f, 1.3f);
            foreach (Collider c in plant.GetComponentsInChildren<Collider>()) Destroy(c);
            placed++;
        }
    }

    private static Light FindDirectionalLight()
    {
        foreach (Light light in FindObjectsByType<Light>())
        {
            if (light.type == LightType.Directional) return light;
        }

        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        LightFlicker.GlobalMultiplier = 1f;
    }
}
