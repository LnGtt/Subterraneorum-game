using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameSound
{
    PlayerAttack,
    PlayerHurt,
    PlayerDown,
    PlayerRevived,
    Dash,
    Ability,
    EnemyHit,
    EnemyDeath,
    LevelUp,
    CardFlip,
    CardChosen,
    BossRoar,
    BossAttack,
    Victory,
    Defeat,
    UiMove,
    UiConfirm,
    OrbCollect,
    DoorOpen,
}

public class GameAudio : MonoBehaviour
{
    public const string SfxResourcesPath = "Subterraneorum/Audio/Sfx";
    private const string SfxVolumeKey = "Subterraneorum.SfxVolume";

    [Serializable]
    public struct Entry
    {
        public GameSound sound;
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume;
    }

    [Tooltip("Clips por evento. Eventos sem entrada aqui usam os arquivos de Resources/Subterraneorum/Audio/Sfx com o nome do evento (ex.: EnemyHit_0, EnemyHit_1).")]
    [SerializeField] private Entry[] sounds = Array.Empty<Entry>();
    [SerializeField, Min(1)] private int voices = 12;
    [Tooltip("Intervalo mínimo entre duas execuções do mesmo som (s). Evita estouro quando uma habilidade acerta vários inimigos no mesmo frame.")]
    [SerializeField] private float minRepeatInterval = 0.045f;

    private static readonly Dictionary<GameSound, float> DefaultVolumes = new Dictionary<GameSound, float>
    {
        { GameSound.PlayerAttack, 0.55f },
        { GameSound.PlayerHurt, 0.75f },
        { GameSound.PlayerDown, 0.9f },
        { GameSound.PlayerRevived, 0.8f },
        { GameSound.Dash, 0.5f },
        { GameSound.Ability, 0.8f },
        { GameSound.EnemyHit, 0.6f },
        { GameSound.EnemyDeath, 0.65f },
        { GameSound.LevelUp, 0.85f },
        { GameSound.CardFlip, 0.7f },
        { GameSound.CardChosen, 0.8f },
        { GameSound.BossRoar, 1f },
        { GameSound.BossAttack, 0.85f },
        { GameSound.Victory, 0.9f },
        { GameSound.Defeat, 0.9f },
        { GameSound.UiMove, 0.6f },
        { GameSound.UiConfirm, 0.7f },
        { GameSound.OrbCollect, 0.7f },
        { GameSound.DoorOpen, 0.75f },
    };

    private static readonly HashSet<GameSound> FixedPitch = new HashSet<GameSound>
    {
        GameSound.Victory, GameSound.Defeat, GameSound.LevelUp, GameSound.UiConfirm, GameSound.CardChosen,
    };

    private static GameAudio instance;
    private static float sfxVolume = -1f;
    private static Dictionary<GameSound, AudioClip[]> library;
    private AudioSource[] sources;
    private int nextSource;
    private readonly Dictionary<GameSound, AudioClip[]> clips = new Dictionary<GameSound, AudioClip[]>();
    private readonly Dictionary<GameSound, float> volumes = new Dictionary<GameSound, float>();
    private readonly Dictionary<GameSound, float> lastPlayed = new Dictionary<GameSound, float>();

    public static float SfxVolume
    {
        get
        {
            if (sfxVolume < 0f) sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
            return sfxVolume;
        }
        set
        {
            sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        sources = new AudioSource[voices];
        for (int i = 0; i < voices; i++)
        {
            sources[i] = gameObject.AddComponent<AudioSource>();
            sources[i].playOnAwake = false;
            sources[i].spatialBlend = 0f;
            sources[i].ignoreListenerPause = true;
        }

        LoadClips();
    }

    private void LoadClips()
    {
        foreach (Entry entry in sounds)
        {
            if (entry.clips == null || entry.clips.Length == 0) continue;
            clips[entry.sound] = entry.clips;
            if (entry.volume > 0f) volumes[entry.sound] = entry.volume;
        }

        foreach (KeyValuePair<GameSound, AudioClip[]> pair in LibraryClips())
        {
            if (!clips.ContainsKey(pair.Key)) clips[pair.Key] = pair.Value;
        }
    }

    private static Dictionary<GameSound, AudioClip[]> LibraryClips()
    {
        if (library != null) return library;

        var found = new Dictionary<GameSound, List<AudioClip>>();
        foreach (AudioClip clip in Resources.LoadAll<AudioClip>(SfxResourcesPath))
        {
            if (clip == null) continue;
            string prefix = clip.name.Split('_')[0];
            if (!Enum.TryParse(prefix, out GameSound sound)) continue;
            if (!found.TryGetValue(sound, out List<AudioClip> list)) found[sound] = list = new List<AudioClip>();
            list.Add(clip);
        }

        library = new Dictionary<GameSound, AudioClip[]>();
        foreach (KeyValuePair<GameSound, List<AudioClip>> pair in found) library[pair.Key] = pair.Value.ToArray();
        return library;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public static void Play(GameSound sound)
    {
        if (instance != null) instance.PlayInternal(sound);
    }

    private void PlayInternal(GameSound sound)
    {
        if (!clips.TryGetValue(sound, out AudioClip[] options) || options.Length == 0) return;

        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(sound, out float last) && now - last < minRepeatInterval) return;
        lastPlayed[sound] = now;

        AudioClip clip = options[UnityEngine.Random.Range(0, options.Length)];
        if (clip == null) return;

        if (!volumes.TryGetValue(sound, out float volume)) volume = DefaultVolumes.TryGetValue(sound, out float preset) ? preset : 1f;

        AudioSource source = sources[nextSource];
        nextSource = (nextSource + 1) % sources.Length;
        source.pitch = FixedPitch.Contains(sound) ? 1f : UnityEngine.Random.Range(0.94f, 1.06f);
        source.PlayOneShot(clip, volume * SfxVolume);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        sfxVolume = -1f;
    }
}
