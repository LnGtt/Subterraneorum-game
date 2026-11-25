using System;
using System.Collections.Generic;
using Subterraneorum.DungeonSystem;
using UnityEngine;
using UnityEngine.UI;

public class CardDraftManager : MonoBehaviour
{
    public enum Phase
    {
        Idle,
        Voting,
        Result,
        ChallengeOffer,
        DuelWait,
        DuelGo,
        DuelResult,
    }

    [SerializeField] private CardDefinition[] deck = Array.Empty<CardDefinition>();
    [SerializeField] private float resultSeconds = 2.2f;
    [SerializeField] private float challengeWindowSeconds = 6f;
    [SerializeField] private Vector2 duelDelay = new Vector2(1.5f, 3.5f);
    [SerializeField] private float duelResultSeconds = 2.5f;
    [Tooltip("Espera (s, tempo real) entre o efeito de subir de nível e a tela de cartas.")]
    [SerializeField] private float levelUpRevealDelay = LevelUpEffect.Duration;

    private readonly Queue<(PlayerProgression player, int level, float readyAt)> pending = new Queue<(PlayerProgression, int, float)>();
    private readonly HashSet<PlayerProgression> subscribed = new HashSet<PlayerProgression>();
    private readonly List<CardDefinition> offered = new List<CardDefinition>();
    private readonly List<PlayerSeat> voters = new List<PlayerSeat>();
    private readonly Dictionary<PlayerSeat, int> cursors = new Dictionary<PlayerSeat, int>();
    private readonly Dictionary<PlayerSeat, int> votes = new Dictionary<PlayerSeat, int>();
    private readonly HashSet<PlayerSeat> passed = new HashSet<PlayerSeat>();

    private Phase phase;
    private PlayerProgression leveler;
    private int newLevel;
    private PlayerSeat levelerSeat;
    private PlayerSeat challenger;
    private float phaseEndsAt;
    private string message;
    private DungeonManager dungeon;
    private int shownSeconds = -1;

    private RectTransform root;
    private Text titleText;
    private Text subtitleText;
    private Text statusText;
    private Text duelText;
    private RectTransform cardsRow;
    private readonly List<CardView> cardViews = new List<CardView>();

    public static event Action<CardDefinition> OnCardChosen;

    public Phase CurrentPhase => phase;
    public bool IsOpen => phase != Phase.Idle;
    public int OfferedCount => offered.Count;
    public string LastResult { get; private set; }

    private void Awake()
    {
        dungeon = FindAnyObjectByType<DungeonManager>();
        BuildUI();
    }

    private void OnEnable()
    {
        PlayerRegistry.OnPlayerRegistered += Subscribe;
        PlayerRegistry.OnPlayerUnregistered += Unsubscribe;
        foreach (PlayerStatus player in PlayerRegistry.Players) Subscribe(player);
    }

    private void OnDisable()
    {
        PlayerRegistry.OnPlayerRegistered -= Subscribe;
        PlayerRegistry.OnPlayerUnregistered -= Unsubscribe;
        foreach (PlayerProgression progression in subscribed)
        {
            if (progression != null)
            {
                progression.OnLevelUp -= Enqueue;
            }
        }

        subscribed.Clear();
        if (IsOpen) Close();
    }

    private void Subscribe(PlayerStatus player)
    {
        PlayerProgression progression = player != null ? player.GetComponent<PlayerProgression>() : null;
        if (progression != null && subscribed.Add(progression)) progression.OnLevelUp += Enqueue;
    }

    private void Unsubscribe(PlayerStatus player)
    {
        PlayerProgression progression = player != null ? player.GetComponent<PlayerProgression>() : null;
        if (progression != null && subscribed.Remove(progression)) progression.OnLevelUp -= Enqueue;
    }

    private void Enqueue(PlayerProgression player, int level)
    {
        pending.Enqueue((player, level, Time.unscaledTime + levelUpRevealDelay));
    }

