using System.Collections.Generic;
using Subterraneorum.DungeonSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MusicDirector : MonoBehaviour
{
    public const string MusicResourcesPath = "Subterraneorum/Audio/Music/";
    private const string MusicVolumeKey = "Subterraneorum.MusicVolume";
    private const float BossIntroLength = 6.4f;
    private const float EngageRadius = 20f;

    private sealed class Layer
    {
        public AudioSource source;
        public AudioLowPassFilter filter;
        public float volume;
        public float target;
        public float fadeIn = 1.5f;
        public float fadeOut = 1.5f;
        public float gain = 1f;
        public bool keepAlive;
    }

    private static MusicDirector instance;
    private static float musicVolume = -1f;

    public static bool Muted { get; set; }

    public static float MusicVolume
    {
        get
        {
            if (musicVolume < 0f) musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 0.8f);
            return musicVolume;
        }
        set
        {
            musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
        }
    }

    public static string CurrentExplorationTrack => instance != null ? instance.exploreClipName : null;
    public static float CombatLevel => instance != null ? instance.combatLevel : 0f;

    private readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    private readonly List<AudioSource> dying = new List<AudioSource>();
    private Layer menu, lobby, explore, combat, sanctuary, minigame, cards, bossIntro, boss, stinger, after;
    private readonly List<Layer> layers = new List<Layer>();

    private bool inGameScene;
    private bool runStarted;
    private bool bossActive;
    private bool runEnded;
    private string exploreClipName;
    private float combatLevel;
    private float lastEngagedAt = -100f;
    private float nextScan;
    private int engagedCount;
    private float afterStartsAt = -1f;
    private float lowpass = 22000f;

    private SafeZone[] safeZones = System.Array.Empty<SafeZone>();
    private SafeZoneMinigame[] minigames = System.Array.Empty<SafeZoneMinigame>();
    private CardDraftManager cardDraft;
    private PauseMenu pauseMenu;
    private readonly Dictionary<EnemyStatus, EnemyBase> enemyBrains = new Dictionary<EnemyStatus, EnemyBase>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("MusicDirector");
        DontDestroyOnLoad(go);
        go.AddComponent<MusicDirector>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        menu = CreateLayer("Menu", 2f, 1.5f);
        lobby = CreateLayer("Lobby", 1.5f, 1.5f);
        explore = CreateLayer("Explore", 2.5f, 1.5f);
        combat = CreateLayer("Combat", 0.9f, 3.5f);
        sanctuary = CreateLayer("Sanctuary", 2.5f, 1.5f);
        minigame = CreateLayer("Minigame", 0.6f, 1.2f);
        cards = CreateLayer("Cards", 0.8f, 1.0f);
        bossIntro = CreateLayer("BossIntro", 0.05f, 1.0f);
        boss = CreateLayer("Boss", 0.05f, 1.5f);
        stinger = CreateLayer("Stinger", 0.05f, 1.0f);
        after = CreateLayer("After", 3f, 2f);
        explore.keepAlive = true;
        combat.keepAlive = true;
        combat.gain = 0.85f;
        sanctuary.gain = 0.9f;
        cards.gain = 0.9f;
        after.gain = 0.5f;
    }

    private Layer CreateLayer(string name, float fadeIn, float fadeOut)
    {
        var go = new GameObject("Music_" + name);
        go.transform.SetParent(transform, false);
        var layer = new Layer
        {
            source = go.AddComponent<AudioSource>(),
            filter = go.AddComponent<AudioLowPassFilter>(),
            fadeIn = fadeIn,
            fadeOut = fadeOut,
        };
        layer.source.playOnAwake = false;
        layer.source.loop = true;
        layer.source.spatialBlend = 0f;
        layer.source.priority = 0;
        layer.source.ignoreListenerPause = true;
        layer.source.bypassReverbZones = true;
        layer.source.volume = 0f;
        layer.filter.cutoffFrequency = 22000f;
        layers.Add(layer);
        return layer;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        DungeonManager.OnLevelStarted += HandleLevelStarted;
        DungeonManager.OnBossSpawned += HandleBossSpawned;
        DungeonManager.OnVictory += HandleVictory;
        DungeonManager.OnDefeat += HandleDefeat;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        DungeonManager.OnLevelStarted -= HandleLevelStarted;
        DungeonManager.OnBossSpawned -= HandleBossSpawned;
        DungeonManager.OnVictory -= HandleVictory;
        DungeonManager.OnDefeat -= HandleDefeat;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private AudioClip Clip(string name)
    {
        if (cache.TryGetValue(name, out AudioClip clip)) return clip;
        clip = Resources.Load<AudioClip>(MusicResourcesPath + name);
        if (clip == null) Debug.LogWarning($"[MusicDirector] Música não encontrada: Resources/{MusicResourcesPath}{name}");
        cache[name] = clip;
        return clip;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;

        inGameScene = scene.name == MainMenuController.GameScene;
        runStarted = false;
        bossActive = false;
        runEnded = false;
        afterStartsAt = -1f;
        combatLevel = 0f;
        engagedCount = 0;
        enemyBrains.Clear();
        safeZones = System.Array.Empty<SafeZone>();
        minigames = System.Array.Empty<SafeZoneMinigame>();
        cardDraft = inGameScene ? FindAnyObjectByType<CardDraftManager>() : null;
        pauseMenu = inGameScene ? FindAnyObjectByType<PauseMenu>() : null;

        Retire(explore);
        Retire(combat);
        exploreClipName = null;
        Stop(bossIntro);
        Stop(boss);
        Stop(stinger);
        Stop(after);

        if (inGameScene) Ensure(lobby, "Music_Lobby");
        else Ensure(menu, "Music_Menu");
    }

    private void HandleLevelStarted(int levelIndex)
    {
        runStarted = true;
        bossActive = false;
        DungeonManager dungeon = FindAnyObjectByType<DungeonManager>();
        int count = dungeon != null ? dungeon.LevelCount : 3;
        string track = levelIndex <= 0 ? "Music_Surface" : levelIndex >= count - 1 ? "Music_Abyss" : "Music_Depths";

        safeZones = FindObjectsByType<SafeZone>(FindObjectsInactive.Exclude);
        minigames = FindObjectsByType<SafeZoneMinigame>(FindObjectsInactive.Exclude);
        if (cardDraft == null) cardDraft = FindAnyObjectByType<CardDraftManager>();
        if (pauseMenu == null) pauseMenu = FindAnyObjectByType<PauseMenu>();
        enemyBrains.Clear();

        if (track == exploreClipName && explore.source.isPlaying) return;

        Retire(explore);
        Retire(combat);
        exploreClipName = track;
        AudioClip baseClip = Clip(track);
        AudioClip combatClip = Clip("Music_Combat");
        double start = AudioSettings.dspTime + 0.15;
        if (baseClip != null)
        {
            explore.source.clip = baseClip;
            explore.source.PlayScheduled(start);
        }

        if (combatClip != null)
        {
            combat.source.clip = combatClip;
            combat.source.PlayScheduled(start);
        }

        explore.volume = 0f;
        combat.volume = 0f;
    }

    private void HandleBossSpawned(EnemyStatus status)
    {
        if (bossActive) return;
        bossActive = true;
        AudioClip intro = Clip("Music_BossIntro");
        AudioClip loop = Clip("Music_BossLoop");
        double start = AudioSettings.dspTime + 0.05;
        if (intro != null)
        {
            bossIntro.source.clip = intro;
            bossIntro.source.loop = false;
            bossIntro.source.PlayScheduled(start);
            bossIntro.volume = 1f;
        }

        if (loop != null)
        {
            boss.source.clip = loop;
            boss.source.loop = true;
            boss.source.PlayScheduled(start + (intro != null ? BossIntroLength : 0.0));
            boss.volume = 1f;
        }
    }

    private void HandleVictory() => PlayEnding("Music_Victory", "Music_Sanctuary");

    private void HandleDefeat() => PlayEnding("Music_Defeat", null);

    private void PlayEnding(string stingerName, string afterName)
    {
        runEnded = true;
        AudioClip clip = Clip(stingerName);
        if (clip == null) return;
        stinger.source.clip = clip;
        stinger.source.loop = false;
        stinger.source.PlayScheduled(AudioSettings.dspTime + 0.35);
        stinger.volume = 1f;
        afterStartsAt = afterName != null ? Time.unscaledTime + Mathf.Max(1f, clip.length - 3f) : -1f;
        if (afterName != null)
        {
            after.source.clip = Clip(afterName);
            after.source.loop = true;
        }
    }

    private void Ensure(Layer layer, string clipName)
    {
        AudioClip clip = Clip(clipName);
        if (clip == null) return;
        if (layer.source.clip != clip)
        {
            layer.source.Stop();
            layer.source.clip = clip;
            layer.volume = 0f;
        }

        layer.source.loop = true;
        if (!layer.source.isPlaying) layer.source.Play();
    }

    private void Stop(Layer layer)
    {
        layer.source.Stop();
        layer.volume = 0f;
        layer.target = 0f;
    }

    private void Retire(Layer layer)
    {
        if (layer.source.isPlaying && layer.volume > 0.001f)
        {
            var go = new GameObject("Music_FadingOut");
            go.transform.SetParent(transform, false);
            AudioSource copy = go.AddComponent<AudioSource>();
            copy.clip = layer.source.clip;
            copy.loop = false;
            copy.spatialBlend = 0f;
            copy.priority = 0;
            copy.ignoreListenerPause = true;
            copy.volume = layer.source.volume;
            copy.timeSamples = layer.source.timeSamples;
            copy.Play();
            dying.Add(copy);
        }

        layer.source.Stop();
        layer.volume = 0f;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        ComputeTargets();

        bool paused = pauseMenu != null && pauseMenu.IsOpen;
        lowpass = Mathf.Lerp(lowpass, paused ? 900f : 22000f, 1f - Mathf.Exp(-6f * dt));
        float master = Muted ? 0f : MusicVolume * (paused ? 0.6f : 1f);

        foreach (Layer layer in layers)
        {
            float speed = layer.target > layer.volume ? 1f / Mathf.Max(0.01f, layer.fadeIn) : 1f / Mathf.Max(0.01f, layer.fadeOut);
            layer.volume = Mathf.MoveTowards(layer.volume, layer.target, speed * dt);
            layer.source.volume = layer.volume * layer.gain * master;
            layer.filter.cutoffFrequency = lowpass;

            if (layer.target > 0f && !layer.source.isPlaying && layer.source.clip != null && layer.source.loop && !layer.keepAlive && layer != boss)
            {
                layer.source.Play();
            }
            else if (layer.target <= 0f && layer.volume <= 0f && layer.source.isPlaying && !layer.keepAlive)
            {
                layer.source.Stop();
            }
        }

        for (int i = dying.Count - 1; i >= 0; i--)
        {
            AudioSource source = dying[i];
            if (source == null)
            {
                dying.RemoveAt(i);
                continue;
            }

            source.volume = Mathf.MoveTowards(source.volume, 0f, dt / 1.5f * Mathf.Max(0.05f, master));
            if (source.volume <= 0.0001f || !source.isPlaying)
            {
                Destroy(source.gameObject);
                dying.RemoveAt(i);
            }
        }
    }

    private void ComputeTargets()
    {
        foreach (Layer layer in layers) layer.target = 0f;

        if (!inGameScene)
        {
            menu.target = 1f;
            return;
        }

        if (runEnded)
        {
            stinger.target = 1f;
            if (afterStartsAt > 0f && Time.unscaledTime >= afterStartsAt && after.source.clip != null)
            {
                after.target = 1f;
                if (!after.source.isPlaying) after.source.Play();
            }

            return;
        }

        if (!runStarted)
        {
            lobby.target = 1f;
            return;
        }

        bool cardsOpen = cardDraft != null && cardDraft.IsOpen;
        float duck = cardsOpen ? 0.15f : 1f;
        if (cardsOpen)
        {
            if (cards.source.clip == null) cards.source.clip = Clip("Music_Cards");
            cards.target = 1f;
        }

        if (bossActive)
        {
            bossIntro.target = duck;
            boss.target = duck;
            return;
        }

        UpdateCombatLevel();
        bool playingMinigame = MinigameActive();
        bool resting = !playingMinigame && combatLevel < 0.3f && PartyInSafeZone();

        if (playingMinigame)
        {
            if (minigame.source.clip == null) minigame.source.clip = Clip("Music_Minigame");
            minigame.target = duck;
        }
        else if (resting)
        {
            if (sanctuary.source.clip == null) sanctuary.source.clip = Clip("Music_Sanctuary");
            sanctuary.target = duck;
        }
        else
        {
            explore.target = (1f - 0.25f * combatLevel) * duck;
            combat.target = combatLevel * duck;
        }
    }

    private void UpdateCombatLevel()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.25f;
            engagedCount = CountEngagedEnemies();
            if (engagedCount > 0) lastEngagedAt = Time.unscaledTime;
        }

        float desired = Mathf.Clamp01(engagedCount / 2f);
        float dt = Time.unscaledDeltaTime;
        if (desired > combatLevel) combatLevel = Mathf.MoveTowards(combatLevel, desired, dt / 0.6f);
        else if (Time.unscaledTime - lastEngagedAt > 2.5f) combatLevel = Mathf.MoveTowards(combatLevel, desired, dt / 4f);
    }

    private int CountEngagedEnemies()
    {
        int count = 0;
        IReadOnlyList<EnemyStatus> enemies = EnemyStatus.All;
        IReadOnlyList<PlayerStatus> players = PlayerRegistry.Players;
        float radiusSqr = EngageRadius * EngageRadius;
        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyStatus enemy = enemies[i];
            if (enemy == null || enemy.IsDead) continue;
            if (!enemyBrains.TryGetValue(enemy, out EnemyBase brain))
            {
                brain = enemy.GetComponent<EnemyBase>();
                enemyBrains[enemy] = brain;
            }

            if (brain == null || (brain.CurrentState != EnemyState.Chase && brain.CurrentState != EnemyState.Attack)) continue;

            Vector3 position = enemy.transform.position;
            for (int p = 0; p < players.Count; p++)
            {
                PlayerStatus player = players[p];
                if (player == null || player.IsDead) continue;
                if ((player.transform.position - position).sqrMagnitude <= radiusSqr)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private bool MinigameActive()
    {
        foreach (SafeZoneMinigame game in minigames)
        {
            if (game == null) continue;
            if (game.CurrentState == SafeZoneMinigame.State.Countdown || game.CurrentState == SafeZoneMinigame.State.Playing) return true;
        }

        return false;
    }

    private bool PartyInSafeZone()
    {
        int alive = PlayerRegistry.AliveCount;
        if (alive <= 0) return false;

        int inside = 0;
        foreach (PlayerStatus player in PlayerRegistry.Players)
        {
            if (player == null || player.IsDead) continue;
            foreach (SafeZone zone in safeZones)
            {
                if (zone == null) continue;
                bool found = false;
                foreach (PlayerStatus p in zone.PlayersInside)
                {
                    if (p == player)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    inside++;
                    break;
                }
            }
        }

        return inside * 2 >= alive;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        musicVolume = -1f;
        Muted = false;
    }
}
