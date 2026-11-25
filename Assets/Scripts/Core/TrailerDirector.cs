using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ProceduralRuins;
using Subterraneorum.DungeonSystem;
using Unity.Cinemachine;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

public class TrailerDirector : MonoBehaviour
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int Fps = 60;

    private enum BotMode { Idle, Manual, MoveTo, Fight, Orbs, Revive }

    private enum CamMode { Game, Follow, Path, Fixed }

    private sealed class Bot
    {
        public int Index;
        public Gamepad Pad;
        public PlayerStatus Player;
        public string ClassId = "";
        public BotMode Mode = BotMode.Idle;
        public Vector3 Target;
        public float ArriveRadius = 0.7f;
        public Vector2 ManualStick;
        public bool AutoAbilities = true;
        public float NextAttack;
        public float NextAbility;
        public float NextDash;
        public int AbilitySlot;
        public Vector2 Stick;
        public readonly HashSet<GamepadButton> Held = new HashSet<GamepadButton>();
        public readonly Dictionary<GamepadButton, int> Taps = new Dictionary<GamepadButton, int>();
        public bool Melee => ClassId == "Warrior" || ClassId == "Rogue";
    }

    private string outDir;
    private string ffmpegPath;
    private int seed = 7;
    private readonly List<Bot> bots = new List<Bot>();
    private readonly Gamepad[] pads = new Gamepad[4];
    private readonly HashSet<PlayerStatus> god = new HashSet<PlayerStatus>();
    private DungeonManager dungeon;

    private Camera cam;
    private CinemachineBrain brain;
    private Camera uiCam;
    private RenderTexture sceneRT;
    private RenderTexture uiRT;
    private Texture2D readScene;
    private Texture2D readUi;
    private byte[] frame;
    private System.Diagnostics.Process encoder;
    private Stream encoderInput;
    private string shotName;
    private int shotFrames;
    private bool recording;

    private CamMode camMode = CamMode.Game;
    private Vector3 followOffset = new Vector3(0f, 8f, -7f);
    private float followFov = 45f;
    private bool followIncludeEnemies = true;
    private Transform followFocus;
    private Vector3 camPos;
    private Vector3 camLook;
    private Vector3 pathFromPos, pathToPos, pathFromLook, pathToLook;
    private float pathFromFov, pathToFov;
    private int pathFrames, pathFrame;
    private bool hudVisible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-trailer");
        if (index < 0 || index + 1 >= args.Length) return;

        var go = new GameObject("TrailerDirector");
        DontDestroyOnLoad(go);
        TrailerDirector director = go.AddComponent<TrailerDirector>();
        director.outDir = args[index + 1];
        int ff = Array.IndexOf(args, "-trailerFfmpeg");
        director.ffmpegPath = ff >= 0 && ff + 1 < args.Length ? args[ff + 1] : "ffmpeg";
        int sd = Array.IndexOf(args, "-trailerSeed");
        if (sd >= 0 && sd + 1 < args.Length && int.TryParse(args[sd + 1], out int s)) director.seed = s;
    }

    private void Configure()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Time.captureFramerate = Fps;
        Time.fixedDeltaTime = 1f / Fps;
        Time.maximumDeltaTime = 1f / Fps;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        MusicDirector.Muted = true;
        Cursor.visible = false;
        Directory.CreateDirectory(outDir);
        UnityEngine.Random.InitState(seed);
        Debug.Log($"[Trailer] Gravando em {outDir} com seed {seed}.");
    }

    private IEnumerator Start()
    {
        Configure();
        if (SceneManager.GetActiveScene().name != MainMenuController.GameScene)
        {
            SceneManager.LoadScene(MainMenuController.GameScene);
            yield return null;
            yield return null;
        }

        DungeonGenerator generator = FindAnyObjectByType<DungeonGenerator>();
        if (generator != null) generator.seed = seed;
        dungeon = FindAnyObjectByType<DungeonManager>();
        SetupCapture();
        for (int i = 0; i < pads.Length; i++) pads[i] = InputSystem.AddDevice<Gamepad>($"TrailerPad{i + 1}");

        yield return Shot_Lobby();
        yield return WaitUntil(() => PlayerRegistry.Players.Count >= 4 && dungeon.CurrentLevel != null && GameObject.Find("Dungeon_Generated") != null, 30f);
        yield return Seconds(0.5f);
        BindBots();
        foreach (Bot bot in bots) PreparePlayer(bot.Player);

        yield return Run(Shot_SurfaceAerial());
        yield return Run(Shot_Gates());
        yield return Run(Shot_FightGame("03_fight_L1_game", 15f));
        yield return Run(Shot_FightFollow("04_fight_L1_close", 13f, new Vector3(0f, 6.5f, -6.2f), 44f));
        yield return Run(Shot_Abilities("05_abilities_L1"));
        yield return Run(Shot_LevelUpCards());
        yield return Run(Shot_SkillTree());
        yield return Run(Shot_Descend());
        yield return Run(Shot_FightFollow("09_fight_L2_close", 13f, new Vector3(0f, 7.5f, -7f), 46f));
        yield return Run(Shot_HeavyStrike());
        yield return Run(Shot_DownRevive());
        yield return Run(Shot_Massacre());
        yield return Run(Shot_Minigame());
        yield return Run(Shot_AbyssDrift());
        yield return Run(Shot_BossReveal());
        yield return Run(Shot_BossFight("17_boss_fight_game", 16f, false));
        yield return Run(Shot_BossFight("18_boss_fight_close", 12f, true));
        yield return Run(Shot_Wipe());

        File.WriteAllText(Path.Combine(outDir, "done.txt"), DateTime.Now.ToString("s"));
        Debug.Log("[Trailer] Gravação concluída.");
        yield return null;
        Application.Quit();
    }

    private IEnumerator Run(IEnumerator shot)
    {
        bool failed = false;
        while (true)
        {
            object current;
            try
            {
                if (!shot.MoveNext()) break;
                current = shot.Current;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Trailer] Falha na tomada {shotName}: {e}");
                failed = true;
                break;
            }

            yield return current;
        }

        if (failed && recording) yield return EndShot();
    }

    private IEnumerator Shot_Lobby()
    {
        SetCamGame();
        BeginShot("01_lobby");
        yield return Seconds(0.7f);
        for (int i = 0; i < pads.Length; i++)
        {
            TapPad(pads[i], GamepadButton.South);
            yield return Seconds(0.45f);
        }

        yield return Seconds(0.7f);
        for (int i = 0; i < pads.Length; i++)
        {
            TapPad(pads[i], GamepadButton.South);
            yield return Seconds(0.4f);
        }

        yield return Seconds(5.4f);
        yield return EndShot();
    }

    private IEnumerator Shot_SurfaceAerial()
    {
        SetHud(false);
        SetGodAll(true);
        PartyLeash.Enabled = false;
        GeneratedLevel level = dungeon.CurrentLevel;
        Bounds all = LevelBounds(level);
        Vector3 start = level.StartRoom.center;
        Vector3 across = (all.center - start);
        across.y = 0f;
        if (across.sqrMagnitude < 1f) across = Vector3.forward;
        across.Normalize();
        Vector3 fromPos = start - across * 18f + Vector3.up * 26f;
        Vector3 toPos = start + across * 4f + Vector3.up * 15f;
        SetCamPath(fromPos, toPos, start + across * 6f, start + across * 2f, 50f, 46f, 9f);
        BeginShot("02_surface_aerial");
        yield return Seconds(9f);
        yield return EndShot();
    }

    private IEnumerator Shot_Gates()
    {
        DungeonDoor door = BestDoor(out GeneratedRoom startRoom, out Vector3 inward);
        if (door == null)
        {
            Debug.LogWarning("[Trailer] Nenhuma porta utilizável.");
            yield break;
        }

        ClearEnemiesInRoom(startRoom.id);
        SetField(door, "openSeconds", 2.6f);
        Vector3 right = Vector3.Cross(Vector3.up, inward);
        Vector3 doorBase = new Vector3(door.Center.x, startRoom.center.y, door.Center.z);
        Vector3[] slots =
        {
            doorBase + inward * 2.8f - right * 0.9f,
            doorBase + inward * 2.8f + right * 0.9f,
            doorBase + inward * 4.2f - right * 1.4f,
            doorBase + inward * 4.2f + right * 1.4f,
        };

        for (int i = 0; i < bots.Count; i++)
        {
            Teleport(bots[i].Player, slots[i], Quaternion.LookRotation(-inward));
            bots[i].Mode = BotMode.Idle;
        }

        float depth = DepthInside(startRoom.worldBounds, doorBase, inward);
        float back = Mathf.Clamp(depth - 0.8f, 5.5f, 10f);
        Vector3 look = doorBase + Vector3.up * 1.7f;
        SetCamPath(doorBase + inward * back + Vector3.up * 2.6f, doorBase + inward * (back - 2.2f) + Vector3.up * 2.0f, look, look + Vector3.up * 0.1f, 46f, 40f, 11.5f);
        BeginShot("02b_gates");
        yield return Seconds(2.4f);
        door.Open();
        yield return Seconds(2.2f);
        Vector3 beyond = doorBase - inward * 7f;
        for (int i = 0; i < 3; i++)
        {
            bots[i].Mode = BotMode.MoveTo;
            bots[i].Target = beyond + right * ((i - 1) * 1.1f);
            yield return Seconds(0.25f);
        }

        yield return Seconds(1.6f);
        Bot rogue = ByClass("Rogue") ?? bots[3];
        rogue.Mode = BotMode.MoveTo;
        rogue.Target = doorBase + inward * 9f + right * 2.5f;
        yield return Seconds(0.9f);
        TapPad(rogue.Pad, GamepadButton.South);
        yield return Seconds(2.6f);
        yield return EndShot();
        foreach (Bot bot in bots) bot.Mode = BotMode.Idle;
    }

    private IEnumerator Shot_FightGame(string name, float seconds)
    {
        GeneratedRoom room = BusiestRoom();
        if (room == null) yield break;
        StagePartyInRoom(room);
        SetHud(true);
        PartyLeash.Enabled = true;
        SetCamGame();
        yield return Seconds(0.1f);
        SnapGameCamera();
        foreach (Bot bot in bots) bot.Mode = BotMode.Fight;
        BeginShot(name);
        yield return SecondsOrCleared(room, seconds);
        yield return EndShot();
        foreach (Bot bot in bots) bot.Mode = BotMode.Idle;
    }

    private IEnumerator Shot_FightFollow(string name, float seconds, Vector3 offset, float fov)
    {
        GeneratedRoom room = BusiestRoom();
        if (room == null) yield break;
        StagePartyInRoom(room);
        SetHud(false);
        PartyLeash.Enabled = false;
        SetCamFollow(offset, fov, true, null);
        foreach (Bot bot in bots) bot.Mode = BotMode.Fight;
        BeginShot(name);
        yield return SecondsOrCleared(room, seconds);
        yield return EndShot();
        foreach (Bot bot in bots) bot.Mode = BotMode.Idle;
    }

    private IEnumerator Shot_Abilities(string name)
    {
        GeneratedRoom room = BusiestRoom();
        if (room == null) yield break;
        StagePartyInRoom(room);
        Vector3 center = PartyCenter();
        int pulled = 0;
        foreach (EnemyStatus enemy in EnemyStatus.All)
        {
            if (enemy == null || enemy.IsDead || pulled >= 12) continue;
            float angle = pulled * 30f * Mathf.Deg2Rad;
            WarpEnemy(enemy, center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (4.5f + (pulled % 3)));
            enemy.ScaleHealth(2.5f);
            pulled++;
        }

        SetHud(false);
        PartyLeash.Enabled = false;
        SetCamFollow(new Vector3(0f, 7.5f, -7.5f), 46f, false, null);
        foreach (Bot bot in bots)
        {
            bot.Mode = BotMode.Fight;
            bot.AutoAbilities = false;
        }

        BeginShot(name);
        yield return Seconds(1.0f);
        Bot wizard = ByClass("Wizard");
        Bot ranger = ByClass("Ranger");
        Bot warrior = ByClass("Warrior");
        Bot rogue = ByClass("Rogue");
        if (wizard != null) TapPad(wizard.Pad, GamepadButton.East);
        yield return Seconds(1.3f);
        if (ranger != null) TapPad(ranger.Pad, GamepadButton.East);
        yield return Seconds(1.3f);
        if (warrior != null) TapPad(warrior.Pad, GamepadButton.LeftShoulder);
        yield return Seconds(1.3f);
        if (rogue != null) TapPad(rogue.Pad, GamepadButton.East);
        yield return Seconds(1.6f);
        if (rogue != null) TapPad(rogue.Pad, GamepadButton.LeftShoulder);
        foreach (Bot bot in bots) bot.AutoAbilities = true;
        yield return Seconds(6f);
        yield return EndShot();
        foreach (Bot bot in bots) bot.Mode = BotMode.Idle;
    }

    private IEnumerator Shot_LevelUpCards()
    {
        GeneratedRoom room = dungeon.CurrentLevel.StartRoom;
        Bot wizard = ByClass("Wizard") ?? bots[0];
        StagePartyAt(room.center);
        SetHud(true);
        PartyLeash.Enabled = true;
        SetCamGame();
        yield return Seconds(0.1f);
        SnapGameCamera();
        CardDraftManager draft = FindAnyObjectByType<CardDraftManager>();
        BeginShot("06_levelup_cards");
        yield return Seconds(0.8f);
        PlayerProgression progression = wizard.Player.GetComponent<PlayerProgression>();
        progression.AddXP(progression.XPToNextLevel - progression.CurrentXP + 1f, false);
        yield return WaitUntil(() => draft != null && draft.CurrentPhase == CardDraftManager.Phase.Voting, 6f);
        yield return Seconds(1.0f);
        int target = Mathf.Min(2, draft.OfferedCount - 1);
        for (int i = 0; i < bots.Count; i++)
        {
            int start = Mathf.Min(i, draft.OfferedCount - 1);
            if (i == 3) continue;
            int steps = target - start;
            for (int s = 0; s < Mathf.Abs(steps); s++)
            {
                yield return Nudge(bots[i].Pad, new Vector2(Mathf.Sign(steps), 0f));
            }

            yield return Seconds(0.25f);
            TapPad(bots[i].Pad, GamepadButton.South);
            yield return Seconds(0.35f);
        }

        TapPad(bots[3].Pad, GamepadButton.South);
        yield return Seconds(2.2f);
        TapPad(bots[3].Pad, GamepadButton.East);
        yield return Seconds(0.5f);
        int rogueStart = Mathf.Min(3, draft.OfferedCount - 1);
        for (int s = 0; s < Mathf.Abs(target - rogueStart); s++) yield return Nudge(bots[3].Pad, new Vector2(Mathf.Sign(target - rogueStart), 0f));
        yield return Seconds(0.3f);
        TapPad(bots[3].Pad, GamepadButton.South);
        yield return WaitUntil(() => draft.CurrentPhase == CardDraftManager.Phase.ChallengeOffer || draft.CurrentPhase == CardDraftManager.Phase.Idle, 8f);
        yield return Seconds(1.2f);
        foreach (Bot bot in bots)
        {
            if (draft.CurrentPhase != CardDraftManager.Phase.ChallengeOffer) break;
            TapPad(bot.Pad, GamepadButton.East);
            yield return Seconds(0.3f);
        }

        yield return WaitUntil(() => draft.CurrentPhase == CardDraftManager.Phase.Idle, 8f);
        yield return Seconds(0.8f);
        yield return EndShot();
    }

    private IEnumerator Shot_SkillTree()
    {
        Bot wizard = ByClass("Wizard") ?? bots[0];
        BeginShot("07_skilltree");
        yield return Seconds(0.6f);
        TapPad(wizard.Pad, GamepadButton.Select);
        yield return Seconds(1.4f);
        yield return Nudge(wizard.Pad, new Vector2(0f, -1f));
        yield return Seconds(0.5f);
        yield return Nudge(wizard.Pad, new Vector2(0f, -1f));
        yield return Seconds(0.6f);
        TapPad(wizard.Pad, GamepadButton.South);
        yield return Seconds(1.6f);
        TapPad(wizard.Pad, GamepadButton.Select);
        yield return Seconds(1.0f);
        yield return EndShot();
    }

    private IEnumerator Shot_Descend()
    {
        GeneratedRoom exit = dungeon.CurrentLevel.ExitRoom;
        if (exit == null) yield break;
        ClearEnemiesInRoom(exit.id);
        Vector3 approach = exit.center + (dungeon.CurrentLevel.StartRoom.center - exit.center).normalized * Mathf.Min(7f, exit.worldBounds.extents.x * 0.8f);
        approach.y = exit.center.y;
        StagePartyAt(approach);
        SetHud(true);
        PartyLeash.Enabled = true;
        SetCamGame();
        yield return Seconds(0.1f);
        SnapGameCamera();
        foreach (Bot bot in bots)
        {
            bot.Mode = BotMode.MoveTo;
            bot.Target = exit.center + new Vector3((bot.Index % 2) * 1.2f - 0.6f, 0f, (bot.Index / 2) * 1.2f - 0.6f);
            bot.ArriveRadius = 0.3f;
        }

        int level = dungeon.CurrentLevelIndex;
        BeginShot("08_descend");
        yield return WaitUntil(() => dungeon.CurrentLevelIndex != level && GameObject.Find("Dungeon_Generated") != null, 12f);
        foreach (Bot bot in bots)
        {
            bot.Mode = BotMode.Idle;
            bot.ArriveRadius = 0.7f;
        }

        yield return Seconds(0.2f);
        SnapGameCamera();
        yield return Seconds(3.0f);
        yield return EndShot();
        foreach (Bot bot in bots) PreparePlayer(bot.Player);
    }

    private IEnumerator Shot_HeavyStrike()
    {
        GeneratedRoom room = BusiestRoom();
        Bot warrior = ByClass("Warrior");
        if (room == null || warrior == null) yield break;
        StagePartyInRoom(room);
        EnemyStatus victim = ClosestEnemy(warrior.Player.transform.position, 40f);
        if (victim == null) yield break;
        Vector3 spot = victim.transform.position;
        Vector3 dir = (warrior.Player.transform.position - spot);
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
        Teleport(warrior.Player, spot + dir * 1.6f, Quaternion.LookRotation(-dir));
        SetHud(false);
        PartyLeash.Enabled = false;
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Vector3 mid = (spot + warrior.Player.transform.position) * 0.5f;
        Vector3 camA = mid + side * 7.0f + dir * 2.5f;
        Vector3 camB = mid - side * 7.0f + dir * 2.5f;
        Vector3 camC = mid + dir * 7.0f;
        Vector3 chosen = dungeon.RoomAt(camA) != null ? camA : dungeon.RoomAt(camB) != null ? camB : camC;
        Vector3 pull = Flat(chosen - mid).normalized;
        SetCamPath(mid + pull * 7.5f + Vector3.up * 5.0f, mid + pull * 6.0f + Vector3.up * 4.0f, mid + Vector3.up * 0.9f, mid + Vector3.up * 0.9f, 46f, 40f, 4.5f);
        foreach (Bot bot in bots) bot.Mode = bot == warrior ? BotMode.Manual : BotMode.Fight;
        warrior.ManualStick = Vector2.zero;
        BeginShot("10_heavy_strike");
        yield return Seconds(0.9f);
        TapPad(warrior.Pad, GamepadButton.LeftShoulder);
        yield return Seconds(0.25f);
        Time.timeScale = 0.3f;
        yield return Seconds(1.6f);
        Time.timeScale = 1f;
        yield return Seconds(1.5f);
        yield return EndShot();
        foreach (Bot bot in bots) bot.Mode = BotMode.Idle;
    }

    private IEnumerator Shot_DownRevive()
    {
        GeneratedRoom room = BusiestRoom();
        Bot rogue = ByClass("Rogue");
        Bot warrior = ByClass("Warrior");
        if (room == null || rogue == null || warrior == null) yield break;
        StagePartyInRoom(room);
        SetHud(true);
        PartyLeash.Enabled = false;
        SetCamFollow(new Vector3(0f, 9f, -8f), 46f, true, null);
        foreach (Bot bot in bots) bot.Mode = BotMode.Fight;
        BeginShot("11_down_revive");
        yield return Seconds(2.0f);
        god.Remove(rogue.Player);
        yield return KillPlayer(rogue.Player);

        rogue.Mode = BotMode.Idle;
        yield return Seconds(1.8f);
        warrior.Mode = BotMode.Revive;
        yield return WaitUntil(() => !rogue.Player.IsDead, 7f);
        warrior.Mode = BotMode.Fight;
        rogue.Mode = BotMode.Fight;
        god.Add(rogue.Player);
        yield return Seconds(2.0f);
        yield return EndShot();
        foreach (Bot bot in bots) bot.Mode = BotMode.Idle;
    }

    private IEnumerator Shot_Massacre()
    {
        GeneratedRoom room = BusiestRoom();
        if (room == null) yield break;
        StagePartyInRoom(room);
        Vector3 center = PartyCenter();
        int pulled = 0;
        foreach (EnemyStatus enemy in EnemyStatus.All)
        {
            if (enemy == null || enemy.IsDead || pulled >= 16) continue;
            float angle = pulled * (360f / 16f) * Mathf.Deg2Rad;
            WarpEnemy(enemy, center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (3.5f + (pulled % 2) * 1.5f));
            pulled++;
        }

        SetHud(true);
        PartyLeash.Enabled = false;
        SetCamFollow(new Vector3(0f, 10f, -8.5f), 48f, true, null);
        Bot ranger = ByClass("Ranger");
        Bot wizard = ByClass("Wizard");
        foreach (Bot bot in bots) bot.Mode = BotMode.Fight;
        var victims = new List<PlayerStatus>();
        if (ranger != null) victims.Add(ranger.Player);
        if (wizard != null) victims.Add(wizard.Player);
        foreach (PlayerStatus victim in victims)
        {
            god.Remove(victim);
            victim.DamageTakenMultiplier = 3f;
        }

        BeginShot("12_massacre");
        float end = 11f;
        float t = 0f;
        while (t < end)
        {
            t += 1f / Fps;
            if (t > 6f)
            {
                foreach (PlayerStatus victim in victims)
                {
                    if (!victim.IsDead && t > 6f + victims.IndexOf(victim) * 1.4f) ForceKill(victim);
                }
            }

            yield return null;
        }

        yield return EndShot();
        foreach (PlayerStatus victim in victims)
        {
            victim.DamageTakenMultiplier = 1f;
            if (victim.IsDead) victim.Revive(1f);
            god.Add(victim);
        }

        foreach (Bot bot in bots) bot.Mode = BotMode.Idle;
    }

    private IEnumerator Shot_Minigame()
    {
        GeneratedRoom safe = null;
        foreach (GeneratedRoom room in dungeon.CurrentLevel.RoomsOfType(DungeonRoomType.SafeZone))
        {
            safe = room;
            break;
        }

        if (safe == null) yield break;
        StagePartyAt(safe.center);
        SetHud(true);
        PartyLeash.Enabled = false;
        SetCamFollow(new Vector3(0f, 11f, -9f), 50f, false, null);
        followFocus = null;
        BeginShot("13_minigame");
        yield return Seconds(1.0f);
        foreach (Bot bot in bots)
        {
            TapPad(bot.Pad, GamepadButton.North);
            yield return Seconds(0.35f);
        }

        yield return Seconds(4.8f);
        foreach (Bot bot in bots) bot.Mode = BotMode.Orbs;
        yield return Seconds(11f);
        yield return EndShot();
        foreach (Bot bot in bots) bot.Mode = BotMode.Idle;
        yield return Seconds(10f);
    }

    private IEnumerator Shot_AbyssDrift()
    {
        dungeon.GoToFinalLevel();
        yield return WaitUntil(() => dungeon.IsOnFinalLevel && GameObject.Find("Dungeon_Generated") != null, 15f);
        yield return Seconds(0.5f);
        foreach (Bot bot in bots) PreparePlayer(bot.Player);
        GeneratedLevel level = dungeon.CurrentLevel;
        GeneratedRoom boss = level.BossRoom;
        GeneratedRoom start = level.StartRoom;
        SetHud(false);
        PartyLeash.Enabled = false;
        Vector3 a = start.center;
        Vector3 b = boss != null ? boss.center : LevelBounds(level).center;
        Vector3 dir = (b - a);
        dir.y = 0f;
        dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.forward;
        SetCamPath(a - dir * 10f + Vector3.up * 24f, a + dir * 10f + Vector3.up * 19f, a + dir * 2f, a + dir * 18f, 50f, 46f, 11f);
        BeginShot("15_abyss_drift");
        yield return Seconds(11f);
        yield return EndShot();
    }

    private IEnumerator Shot_BossReveal()
    {
        GeneratedRoom room = dungeon.CurrentLevel.BossRoom;
        if (room == null) yield break;
        Bounds b = room.worldBounds;
        Vector3 c = room.center;
        SetField(dungeon, "bossSpawned", true);
        float backZ = Mathf.Max(b.min.z + 1.0f, c.z - 13f);
        for (int i = 0; i < bots.Count; i++)
        {
            Vector3 p = new Vector3(c.x + (i - 1.5f) * 2.0f, c.y, Mathf.Max(b.min.z + 4f, c.z - 6f) + (i % 2) * 0.8f);
            Teleport(bots[i].Player, p, Quaternion.LookRotation(Vector3.forward));
            bots[i].Mode = BotMode.Idle;
        }

        SetHud(false);
        PartyLeash.Enabled = false;
        Vector3 camStart = new Vector3(c.x, c.y + 4.2f, backZ);
        SetCamPath(camStart, camStart + new Vector3(0f, -0.4f, 1.5f), c + Vector3.up * 2.6f, c + Vector3.up * 3.3f, 58f, 50f, 7.5f);
        BeginShot("16_boss_reveal");
        yield return Seconds(1.2f);
        SetField(dungeon, "bossSpawned", false);
        dungeon.SpawnBossNow();
        yield return Seconds(3.4f);
        foreach (Bot bot in bots)
        {
            bot.Mode = BotMode.MoveTo;
            bot.Target = bot.Player.transform.position + Vector3.forward * 3.5f;
        }

        yield return Seconds(2.5f);
        yield return EndShot();
    }

    private IEnumerator Shot_BossFight(string name, float seconds, bool close)
    {
        EnemyStatus boss = BossStatus();
        if (boss == null) yield break;
        if (close)
        {
            SetHud(false);
            PartyLeash.Enabled = false;
            SetCamFollow(new Vector3(0f, 7f, -10f), 48f, false, boss.transform);
        }
        else
        {
            SetHud(true);
            PartyLeash.Enabled = true;
            SetCamGame();
            yield return Seconds(0.1f);
        }

        foreach (Bot bot in bots)
        {
            bot.Mode = BotMode.Fight;
            PlayerCombat combat = bot.Player.GetComponent<PlayerCombat>();
            if (combat != null) combat.DamageMultiplier = 0.35f;
        }

        BeginShot(name);
        BossController controller = boss.GetComponent<BossController>();
        float t = 0f;
        int forced = 0;
        while (t < seconds && boss != null && !boss.IsDead)
        {
            if (controller != null && !controller.IsAttacking && t > 1.5f + forced * 4.5f)
            {
                controller.ForceAttack(forced % 3);
                forced++;
            }

            if (boss.CurrentHealth < boss.MaxHealth * 0.35f) SetField(boss, "currentHealth", boss.MaxHealth * 0.7f);
            t += 1f / Fps;
            yield return null;
        }

        yield return EndShot();
    }

    private IEnumerator Shot_Wipe()
    {
        EnemyStatus boss = BossStatus();
        if (boss == null) yield break;
        SetHud(true);
        PartyLeash.Enabled = false;
        SetCamFollow(new Vector3(0f, 9.5f, -11f), 50f, false, boss.transform);
        BossController controller = boss.GetComponent<BossController>();
        foreach (Bot bot in bots)
        {
            bot.Mode = BotMode.Fight;
            god.Remove(bot.Player);
            bot.Player.DamageTakenMultiplier = 0.9f;
        }

        BeginShot("19_wipe");
        float t = 0f;
        int forced = 0;
        while (PlayerRegistry.AliveCount > 0 && t < 14f)
        {
            if (controller != null && !controller.IsAttacking && t > 0.6f + forced * 3.2f)
            {
                controller.ForceAttack(forced % 3 == 2 ? 0 : forced % 3);
                forced++;
            }

            if (t > 3.5f)
            {
                foreach (Bot bot in bots)
                {
                    if (!bot.Player.IsDead && t > 3.5f + bot.Index * 0.45f) ForceKill(bot.Player);
                }
            }

            t += 1f / Fps;
            yield return null;
        }

        yield return Seconds(4.5f);
        yield return EndShot();
    }

    private static void ForceKill(PlayerStatus player)
    {
        if (player == null || player.IsDead) return;
        SetField(player, "invulnerableUntil", 0f);
        player.TakeDamage(player.CurrentHealth / Mathf.Max(0.01f, player.DamageTakenMultiplier * player.BlockMultiplier) + 500f);
    }

    private IEnumerator KillPlayer(PlayerStatus player)
    {
        for (int i = 0; i < Fps && player != null && !player.IsDead; i++)
        {
            ForceKill(player);
            if (!player.IsDead) yield return null;
        }
    }

    private void BindBots()
    {
        bots.Clear();
        foreach (PlayerStatus player in PlayerRegistry.Players)
        {
            PlayerIdentity identity = player.GetComponent<PlayerIdentity>();
            PlayerSeat seat = identity != null ? identity.Seat : null;
            Gamepad pad = null;
            if (seat != null && seat.Input != null)
            {
                foreach (InputDevice device in seat.Input.devices)
                {
                    if (device is Gamepad g) pad = g;
                }
            }

            if (pad == null) continue;
            bots.Add(new Bot
            {
                Index = Array.IndexOf(pads, pad),
                Pad = pad,
                Player = player,
                ClassId = identity != null && identity.Class != null ? identity.Class.classId : "",
            });
        }

        bots.Sort((x, y) => x.Index.CompareTo(y.Index));
        foreach (Bot bot in bots) Debug.Log($"[Trailer] Bot {bot.Index + 1}: {bot.ClassId} ({bot.Player.name})");
    }

    private void PreparePlayer(PlayerStatus player)
    {
        if (player == null) return;
        if (player.IsDead) player.Revive(1f);
        player.Heal(player.MaxHealth);
        player.DamageTakenMultiplier = 0.35f;
        god.Add(player);
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        if (abilities != null)
        {
            abilities.UnlockAbility2();
            abilities.CooldownMultiplier = 0.45f;
        }

        PlayerCombat combat = player.GetComponent<PlayerCombat>();
        if (combat != null) combat.DamageMultiplier = 0.55f;
        PlayerProgression progression = player.GetComponent<PlayerProgression>();
        if (progression != null) progression.XPGainMultiplier = 0f;
    }

    private void SetGodAll(bool on)
    {
        foreach (Bot bot in bots)
        {
            if (on) god.Add(bot.Player);
            else god.Remove(bot.Player);
        }
    }

    private void Update()
    {
        foreach (PlayerStatus player in god)
        {
            if (player == null || player.IsDead) continue;
            float missing = player.MaxHealth * 0.85f - player.CurrentHealth;
            if (missing > 0f) player.Heal(Mathf.Min(4.5f, missing));
        }

        foreach (Bot bot in bots) Think(bot);
    }

    private void LateUpdate()
    {
        foreach (Gamepad pad in pads)
        {
            if (pad == null) continue;
            Bot bot = bots.Find(b => b.Pad == pad);
            if (bot == null) FlushPad(pad, padStick.TryGetValue(pad, out Vector2 s) ? s : Vector2.zero, padTaps.TryGetValue(pad, out var taps) ? taps : null, null);
            else FlushPad(pad, bot.Stick, bot.Taps, bot.Held);
        }

        UpdateCamera();
        ConvertCanvases();
    }

    private readonly Dictionary<Gamepad, Vector2> padStick = new Dictionary<Gamepad, Vector2>();
    private readonly Dictionary<Gamepad, Dictionary<GamepadButton, int>> padTaps = new Dictionary<Gamepad, Dictionary<GamepadButton, int>>();

    private void TapPad(Gamepad pad, GamepadButton button)
    {
        Bot bot = bots.Find(b => b.Pad == pad);
        Dictionary<GamepadButton, int> taps;
        if (bot != null) taps = bot.Taps;
        else if (!padTaps.TryGetValue(pad, out taps)) padTaps[pad] = taps = new Dictionary<GamepadButton, int>();
        taps[button] = 3;
    }

    private IEnumerator Nudge(Gamepad pad, Vector2 direction)
    {
        Bot bot = bots.Find(b => b.Pad == pad);
        BotMode previous = bot != null ? bot.Mode : BotMode.Idle;
        if (bot != null)
        {
            bot.Mode = BotMode.Manual;
            bot.ManualStick = direction;
        }
        else padStick[pad] = direction;

        yield return Frames(5);
        if (bot != null) bot.ManualStick = Vector2.zero;
        else padStick[pad] = Vector2.zero;
        yield return Frames(7);
        if (bot != null) bot.Mode = previous;
    }

    private static void FlushPad(Gamepad pad, Vector2 stick, Dictionary<GamepadButton, int> taps, HashSet<GamepadButton> held)
    {
        var state = new GamepadState { leftStick = stick };
        if (held != null)
        {
            foreach (GamepadButton button in held) state = state.WithButton(button, true);
        }

        if (taps != null)
        {
            var keys = new List<GamepadButton>(taps.Keys);
            foreach (GamepadButton button in keys)
            {
                int left = taps[button];
                if (left > 0) state = state.WithButton(button, true);
                left--;
                if (left <= -3) taps.Remove(button);
                else taps[button] = left;
            }
        }

        InputSystem.QueueStateEvent(pad, state);
    }

    private void Think(Bot bot)
    {
        bot.Held.Clear();
        PlayerStatus player = bot.Player;
        if (player == null || player.IsDead)
        {
            bot.Stick = Vector2.zero;
            return;
        }

        Vector3 pos = player.transform.position;
        switch (bot.Mode)
        {
            case BotMode.Idle:
                bot.Stick = Vector2.zero;
                break;
            case BotMode.Manual:
                bot.Stick = bot.ManualStick;
                break;
            case BotMode.MoveTo:
                bot.Stick = StickToward(pos, bot.Target, bot.ArriveRadius, 1f);
                break;
            case BotMode.Revive:
                PlayerStatus downed = null;
                float best = float.MaxValue;
                foreach (PlayerStatus other in PlayerRegistry.Players)
                {
                    if (other == null || !other.IsDead || other == player) continue;
                    float d = (other.transform.position - pos).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        downed = other;
                    }
                }

                if (downed == null)
                {
                    bot.Stick = Vector2.zero;
                    break;
                }

                bot.Stick = StickToward(pos, downed.transform.position, 1.2f, 1f);
                if (Flat(downed.transform.position - pos).magnitude < 1.8f) bot.Held.Add(GamepadButton.North);
                break;
            case BotMode.Orbs:
                Transform orb = NearestOrb(pos);
                bot.Stick = orb != null ? StickToward(pos, orb.position, 0.2f, 1f) : Vector2.zero;
                break;
            case BotMode.Fight:
                Fight(bot, pos);
                break;
        }
    }

    private void Fight(Bot bot, Vector3 pos)
    {
        EnemyStatus target = ClosestEnemy(pos, 26f);
        float now = Time.time;
        if (target == null)
        {
            Vector3 center = PartyCenter();
            bot.Stick = StickToward(pos, center, 2.5f, 0.8f);
            return;
        }

        Vector3 to = Flat(target.transform.position - pos);
        float dist = to.magnitude;
        bool isBoss = target.GetComponent<BossController>() != null;
        float reach = bot.Melee ? (isBoss ? 2.6f : 1.5f) : (isBoss ? 8f : 6f);
        Vector3 dir = dist > 0.01f ? to / dist : Vector3.forward;

        if (bot.Melee)
        {
            bot.Stick = dist > reach ? WorldToStick(dir) : WorldToStick(dir) * 0.22f;
        }
        else
        {
            if (dist > reach + 1.5f) bot.Stick = WorldToStick(dir);
            else if (dist < reach - 2.5f) bot.Stick = WorldToStick(-dir) * 0.8f;
            else bot.Stick = WorldToStick(dir) * 0.2f;
        }

        if (dist <= reach + (bot.Melee ? 0.6f : 3f) && now >= bot.NextAttack)
        {
            bot.Taps[GamepadButton.West] = 2;
            bot.NextAttack = now + (bot.Melee ? 0.22f : 0.3f) + UnityEngine.Random.Range(0f, 0.08f);
        }

        if (bot.AutoAbilities && now >= bot.NextAbility && dist < (bot.Melee ? 4f : 9f))
        {
            bot.Taps[bot.AbilitySlot == 0 ? GamepadButton.East : GamepadButton.LeftShoulder] = 2;
            bot.AbilitySlot = 1 - bot.AbilitySlot;
            bot.NextAbility = now + UnityEngine.Random.Range(1.6f, 3.2f);
        }

        if (bot.Melee && dist > 4.5f && now >= bot.NextDash)
        {
            bot.Taps[GamepadButton.South] = 2;
            bot.NextDash = now + UnityEngine.Random.Range(2.5f, 4.5f);
        }
    }

    private Vector2 StickToward(Vector3 from, Vector3 to, float arrive, float speed)
    {
        Vector3 d = Flat(to - from);
        if (d.magnitude <= arrive) return Vector2.zero;
        return WorldToStick(d.normalized) * speed;
    }

    private Vector2 WorldToStick(Vector3 dir)
    {
        if (cam == null) return new Vector2(dir.x, dir.z);
        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
            right = Vector3.right;
        }

        var v = new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, forward));
        return v.sqrMagnitude > 1f ? v.normalized : v;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private Transform NearestOrb(Vector3 pos)
    {
        Transform best = null;
        float bestSqr = float.MaxValue;
        foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            if (r.name != "MinigameOrb") continue;
            float d = Flat(r.transform.position - pos).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = r.transform;
            }
        }

        return best;
    }

    private static EnemyStatus ClosestEnemy(Vector3 pos, float maxDistance)
    {
        EnemyStatus best = null;
        float bestSqr = maxDistance * maxDistance;
        foreach (EnemyStatus enemy in EnemyStatus.All)
        {
            if (enemy == null || enemy.IsDead) continue;
            float d = Flat(enemy.transform.position - pos).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = enemy;
            }
        }

        return best;
    }

    private static EnemyStatus BossStatus()
    {
        foreach (EnemyStatus enemy in EnemyStatus.All)
        {
            if (enemy != null && !enemy.IsDead && enemy.GetComponent<BossController>() != null) return enemy;
        }

        return null;
    }

    private Bot ByClass(string classId) => bots.Find(b => b.ClassId == classId);

    private Vector3 PartyCenter()
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (Bot bot in bots)
        {
            if (bot.Player == null || bot.Player.IsDead) continue;
            sum += bot.Player.transform.position;
            n++;
        }

        if (n == 0)
        {
            foreach (Bot bot in bots)
            {
                if (bot.Player == null) continue;
                sum += bot.Player.transform.position;
                n++;
            }
        }

        return n > 0 ? sum / n : Vector3.zero;
    }

    private GeneratedRoom BusiestRoom()
    {
        GeneratedRoom best = null;
        int bestCount = 0;
        foreach (GeneratedRoom room in dungeon.CurrentLevel.rooms)
        {
            int count = dungeon.AliveEnemiesInRoom(room.id);
            if (count > bestCount)
            {
                bestCount = count;
                best = room;
            }
        }

        return best;
    }

    private void StagePartyInRoom(GeneratedRoom room)
    {
        Vector3 center = room.center;
        Vector3 start = dungeon.CurrentLevel.StartRoom.center;
        Vector3 toStart = Flat(start - center);
        toStart = toStart.sqrMagnitude > 0.01f ? toStart.normalized : Vector3.back;
        float edge = Mathf.Min(room.worldBounds.extents.x, room.worldBounds.extents.z) * 0.45f;
        StagePartyAt(center + toStart * edge);
    }

    private void StagePartyAt(Vector3 point)
    {
        for (int i = 0; i < bots.Count; i++)
        {
            Vector3 offset = new Vector3((i % 2) * 1.6f - 0.8f, 0f, (i / 2) * 1.6f - 0.8f);
            Vector3 p = point + offset;
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) p = hit.position;
            if (bots[i].Player.IsDead) bots[i].Player.Revive(1f);
            Teleport(bots[i].Player, p, Quaternion.identity);
        }
    }

    private static void Teleport(PlayerStatus player, Vector3 position, Quaternion rotation)
    {
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement != null) movement.Teleport(position, rotation);
        else player.transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
    }

    private static void WarpEnemy(EnemyStatus enemy, Vector3 position)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) position = hit.position;
        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled) agent.Warp(position);
        else enemy.transform.position = position;
    }

    private void ClearEnemiesInRoom(int roomId)
    {
        foreach (EnemyStatus enemy in new List<EnemyStatus>(EnemyStatus.All))
        {
            if (enemy != null && !enemy.IsDead && enemy.HomeRoomId == roomId) enemy.gameObject.SetActive(false);
        }
    }

    private DungeonDoor BestDoor(out GeneratedRoom room, out Vector3 inward)
    {
        DungeonDoor best = null;
        room = null;
        inward = Vector3.forward;
        float bestScore = float.MinValue;
        Vector3 start = dungeon.CurrentLevel.StartRoom.center;
        foreach (DungeonDoor door in DungeonDoor.All)
        {
            if (door == null || door.IsOpen) continue;
            Vector3 n = Flat(door.Normal).normalized;
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector3 side = n * sign;
                GeneratedRoom candidate = dungeon.RoomAt(door.Center + side * 3f);
                if (candidate == null) continue;
                Vector3 doorBase = new Vector3(door.Center.x, candidate.center.y, door.Center.z);
                float depth = DepthInside(candidate.worldBounds, doorBase + side * 0.6f, side);
                float width = Mathf.Min(candidate.worldBounds.size.x, candidate.worldBounds.size.z);
                float score = Mathf.Min(depth, 11f) * 2f + Mathf.Min(width, 12f) - Flat(door.Center - start).magnitude * 0.05f;
                if (candidate.type == DungeonRoomType.Boss || candidate.type == DungeonRoomType.Exit) score -= 50f;
                if (depth < 6f) score -= 30f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = door;
                    room = candidate;
                    inward = side;
                }
            }
        }

        if (best != null) Debug.Log($"[Trailer] Porta escolhida em {best.Center}, sala {room.id} ({room.type}), pontuação {bestScore:0.0}.");
        return best;
    }

    private static float DepthInside(Bounds bounds, Vector3 from, Vector3 dir)
    {
        float t = 0f;
        for (int i = 0; i < 60; i++)
        {
            Vector3 p = from + dir * (t + 0.5f);
            p.y = bounds.center.y;
            if (!bounds.Contains(p)) break;
            t += 0.5f;
        }

        return t;
    }

    private static Bounds LevelBounds(GeneratedLevel level)
    {
        Bounds b = level.rooms[0].worldBounds;
        foreach (GeneratedRoom room in level.rooms) b.Encapsulate(room.worldBounds);
        return b;
    }

    private static void SetField(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (info != null) info.SetValue(target, value);
    }

    private IEnumerator SecondsOrCleared(GeneratedRoom room, float seconds)
    {
        int frames = Mathf.RoundToInt(seconds * Fps);
        int clearedAt = -1;
        for (int i = 0; i < frames; i++)
        {
            if (clearedAt < 0 && dungeon.AliveEnemiesInRoom(room.id) == 0) clearedAt = i;
            if (clearedAt >= 0 && i - clearedAt > Fps * 1.5f && i > Fps * 7) break;
            yield return null;
        }
    }

    private static IEnumerator Seconds(float seconds) => Frames(Mathf.RoundToInt(seconds * Fps));

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        int frames = Mathf.RoundToInt(timeoutSeconds * Fps);
        for (int i = 0; i < frames && !condition(); i++) yield return null;
    }

    private void SetHud(bool visible)
    {
        hudVisible = visible;
        GameHud hud = FindAnyObjectByType<GameHud>();
        if (hud != null)
        {
            foreach (Canvas canvas in hud.GetComponentsInChildren<Canvas>(true)) canvas.enabled = visible;
        }

        Minimap minimap = FindAnyObjectByType<Minimap>();
        if (minimap != null)
        {
            foreach (Canvas canvas in minimap.GetComponentsInChildren<Canvas>(true)) canvas.enabled = visible;
        }
    }

    private void SetCamGame()
    {
        camMode = CamMode.Game;
        if (brain != null) brain.enabled = true;
    }

    private void SnapGameCamera()
    {
        if (Subterraneorum.CameraSystem.CameraManager.Instance != null) Subterraneorum.CameraSystem.CameraManager.Instance.SnapToTargets();
    }

    private void SetCamFollow(Vector3 offset, float fov, bool includeEnemies, Transform focus)
    {
        camMode = CamMode.Follow;
        if (brain != null) brain.enabled = false;
        followOffset = offset;
        followFov = fov;
        followIncludeEnemies = includeEnemies;
        followFocus = focus;
        Vector3 anchor = FollowAnchor();
        camPos = anchor + offset;
        camLook = anchor + Vector3.up;
        ApplyCamera(camPos, camLook, fov);
    }

    private void SetCamPath(Vector3 fromPos, Vector3 toPos, Vector3 fromLook, Vector3 toLook, float fromFov, float toFov, float seconds)
    {
        camMode = CamMode.Path;
        if (brain != null) brain.enabled = false;
        pathFromPos = fromPos;
        pathToPos = toPos;
        pathFromLook = fromLook;
        pathToLook = toLook;
        pathFromFov = fromFov;
        pathToFov = toFov;
        pathFrames = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
        pathFrame = 0;
        ApplyCamera(fromPos, fromLook, fromFov);
    }

    private Vector3 FollowAnchor()
    {
        Vector3 center = PartyCenter();
        if (followFocus != null) return Vector3.Lerp(center, followFocus.position, 0.4f);
        if (!followIncludeEnemies) return center;

        Vector3 sum = center * 3f;
        float weight = 3f;
        foreach (EnemyStatus enemy in EnemyStatus.All)
        {
            if (enemy == null || enemy.IsDead) continue;
            if (Flat(enemy.transform.position - center).sqrMagnitude > 100f) continue;
            sum += enemy.transform.position;
            weight += 1f;
        }

        return sum / weight;
    }

    private void UpdateCamera()
    {
        if (cam == null) return;
        switch (camMode)
        {
            case CamMode.Follow:
                Vector3 anchor = FollowAnchor();
                float k = 1f - Mathf.Exp(-3f / Fps);
                camPos = Vector3.Lerp(camPos, anchor + followOffset, k);
                camLook = Vector3.Lerp(camLook, anchor + Vector3.up, k);
                ApplyCamera(camPos, camLook, followFov);
                break;
            case CamMode.Path:
                float t = Mathf.Clamp01(pathFrame / (float)pathFrames);
                float e = t * t * (3f - 2f * t);
                ApplyCamera(Vector3.Lerp(pathFromPos, pathToPos, e), Vector3.Lerp(pathFromLook, pathToLook, e), Mathf.Lerp(pathFromFov, pathToFov, e));
                pathFrame++;
                break;
        }
    }

    private void ApplyCamera(Vector3 position, Vector3 look, float fov)
    {
        if (cam == null) return;
        cam.transform.position = position;
        Vector3 forward = look - position;
        if (forward.sqrMagnitude > 0.0001f) cam.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        cam.fieldOfView = fov;
    }

    private void SetupCapture()
    {
        cam = Camera.main;
        brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        sceneRT = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        sceneRT.Create();
        uiRT = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        uiRT.Create();
        if (cam != null) cam.targetTexture = sceneRT;

        var uiGo = new GameObject("TrailerUiCamera");
        DontDestroyOnLoad(uiGo);
        uiGo.transform.position = new Vector3(0f, -10000f, 0f);
        uiCam = uiGo.AddComponent<Camera>();
        uiCam.clearFlags = CameraClearFlags.SolidColor;
        uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        uiCam.nearClipPlane = 0.1f;
        uiCam.farClipPlane = 40f;
        uiCam.depth = 50f;
        uiCam.targetTexture = uiRT;
        UniversalAdditionalCameraData data = uiCam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;

        readScene = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
        readUi = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
        frame = new byte[Width * Height * 3];
        StartCoroutine(CaptureLoop());
    }

    private void ConvertCanvases()
    {
        if (uiCam == null) return;
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCam;
            canvas.planeDistance = 10f;
        }
    }

    private void BeginShot(string name)
    {
        shotName = name;
        shotFrames = 0;
        string video = Path.Combine(outDir, name + ".mp4");
        var info = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {Width}x{Height} -r {Fps} -i - -c:v libx264 -preset veryfast -crf 13 -pix_fmt yuv420p \"{video}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        encoder = System.Diagnostics.Process.Start(info);
        encoderInput = encoder.StandardInput.BaseStream;

        recording = true;
        Debug.Log($"[Trailer] >> {name} (t={Time.time:0.00}, unscaled={Time.unscaledTime:0.00})");
    }

    private IEnumerator EndShot()
    {
        yield return new WaitForEndOfFrame();
        recording = false;
        try
        {
            encoderInput.Flush();
            encoderInput.Close();
            encoder.WaitForExit();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Trailer] ffmpeg: {e.Message}");
        }

        Debug.Log($"[Trailer] << {shotName}: {shotFrames} quadros ({shotFrames / (float)Fps:0.00}s), unscaled={Time.unscaledTime:0.00}");
    }

    private IEnumerator CaptureLoop()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            if (!recording) continue;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = sceneRT;
            readScene.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            RenderTexture.active = uiRT;
            readUi.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            RenderTexture.active = previous;

            NativeArray<byte> scene = readScene.GetRawTextureData<byte>();
            NativeArray<byte> ui = readUi.GetRawTextureData<byte>();
            int rowBytes = Width * 4;
            for (int y = 0; y < Height; y++)
            {
                int src = (Height - 1 - y) * rowBytes;
                int dst = y * Width * 3;
                for (int x = 0; x < Width; x++)
                {
                    int s = src + x * 4;
                    int a = ui[s + 3];
                    if (a == 0)
                    {
                        frame[dst] = scene[s];
                        frame[dst + 1] = scene[s + 1];
                        frame[dst + 2] = scene[s + 2];
                    }
                    else
                    {
                        int inv = 255 - a;
                        frame[dst] = (byte)Mathf.Min(255, ui[s] + scene[s] * inv / 255);
                        frame[dst + 1] = (byte)Mathf.Min(255, ui[s + 1] + scene[s + 1] * inv / 255);
                        frame[dst + 2] = (byte)Mathf.Min(255, ui[s + 2] + scene[s + 2] * inv / 255);
                    }

                    dst += 3;
                }
            }

            try
            {
                encoderInput.Write(frame, 0, frame.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Trailer] Falha ao enviar quadro: {e.Message}");
                recording = false;
            }

            shotFrames++;
        }
    }

    private void OnDestroy()
    {
        if (recording)
        {
            recording = false;
            try
            {
                encoderInput?.Close();
            }
            catch (Exception)
            {
            }
        }
    }
}