    private void Update()
    {
        switch (phase)
        {
            case Phase.Idle:
                TryStartNext();
                break;
            case Phase.Result:
                if (Time.unscaledTime >= phaseEndsAt) BeginChallengeOffer();
                break;
            case Phase.ChallengeOffer:
                if (Time.unscaledTime >= phaseEndsAt) Close();
                else if (Mathf.CeilToInt(phaseEndsAt - Time.unscaledTime) != shownSeconds) RefreshStatus();
                break;
            case Phase.DuelWait:
                if (Time.unscaledTime >= phaseEndsAt)
                {
                    phase = Phase.DuelGo;
                    duelText.text = UiFactory.Colored("JÁ!", UiFactory.Gold);
                }

                break;
            case Phase.DuelResult:
                if (Time.unscaledTime >= phaseEndsAt) Close();
                break;
        }
    }

    private void TryStartNext()
    {
        if (pending.Count == 0 || deck.Length == 0) return;
        if (dungeon != null && dungeon.RunEnded)
        {
            pending.Clear();
            return;
        }

        (PlayerProgression player, int level, float readyAt) next = pending.Peek();
        if (next.player == null)
        {
            pending.Dequeue();
            return;
        }

        if (Time.unscaledTime < next.readyAt) return;

        if (!GamePause.TryAcquire(this)) return;
        pending.Dequeue();

        leveler = next.player;
        newLevel = next.level;
        PlayerIdentity identity = leveler.GetComponent<PlayerIdentity>();
        levelerSeat = identity != null ? identity.Seat : null;

        voters.Clear();
        foreach (PlayerSeat seat in PlayerSeat.All)
        {
            if (seat != null && seat.HasCharacter) voters.Add(seat);
        }

        offered.Clear();
        var pool = new List<CardDefinition>(deck);
        int count = Mathf.Clamp(voters.Count + 1, 2, pool.Count);
        for (int i = 0; i < count; i++)
        {
            int pick = UnityEngine.Random.Range(0, pool.Count);
            offered.Add(pool[pick]);
            pool.RemoveAt(pick);
        }

        cursors.Clear();
        votes.Clear();
        passed.Clear();
        challenger = null;
        message = null;
        for (int i = 0; i < voters.Count; i++)
        {
            PlayerSeat seat = voters[i];
            cursors[seat] = Mathf.Min(i, offered.Count - 1);
            seat.PushMenu();
            seat.NavigateRequested += HandleNavigate;
            seat.ConfirmRequested += HandleConfirm;
            seat.CancelRequested += HandleCancel;
        }

        phase = Phase.Voting;
        GameAudio.Play(GameSound.CardFlip);
        Debug.Log($"[CardDraft] {LabelOf(levelerSeat)} subiu para o nível {newLevel}: {offered.Count} cartas na mesa.");
        BuildCards();
        root.gameObject.SetActive(true);
        duelText.gameObject.SetActive(false);
        titleText.text = $"{ColoredLabel(levelerSeat)} subiu para o nível {newLevel}!";
        subtitleText.text = "Escolham UMA carta juntos — a escolha precisa ser unânime.\nAs cartas que sobrarem fortalecem os inimigos.";
        RefreshCards();
    }

    private void HandleNavigate(PlayerSeat seat, Vector2Int direction)
    {
        if (phase != Phase.Voting || direction.x == 0 || votes.ContainsKey(seat)) return;
        cursors[seat] = (cursors[seat] + direction.x + offered.Count) % offered.Count;
        GameAudio.Play(GameSound.UiMove);
        RefreshCards();
    }

    private void HandleConfirm(PlayerSeat seat)
    {
        switch (phase)
        {
            case Phase.Voting:
                votes[seat] = cursors[seat];
                GameAudio.Play(GameSound.UiConfirm);
                CheckConsensus();
                break;
            case Phase.ChallengeOffer:
                if (seat != levelerSeat && !passed.Contains(seat)) StartDuel(seat);
                break;
            case Phase.DuelWait:
                if (IsDuelist(seat)) FinishDuel(Opponent(seat), falseStart: true);
                break;
            case Phase.DuelGo:
                if (IsDuelist(seat)) FinishDuel(seat, falseStart: false);
                break;
        }
    }

