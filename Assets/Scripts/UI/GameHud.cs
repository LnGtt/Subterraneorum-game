using System.Collections.Generic;
using Subterraneorum.DungeonSystem;
using UnityEngine;
using UnityEngine.UI;

public class GameHud : MonoBehaviour
{
    private const int MaxSlots = 4;
    private const float CardWidth = 460f;
    private const float CardHeight = 178f;

    private RectTransform root;
    private readonly PlayerSlot[] slots = new PlayerSlot[MaxSlots];

    private RectTransform bossRoot;
    private Image bossFill;
    private Text bossName;
    private Text bossHpText;
    private int shownBossHp = -1;
    private EnemyStatus boss;

    private Text banner;
    private float bannerUntil;
    private readonly List<PlayerSeat> seatBuffer = new List<PlayerSeat>(MaxSlots);
    private static readonly System.Comparison<PlayerSeat> ByIndex = (a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex);

    private void Awake()
    {
        root = UiFactory.Canvas(transform, "HudCanvas", 20);
        const float spacing = 12f;
        float total = MaxSlots * CardWidth + (MaxSlots - 1) * spacing;
        for (int i = 0; i < MaxSlots; i++)
        {
            float x = -total * 0.5f + CardWidth * 0.5f + i * (CardWidth + spacing);
            slots[i] = new PlayerSlot(root, i, new Vector2(x, 14f), new Vector2(CardWidth, CardHeight));
        }

        BuildBossBar();
        gameObject.AddComponent<Minimap>().Build(root);
        banner = UiFactory.Label(root, "Banner", "", 56, UiFactory.Gold, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(1600f, 140f));
        banner.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        DungeonManager.OnBossSpawned += ShowBoss;
        DungeonManager.OnLevelStarted += ShowLevelBanner;
        CardDraftManager.OnCardChosen += ShowCardToast;
    }

    private void OnDisable()
    {
        DungeonManager.OnBossSpawned -= ShowBoss;
        DungeonManager.OnLevelStarted -= ShowLevelBanner;
        CardDraftManager.OnCardChosen -= ShowCardToast;
        foreach (PlayerSlot slot in slots) slot?.Bind(null);
    }

    private void Update()
    {
        List<PlayerSeat> seats = seatBuffer;
        seats.Clear();
        IReadOnlyList<PlayerSeat> all = PlayerSeat.All;
        for (int i = 0; i < all.Count; i++)
        {
            PlayerSeat seat = all[i];
            if (seat != null && seat.HasCharacter) seats.Add(seat);
        }

        if (seats.Count > 1) seats.Sort(ByIndex);
        for (int i = 0; i < MaxSlots; i++)
        {
            slots[i].Bind(i < seats.Count ? seats[i] : null);
            slots[i].Refresh();
        }

        UpdateBoss();

        if (banner.gameObject.activeSelf && Time.unscaledTime > bannerUntil) banner.gameObject.SetActive(false);
    }

    private void ShowLevelBanner(int levelIndex)
    {
        DungeonManager dungeon = FindAnyObjectByType<DungeonManager>();
        int count = dungeon != null ? dungeon.LevelCount : levelIndex + 1;
        bool final = dungeon != null ? dungeon.IsOnFinalLevel : true;
        string floor = $"Nível {levelIndex + 1} de {count}";

        string text = levelIndex == 0 ? $"{floor} — As Ruínas"
            : final ? $"{floor} — o chefão aguarda nas profundezas"
            : $"{floor} — descendo para as profundezas";
        ShowBanner(text, 3f);
    }

    private void ShowCardToast(CardDefinition card)
    {
        ShowBanner($"Carta: {card.title}\n<size=34>{card.effectText}</size>", 2.5f);
    }

    public void ShowBanner(string text, float seconds)
    {
        banner.text = text;
        banner.gameObject.SetActive(true);
        bannerUntil = Time.unscaledTime + seconds;
    }