    private void HandleCancel(PlayerSeat seat)
    {
        switch (phase)
        {
            case Phase.Voting:
                if (votes.Remove(seat))
                {
                    message = null;
                    RefreshCards();
                }

                break;
            case Phase.ChallengeOffer:
                if (seat != levelerSeat && passed.Add(seat))
                {
                    if (AllPassed()) Close();
                    else RefreshStatus();
                }

                break;
        }
    }

    private void CheckConsensus()
    {
        if (votes.Count < voters.Count)
        {
            message = null;
            RefreshCards();
            return;
        }

        int choice = -1;
        foreach (int v in votes.Values)
        {
            if (choice == -1) choice = v;
            else if (v != choice)
            {
                message = UiFactory.Colored("Os votos não batem! A escolha precisa ser unânime — use Voltar para mudar o voto.", UiFactory.Bad);
                RefreshCards();
                return;
            }
        }

        Resolve(choice);
    }

    private void Resolve(int index)
    {
        CardDefinition chosen = offered[index];
        RunEffects.Apply(chosen.effect, chosen.value);
        var leftovers = new List<string>();
        for (int i = 0; i < offered.Count; i++)
        {
            if (i == index) continue;
            RunEffects.Apply(offered[i].leftoverEffect, offered[i].leftoverValue);
            leftovers.Add(offered[i].leftoverText);
        }

        LastResult = chosen.title;
        OnCardChosen?.Invoke(chosen);
        GameAudio.Play(GameSound.CardChosen);
        Debug.Log($"[CardDraft] Carta escolhida: {chosen.title} ({chosen.effectText}). Inimigos ganham: {string.Join("; ", leftovers)}.");

        phase = Phase.Result;
        phaseEndsAt = Time.unscaledTime + resultSeconds;
        for (int i = 0; i < cardViews.Count; i++) cardViews[i].SetHighlight(i == index ? 1 : -1);
        subtitleText.text = $"{UiFactory.Colored(chosen.effectText, UiFactory.Good)}\n{UiFactory.Colored("Inimigos: " + string.Join(" · ", leftovers.ConvertAll(StripPrefix)), UiFactory.Bad)}";
        statusText.text = $"Escolhida: {chosen.title}";
    }

    private static string StripPrefix(string text) => text.StartsWith("Inimigos: ") ? text.Substring("Inimigos: ".Length) : text;

    private void BeginChallengeOffer()
    {
        bool levelerPresent = levelerSeat != null && voters.Contains(levelerSeat) && leveler != null && leveler.CurrentLevel > 1;
        if (!levelerPresent || voters.Count < 2)
        {
            Close();
            return;
        }

        phase = Phase.ChallengeOffer;
        phaseEndsAt = Time.unscaledTime + challengeWindowSeconds;
        cardsRow.gameObject.SetActive(false);
        titleText.text = $"Desafio! Alguém quer o nível do {ColoredLabel(levelerSeat)}?";
        subtitleText.text = "Confirmar = desafiar (duelo de reflexo) · Voltar = passar.\nQuem vencer fica com o XP do nível.";
        RefreshStatus();
    }

    private bool AllPassed()
    {
        foreach (PlayerSeat seat in voters)
        {
            if (seat != levelerSeat && !passed.Contains(seat)) return false;
        }

        return true;
    }

    private void StartDuel(PlayerSeat seat)
    {
        challenger = seat;
        phase = Phase.DuelWait;
        phaseEndsAt = Time.unscaledTime + UnityEngine.Random.Range(duelDelay.x, duelDelay.y);
        titleText.text = $"Duelo: {ColoredLabel(levelerSeat)} x {ColoredLabel(challenger)}";
        subtitleText.text = "Apertem Confirmar quando aparecer \"JÁ!\". Apertar antes perde.";
        statusText.text = "";
        duelText.gameObject.SetActive(true);
        duelText.text = "Preparar...";
        Debug.Log($"[CardDraft] {LabelOf(challenger)} desafiou {LabelOf(levelerSeat)}.");
    }

    private bool IsDuelist(PlayerSeat seat) => seat == levelerSeat || seat == challenger;

    private PlayerSeat Opponent(PlayerSeat seat) => seat == levelerSeat ? challenger : levelerSeat;

    private void FinishDuel(PlayerSeat winner, bool falseStart)
    {
        phase = Phase.DuelResult;
        phaseEndsAt = Time.unscaledTime + duelResultSeconds;
        string prefix = falseStart ? $"{ColoredLabel(Opponent(winner))} apertou antes da hora!\n" : "";

        if (winner == challenger)
        {
            PlayerProgression challengerProgression = challenger.Character != null ? challenger.Character.GetComponent<PlayerProgression>() : null;
            float xp = leveler.LoseLevel();
            if (challengerProgression != null) challengerProgression.AddXP(xp, applyMultiplier: false);
            LastResult = $"duelo:{LabelOf(challenger)}";
            duelText.text = $"{prefix}{ColoredLabel(challenger)} venceu e levou {Mathf.RoundToInt(xp)} XP!\n{ColoredLabel(levelerSeat)} voltou para o nível {leveler.CurrentLevel}.";
        }
        else
        {
            LastResult = $"duelo:{LabelOf(levelerSeat)}";
            duelText.text = $"{prefix}{ColoredLabel(levelerSeat)} defendeu o nível {leveler.CurrentLevel}!";
        }

        Debug.Log($"[CardDraft] Duelo: venceu {LabelOf(winner)}{(falseStart ? " (largada queimada)" : "")}.");
    }

    private void Close()
    {
        foreach (PlayerSeat seat in voters)
        {
            if (seat == null) continue;
            seat.NavigateRequested -= HandleNavigate;
            seat.ConfirmRequested -= HandleConfirm;
            seat.CancelRequested -= HandleCancel;
            seat.PopMenu();
        }

        voters.Clear();
        phase = Phase.Idle;
        root.gameObject.SetActive(false);
        GamePause.Release(this);
    }

    private static string LabelOf(PlayerSeat seat) => seat != null ? $"J{seat.PlayerIndex + 1}" : "?";

    private static Color ColorOf(PlayerSeat seat)
    {
        if (seat == null || seat.Character == null) return Color.white;
        PlayerIdentity identity = seat.Character.GetComponent<PlayerIdentity>();
        return identity != null && identity.Class != null ? identity.Class.color : Color.white;
    }

    private static string ColoredLabel(PlayerSeat seat) => UiFactory.Colored(LabelOf(seat), ColorOf(seat));

    private void RefreshStatus()
    {
        if (phase != Phase.ChallengeOffer) return;
        var parts = new List<string>();
        foreach (PlayerSeat seat in voters)
        {
            if (seat == levelerSeat) continue;
            parts.Add($"{ColoredLabel(seat)}: {(passed.Contains(seat) ? "passou" : "decidindo")}");
        }

        int seconds = Mathf.CeilToInt(Mathf.Max(0f, phaseEndsAt - Time.unscaledTime));
        shownSeconds = seconds;
        statusText.text = $"{string.Join("   ", parts)}   ({seconds}s)";
    }