    private void BuildBossBar()
    {
        UiTheme theme = UiTheme.Current;
        Image panel = UiFactory.Panel(root, "BossBar", theme != null ? theme.panelRed : null, new Color(1f, 1f, 1f, 0.95f));
        bossRoot = panel.rectTransform;
        UiFactory.Place(bossRoot, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1000f, 90f));
        bossName = UiFactory.Label(bossRoot, "Name", "", 30, UiFactory.TextLight, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Place(bossName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(960f, 36f));
        bossFill = UiFactory.Bar(bossRoot, "Health", new Color(0.85f, 0.12f, 0.12f), out RectTransform barRoot);
        UiFactory.Place(barRoot, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(940f, 30f), new Vector2(0.5f, 0f));
        bossHpText = UiFactory.Label(barRoot, "Value", "", 20, Color.white, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Stretch(bossHpText.rectTransform);
        bossRoot.gameObject.SetActive(false);
    }

    private void ShowBoss(EnemyStatus status)
    {
        boss = status;
        shownBossHp = -1;
        BossController controller = status != null ? status.GetComponent<BossController>() : null;
        bossName.text = controller != null ? controller.DisplayName : "Chefão";
        bossRoot.gameObject.SetActive(true);
        bossRoot.SetAsLastSibling();
        ShowBanner(controller != null ? controller.DisplayName.ToUpperInvariant() : "CHEFÃO", 2.5f);
        Debug.Log($"[GameHud] Barra do chefão visível: {bossName.text}, vida {(status != null ? status.MaxHealth : 0f):0}.");
    }

    private void UpdateBoss()
    {
        if (!bossRoot.gameObject.activeSelf) return;
        if (boss == null || boss.IsDead)
        {
            bossRoot.gameObject.SetActive(false);
            return;
        }

        UiFactory.SetBar(bossFill, boss.MaxHealth > 0f ? boss.CurrentHealth / boss.MaxHealth : 0f);

        int hp = Mathf.CeilToInt(boss.CurrentHealth);
        if (hp != shownBossHp)
        {
            shownBossHp = hp;
            bossHpText.text = $"{hp} / {Mathf.CeilToInt(boss.MaxHealth)}";
        }
    }

    private sealed class PlayerSlot
    {
        private readonly RectTransform panel;
        private readonly Image portrait;
        private readonly Text initial;
        private readonly Text levelBadge;
        private readonly Text nameText;
        private readonly Text roleText;
        private readonly Image hpFill;
        private readonly Text hpText;
        private readonly Image xpFill;
        private readonly Text xpText;
        private readonly RectTransform pointsBanner;
        private readonly Text pointsText;
        private readonly Text downedText;
        private readonly SkillBox dash;
        private readonly SkillBox ability1;
        private readonly SkillBox ability2;

        private readonly RectTransform skillMenu;
        private readonly RectTransform skillDim;
        private readonly Text skillTitle;
        private readonly Text[] skillLines = new Text[PlayerSkills.Nodes.Length];
        private int skillCursor;
        private bool skillMenuOpen;
        private int skillMenuOpenedFrame = -1;

        private bool PausedByOther => GamePause.IsPaused && !ReferenceEquals(GamePause.Owner, this);

        private PlayerSeat seat;
        private PlayerStatus status;
        private PlayerProgression progression;
        private PlayerMovement movement;
        private PlayerAbilities abilities;
        private PlayerSkills skills;
        private PlayerIdentity identity;
        private PlayerInputHandler input;

        private int shownLevel = -1;
        private int shownHp = -1;
        private int shownMaxHp = -1;
        private int shownXp = -1;
        private int shownXpMax = -1;
        private int shownPoints = -1;

        public PlayerSlot(RectTransform parent, int index, Vector2 position, Vector2 size)
        {
            UiTheme theme = UiTheme.Current;
            Image back = UiFactory.Panel(parent, $"PlayerHud_{index + 1}", theme != null ? theme.panelDark : null, new Color(1f, 1f, 1f, 0.94f));
            panel = back.rectTransform;
            UiFactory.Place(panel, new Vector2(0.5f, 0f), position, size, new Vector2(0.5f, 0f));

            portrait = UiFactory.Box(panel, "Portrait", Color.gray);
            UiFactory.Place(portrait.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(84f, 84f), new Vector2(0f, 1f));
            initial = UiFactory.Label(portrait.rectTransform, "Initial", "", 54, Color.white, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(initial.rectTransform);
            Image badge = UiFactory.Box(panel, "LevelBadge", new Color(0f, 0f, 0f, 0.55f));
            UiFactory.Place(badge.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -102f), new Vector2(84f, 30f), new Vector2(0f, 1f));
            levelBadge = UiFactory.Label(badge.rectTransform, "Level", "Nv 1", 20, UiFactory.Gold, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(levelBadge.rectTransform);

            const float left = 112f;
            const float right = 14f;
            float column = size.x - left - right;

            nameText = UiFactory.Label(panel, "Name", "", 21, UiFactory.TextLight, TextAnchor.MiddleLeft, bold: true);
            UiFactory.Place(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(left, -12f), new Vector2(column - 110f, 28f), new Vector2(0f, 1f));
            roleText = UiFactory.Label(panel, "Role", "", 18, Color.white, TextAnchor.MiddleRight, bold: true);
            UiFactory.Place(roleText.rectTransform, new Vector2(1f, 1f), new Vector2(-right, -12f), new Vector2(120f, 28f), new Vector2(1f, 1f));

            hpFill = UiFactory.Bar(panel, "Health", new Color(0.82f, 0.16f, 0.16f), out RectTransform hpRoot);
            UiFactory.Place(hpRoot, new Vector2(0f, 1f), new Vector2(left, -44f), new Vector2(column, 24f), new Vector2(0f, 1f));
            hpText = UiFactory.Label(hpRoot, "Value", "", 17, Color.white, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(hpText.rectTransform);

            xpFill = UiFactory.Bar(panel, "XP", new Color(0.3f, 0.55f, 1f), out RectTransform xpRoot);
            UiFactory.Place(xpRoot, new Vector2(0f, 1f), new Vector2(left, -72f), new Vector2(column, 16f), new Vector2(0f, 1f));
            xpText = UiFactory.Label(xpRoot, "Value", "", 13, Color.white, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(xpText.rectTransform);

            float boxWidth = (column - 12f) / 3f;
            dash = new SkillBox(panel, "Esquiva", new Vector2(left, 12f), new Vector2(boxWidth, 66f));
            ability1 = new SkillBox(panel, "Habilidade1", new Vector2(left + boxWidth + 6f, 12f), new Vector2(boxWidth, 66f));
            ability2 = new SkillBox(panel, "Habilidade2", new Vector2(left + (boxWidth + 6f) * 2f, 12f), new Vector2(boxWidth, 66f));

            Image pointsBack = UiFactory.Panel(parent, $"SkillPoints_{index + 1}", theme != null ? theme.panelLight : null, new Color(1f, 0.92f, 0.6f, 1f));
            pointsBanner = pointsBack.rectTransform;
            UiFactory.Place(pointsBanner, new Vector2(0.5f, 0f), position + new Vector2(0f, size.y + 4f), new Vector2(size.x, 34f), new Vector2(0.5f, 0f));
            pointsText = UiFactory.Label(pointsBanner, "Text", "", 18, UiFactory.TextDark, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(pointsText.rectTransform);
            pointsBanner.gameObject.SetActive(false);

            downedText = UiFactory.Label(panel, "Downed", "CAÍDO\nUm aliado precisa segurar E / Y perto de você", 22, UiFactory.Bad, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(downedText.rectTransform);
            Image downedBack = UiFactory.Box(downedText.rectTransform, "Back", new Color(0f, 0f, 0f, 0.75f));
            UiFactory.Stretch(downedBack.rectTransform);
            downedBack.transform.SetAsFirstSibling();
            downedText.gameObject.SetActive(false);

            Image dim = UiFactory.Box(parent, $"SkillTreeDim_{index + 1}", new Color(0.02f, 0.01f, 0.04f, 0.7f));
            skillDim = dim.rectTransform;
            UiFactory.Stretch(skillDim);
            skillDim.gameObject.SetActive(false);

            const float menuWidth = 760f;
            const float lineHeight = 78f;
            float menuHeight = 110f + skillLines.Length * lineHeight + 50f;
            Image menuBack = UiFactory.Panel(parent, $"SkillTree_{index + 1}", theme != null ? theme.panelLight : null, Color.white);
            skillMenu = menuBack.rectTransform;
            UiFactory.Place(skillMenu, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(menuWidth, menuHeight), new Vector2(0.5f, 0.5f));
            skillTitle = UiFactory.Label(skillMenu, "Title", "", 30, UiFactory.TextDark, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Place(skillTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(menuWidth - 40f, 70f), new Vector2(0.5f, 1f));
            for (int i = 0; i < skillLines.Length; i++)
            {
                skillLines[i] = UiFactory.Label(skillMenu, $"Node_{i}", "", 23, UiFactory.TextDark, TextAnchor.MiddleLeft);
                UiFactory.Place(skillLines[i].rectTransform, new Vector2(0f, 1f), new Vector2(36f, -110f - i * lineHeight), new Vector2(menuWidth - 72f, lineHeight - 6f), new Vector2(0f, 1f));
            }

            Text help = UiFactory.Label(skillMenu, "Help", "Cima/baixo escolhe · Confirmar compra · Voltar ou Tab/Select fecha (jogo pausado)", 18, new Color(0.35f, 0.25f, 0.15f));
            UiFactory.Place(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(menuWidth - 40f, 30f), new Vector2(0.5f, 0f));
            skillMenu.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
        }

        public void Bind(PlayerSeat newSeat)
        {
            if (newSeat == seat && (seat == null || status != null)) return;

            CloseSkillMenu();
            if (input != null) input.OnSkillsInput -= ToggleSkillMenu;

            seat = newSeat;
            GameObject character = seat != null ? seat.Character : null;
            status = character != null ? character.GetComponent<PlayerStatus>() : null;
            progression = character != null ? character.GetComponent<PlayerProgression>() : null;
            movement = character != null ? character.GetComponent<PlayerMovement>() : null;
            abilities = character != null ? character.GetComponent<PlayerAbilities>() : null;
            skills = character != null ? character.GetComponent<PlayerSkills>() : null;
            identity = character != null ? character.GetComponent<PlayerIdentity>() : null;
            input = character != null ? character.GetComponent<PlayerInputHandler>() : null;
            if (input != null) input.OnSkillsInput += ToggleSkillMenu;

            panel.gameObject.SetActive(status != null);
            if (status == null) pointsBanner.gameObject.SetActive(false);
            shownLevel = shownHp = shownMaxHp = shownXp = shownXpMax = shownPoints = -1;
            if (identity == null || identity.Class == null) return;

            ClassDefinition def = identity.Class;
            portrait.color = def.color;
            initial.text = def.displayName.Substring(0, 1);
            nameText.text = $"J{seat.PlayerIndex + 1} · {def.displayName}";
            roleText.text = def.roleName;
            roleText.color = Color.Lerp(def.color, Color.white, 0.35f);

            bool pad = seat.UsesGamepad;
            dash.Setup("Esquiva", pad ? "A" : "Espaço");
            ability1.Setup(def.ability1 != null ? def.ability1.displayName : "Habilidade 1", pad ? "B" : "K");
            ability2.Setup(def.ability2 != null ? def.ability2.displayName : "Habilidade 2", pad ? "LB" : "L");
        }

        public void Refresh()
        {
            if (status == null) return;

            int level = progression != null ? progression.CurrentLevel : 1;
            if (level != shownLevel)
            {
                shownLevel = level;
                levelBadge.text = $"Nv {level}";
            }

            UiFactory.SetBar(hpFill, status.MaxHealth > 0f ? status.CurrentHealth / status.MaxHealth : 0f);
            int hp = Mathf.CeilToInt(status.CurrentHealth);
            int maxHp = Mathf.CeilToInt(status.MaxHealth);
            if (hp != shownHp || maxHp != shownMaxHp)
            {
                shownHp = hp;
                shownMaxHp = maxHp;
                hpText.text = $"Vida  {hp} / {maxHp}";
            }

            if (progression != null)
            {
                UiFactory.SetBar(xpFill, progression.XPToNextLevel > 0f ? progression.CurrentXP / progression.XPToNextLevel : 0f);
                int xp = Mathf.FloorToInt(progression.CurrentXP);
                int xpMax = Mathf.CeilToInt(progression.XPToNextLevel);
                if (xp != shownXp || xpMax != shownXpMax)
                {
                    shownXp = xp;
                    shownXpMax = xpMax;
                    xpText.text = $"XP  {xp} / {xpMax}";
                }
            }

            if (movement != null) dash.Set(movement.DashCooldownRemaining, movement.DashCooldown, true);
            if (abilities != null)
            {
                ability1.Set(abilities.CooldownRemaining(0), abilities.CooldownDuration(0), abilities.IsUnlocked(0));
                ability2.Set(abilities.CooldownRemaining(1), abilities.CooldownDuration(1), abilities.IsUnlocked(1));
            }

            int points = skills != null ? skills.Points : 0;
            if (points != shownPoints)
            {
                shownPoints = points;
                pointsBanner.gameObject.SetActive(points > 0);
                string key = seat.UsesGamepad ? "Select" : "Tab";
                pointsText.text = points == 1
                    ? $"+1 ponto de habilidade — aperte {key}"
                    : $"+{points} pontos de habilidade — aperte {key}";
                if (skillMenuOpen) RefreshSkillMenu();
            }

            if (downedText.gameObject.activeSelf != status.IsDead) downedText.gameObject.SetActive(status.IsDead);

            if (skillMenuOpen && Time.frameCount != skillMenuOpenedFrame && input != null && input.SkillsPressedThisFrame) CloseSkillMenu();

            if (skillMenuOpen && PausedByOther) CloseSkillMenu();
        }

        private void ToggleSkillMenu()
        {
            if (skillMenuOpen) CloseSkillMenu();
            else OpenSkillMenu();
        }

        private void OpenSkillMenu()
        {
            if (skillMenuOpen || seat == null || skills == null) return;
            if (!GamePause.TryAcquire(this)) return;

            skillMenuOpen = true;
            skillMenuOpenedFrame = Time.frameCount;
            skillCursor = 0;
            seat.PushMenu();
            seat.NavigateRequested += HandleNavigate;
            seat.ConfirmRequested += HandleConfirm;
            seat.CancelRequested += HandleCancel;
            skillDim.gameObject.SetActive(true);
            skillMenu.gameObject.SetActive(true);
            skillDim.SetAsLastSibling();
            skillMenu.SetAsLastSibling();
            pointsBanner.gameObject.SetActive(false);
            RefreshSkillMenu();
        }

        private void CloseSkillMenu()
        {
            if (!skillMenuOpen) return;
            skillMenuOpen = false;
            if (seat != null)
            {
                seat.NavigateRequested -= HandleNavigate;
                seat.ConfirmRequested -= HandleConfirm;
                seat.CancelRequested -= HandleCancel;
                seat.PopMenu();
            }

            skillMenu.gameObject.SetActive(false);
            skillDim.gameObject.SetActive(false);
            GamePause.Release(this);
            shownPoints = -1;
        }

        private void HandleNavigate(PlayerSeat _, Vector2Int direction)
        {
            if (direction.y == 0 || PausedByOther) return;
            skillCursor = (skillCursor - direction.y + skillLines.Length) % skillLines.Length;
            GameAudio.Play(GameSound.UiMove);
            RefreshSkillMenu();
        }

        private void HandleConfirm(PlayerSeat _)
        {
            if (PausedByOther) return;
            skills.TryBuy(PlayerSkills.Nodes[skillCursor]);
            RefreshSkillMenu();
        }

        private void HandleCancel(PlayerSeat _)
        {
            if (!PausedByOther) CloseSkillMenu();
        }

        private void RefreshSkillMenu()
        {
            string who = seat != null ? $"J{seat.PlayerIndex + 1}" : "";
            if (identity != null && identity.Class != null) who += $" · {identity.Class.displayName}";
            skillTitle.text = $"{who} — Árvore de habilidades\n<size=22>{skills.Points} ponto(s) para gastar</size>";
            for (int i = 0; i < skillLines.Length; i++)
            {
                PlayerSkills.Node node = PlayerSkills.Nodes[i];
                string state = skills.Owns(node) ? "<color=#1d7a1d>[comprado]</color>"
                    : !skills.Requirement(node) ? "<color=#8a3b2a>[precisa da hab. 2]</color>"
                    : skills.Points > 0 ? "<color=#7a5a10>[1 ponto]</color>" : "<color=#6b6b6b>[sem pontos]</color>";
                string cursor = i == skillCursor ? "> " : "   ";
                skillLines[i].text = $"{cursor}<b>{skills.Title(node)}</b> {state}\n   <size=15>{skills.Description(node)}</size>";
            }
        }
    }

    private sealed class SkillBox
    {
        private readonly Image back;
        private readonly Image shade;
        private readonly Text nameText;
        private readonly Text keyText;
        private readonly Text timerText;
        private string keyName = "";
        private int state = -1;
        private int shownTenths = -1;

        private static readonly Color Ready = new Color(0.22f, 0.3f, 0.38f, 1f);
        private static readonly Color Locked = new Color(0.16f, 0.16f, 0.18f, 1f);

        public SkillBox(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            back = UiFactory.Box(parent, $"Skill_{name}", Ready);
            UiFactory.Place(back.rectTransform, Vector2.zero, position, size, Vector2.zero);

            nameText = UiFactory.Label(back.rectTransform, "Name", name, 15, UiFactory.TextLight, TextAnchor.UpperCenter, bold: true);
            UiFactory.Region(nameText.rectTransform, new Vector2(0f, 0.38f), Vector2.one);
            nameText.rectTransform.offsetMin = new Vector2(3f, 0f);
            nameText.rectTransform.offsetMax = new Vector2(-3f, -3f);

            keyText = UiFactory.Label(back.rectTransform, "Key", "", 14, UiFactory.Gold, TextAnchor.LowerCenter, bold: true);
            UiFactory.Region(keyText.rectTransform, Vector2.zero, new Vector2(1f, 0.4f));
            keyText.rectTransform.offsetMin = new Vector2(2f, 3f);

            shade = UiFactory.Box(back.rectTransform, "Shade", new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Stretch(shade.rectTransform);
            shade.rectTransform.pivot = new Vector2(0.5f, 0f);
            shade.rectTransform.localScale = new Vector3(1f, 0f, 1f);

            timerText = UiFactory.Label(back.rectTransform, "Timer", "", 24, Color.white, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(timerText.rectTransform);
            timerText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            timerText.gameObject.SetActive(false);
        }

        public void Setup(string displayName, string key)
        {
            nameText.text = displayName;
            keyName = key;
            keyText.text = $"[{key}]";
            state = -1;
        }

        public void Set(float remaining, float duration, bool unlocked)
        {
            int newState = !unlocked ? 2 : remaining > 0.05f ? 1 : 0;
            if (newState != state)
            {
                state = newState;
                back.color = newState == 2 ? Locked : Ready;
                keyText.text = newState == 2 ? "Tab: destravar" : $"[{keyName}]";
                keyText.color = newState == 2 ? new Color(0.75f, 0.75f, 0.75f) : UiFactory.Gold;
                nameText.color = newState == 2 ? new Color(0.6f, 0.6f, 0.6f) : UiFactory.TextLight;
                timerText.gameObject.SetActive(newState == 1);
                if (newState == 2)
                {
                    timerText.gameObject.SetActive(true);
                    timerText.text = "Bloqueada";
                    timerText.fontSize = 15;
                }
                else
                {
                    timerText.fontSize = 24;
                }

                shownTenths = -1;
            }

            if (newState == 1)
            {
                float t = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;
                shade.rectTransform.localScale = new Vector3(1f, t, 1f);
                int tenths = Mathf.CeilToInt(remaining * 10f);
                if (tenths != shownTenths)
                {
                    shownTenths = tenths;
                    timerText.text = remaining >= 1f ? Mathf.CeilToInt(remaining).ToString() : (tenths / 10f).ToString("0.0");
                }
            }
            else
            {
                shade.rectTransform.localScale = new Vector3(1f, newState == 2 ? 1f : 0f, 1f);
            }
        }
    }
}