    private void BuildUI()
    {
        root = UiFactory.Canvas(transform, "CardDraftCanvas", 60);
        Image dim = UiFactory.Box(root, "Dim", new Color(0.02f, 0.01f, 0.04f, 0.82f));
        UiFactory.Stretch(dim.rectTransform);

        titleText = UiFactory.Label(root, "Title", "", 54, UiFactory.Gold, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1700f, 80f));
        subtitleText = UiFactory.Label(root, "Subtitle", "", 28, UiFactory.TextLight);
        UiFactory.Place(subtitleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -185f), new Vector2(1700f, 90f));

        cardsRow = UiFactory.Empty(root, "Cards");
        UiFactory.Place(cardsRow, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1800f, 560f));

        statusText = UiFactory.Label(root, "Status", "", 28, UiFactory.TextLight);
        UiFactory.Place(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(1800f, 80f));

        duelText = UiFactory.Label(root, "Duel", "", 72, UiFactory.TextLight, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Place(duelText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1700f, 400f));

        root.gameObject.SetActive(false);
    }

    private void BuildCards()
    {
        foreach (CardView view in cardViews) Destroy(view.root.gameObject);
        cardViews.Clear();
        cardsRow.gameObject.SetActive(true);

        const float width = 300f;
        const float spacing = 30f;
        float total = offered.Count * width + (offered.Count - 1) * spacing;
        for (int i = 0; i < offered.Count; i++)
        {
            float x = -total * 0.5f + width * 0.5f + i * (width + spacing);
            cardViews.Add(CardView.Create(cardsRow, offered[i], new Vector2(x, 0f), new Vector2(width, 540f)));
        }
    }

    private void RefreshCards()
    {
        for (int i = 0; i < cardViews.Count; i++)
        {
            var tags = new List<string>();
            bool anyVote = false;
            foreach (PlayerSeat seat in voters)
            {
                bool voted = votes.TryGetValue(seat, out int v) && v == i;
                bool hovering = !votes.ContainsKey(seat) && cursors[seat] == i;
                if (voted)
                {
                    tags.Add(UiFactory.Colored($"[{LabelOf(seat)} votou]", ColorOf(seat)));
                    anyVote = true;
                }
                else if (hovering)
                {
                    tags.Add(UiFactory.Colored($"^{LabelOf(seat)}", ColorOf(seat)));
                }
            }

            cardViews[i].SetVoters(string.Join(" ", tags));
            cardViews[i].SetHighlight(anyVote ? 0 : -1);
        }

        int missing = voters.Count - votes.Count;
        statusText.text = message ?? (missing > 0
            ? $"Esquerda/direita escolhe · Confirmar vota · Voltar desfaz o voto   (faltam {missing} voto(s))"
            : "");
    }

    private sealed class CardView
    {
        public RectTransform root;
        private Image frame;
        private Text voters;

        public static CardView Create(RectTransform parent, CardDefinition card, Vector2 position, Vector2 size)
        {
            UiTheme theme = UiTheme.Current;
            var view = new CardView();
            view.frame = UiFactory.Panel(parent, $"Card_{card.name}", theme != null ? theme.panelLight : null, Color.white);
            view.root = view.frame.rectTransform;
            UiFactory.Place(view.root, new Vector2(0.5f, 0.5f), position, size, new Vector2(0.5f, 0.5f));

            var faceGo = new GameObject("Face", typeof(RectTransform), typeof(RawImage));
            faceGo.transform.SetParent(view.root, false);
            RawImage face = faceGo.GetComponent<RawImage>();
            face.raycastTarget = false;
            if (theme != null && theme.cardAtlas != null)
            {
                face.texture = theme.cardAtlas;
                face.uvRect = theme.CardUV(card.atlasCell);
            }
            else
            {
                face.color = new Color(0.9f, 0.85f, 0.7f);
            }

            UiFactory.Place(face.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(138f, 186f), new Vector2(0.5f, 1f));

            Text title = UiFactory.Label(view.root, "Title", card.title, 30, UiFactory.TextDark, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(size.x - 30f, 44f), new Vector2(0.5f, 1f));
            Text effect = UiFactory.Label(view.root, "Effect", card.effectText, 24, new Color(0.1f, 0.45f, 0.12f), TextAnchor.UpperCenter, bold: true);
            UiFactory.Place(effect.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -292f), new Vector2(size.x - 36f, 70f), new Vector2(0.5f, 1f));
            Text leftover = UiFactory.Label(view.root, "Leftover", $"Se ficar na mesa:\n{card.leftoverText}", 20, new Color(0.6f, 0.12f, 0.1f), TextAnchor.UpperCenter);
            UiFactory.Place(leftover.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -372f), new Vector2(size.x - 36f, 70f), new Vector2(0.5f, 1f));

            view.voters = UiFactory.Label(view.root, "Voters", "", 26, UiFactory.TextLight, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Place(view.voters.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -40f), new Vector2(size.x + 20f, 40f), new Vector2(0.5f, 0.5f));
            return view;
        }

        public void SetVoters(string text) => voters.text = text;

        public void SetHighlight(int state)
        {
            frame.color = state == 1 ? new Color(0.7f, 1f, 0.7f) : state == 0 ? new Color(1f, 0.95f, 0.75f) : Color.white;
            root.localScale = state == 1 ? Vector3.one * 1.08f : Vector3.one;
        }
    }
}
