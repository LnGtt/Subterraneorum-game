using System;
using System.Collections.Generic;
using Subterraneorum.DungeonSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ClassSelectionLobby : MonoBehaviour
{
    private const int MaxPlayers = 4;

    [SerializeField] private ClassDefinition[] classes;
    [SerializeField] private PlayerInputManager inputManager;
    [SerializeField] private DungeonManager dungeonManager;
    [Tooltip("Segundos entre todos confirmarem e a partida começar. Para se alguém trocar de personagem, voltar atrás ou entrar.")]
    [SerializeField] private float startCountdown = 5f;

    private readonly List<PlayerSeat> seats = new List<PlayerSeat>();
    private readonly Dictionary<PlayerSeat, float> rejectedUntil = new Dictionary<PlayerSeat, float>();
    private bool matchStarted;
    private float countdownRemaining = -1f;

    private Font font;
    private GameObject background;
    private Text titleText;
    private Text subtitleText;
    private Text rosterText;
    private Text footerText;
    private Text countdownText;
    private Text inGameHintText;
    private readonly SeatCard[] cards = new SeatCard[MaxPlayers];

    public IReadOnlyList<ClassDefinition> Classes => classes;

    private void Awake()
    {
        if (inputManager == null) inputManager = FindAnyObjectByType<PlayerInputManager>();
        if (dungeonManager == null) dungeonManager = FindAnyObjectByType<DungeonManager>();
        if (classes == null || classes.Length == 0)
        {
            Debug.LogError("[ClassSelectionLobby] Nenhuma classe configurada.", this);
            classes = Array.Empty<ClassDefinition>();
        }

        BuildUI();
    }

    private void OnEnable()
    {
        if (inputManager == null) return;
        inputManager.onPlayerJoined += HandlePlayerJoined;
        inputManager.onPlayerLeft += HandlePlayerLeft;
    }

    private void OnDisable()
    {
        if (inputManager == null) return;
        inputManager.onPlayerJoined -= HandlePlayerJoined;
        inputManager.onPlayerLeft -= HandlePlayerLeft;
    }

    private void Start()
    {
        foreach (PlayerInput existing in PlayerInput.all)
        {
            HandlePlayerJoined(existing);
        }

        Refresh();
    }

    private void Update()
    {
        if (seats.RemoveAll(s => s == null) > 0) Refresh();

        if (!matchStarted)
        {
            bool allReady = seats.Count > 0 && seats.TrueForAll(s => s.IsConfirmed);
            if (!allReady)
            {
                if (countdownRemaining >= 0f)
                {
                    countdownRemaining = -1f;
                    Refresh();
                }
            }
            else
            {
                if (countdownRemaining < 0f) countdownRemaining = startCountdown;
                countdownRemaining -= Time.unscaledDeltaTime;
                if (countdownRemaining <= 0f)
                {
                    StartMatch();
                    return;
                }

                countdownText.text = $"Todos prontos! Começando em {Mathf.CeilToInt(countdownRemaining)}...\n<size=26>Para trocar de personagem: esquerda/direita ou Voltar</size>";
            }
        }

        if (rejectedUntil.Count > 0)
        {
            bool expired = false;
            foreach (KeyValuePair<PlayerSeat, float> pair in rejectedUntil)
            {
                if (Time.unscaledTime >= pair.Value) expired = true;
            }

            if (expired)
            {
                var keys = new List<PlayerSeat>(rejectedUntil.Keys);
                foreach (PlayerSeat key in keys)
                {
                    if (Time.unscaledTime >= rejectedUntil[key]) rejectedUntil.Remove(key);
                }

                Refresh();
            }
        }
    }

    private void HandlePlayerJoined(PlayerInput playerInput)
    {
        if (playerInput == null) return;
        PlayerSeat seat = playerInput.GetComponent<PlayerSeat>();
        if (seat == null)
        {
            Debug.LogWarning($"[ClassSelectionLobby] {playerInput.name} entrou sem PlayerSeat; ignorado.", playerInput);
            return;
        }

        if (seats.Contains(seat)) return;

        if (matchStarted)
        {
            Debug.LogWarning($"[ClassSelectionLobby] Tentativa de entrar com a partida em andamento; ignorada.", playerInput);
            Destroy(playerInput.gameObject);
            return;
        }

        seat.EnsureInitialized();
        seat.CursorIndex = FirstFreeClass(seat, avoidCursors: true);
        seat.NavigateRequested += Navigate;
        seat.ConfirmRequested += Confirm;
        seat.CancelRequested += Cancel;
        seats.Add(seat);
        seats.Sort((a, b) => a.PlayerIndex.CompareTo(b.PlayerIndex));
        countdownRemaining = -1f;

        if ((seats.Count >= MaxPlayers || matchStarted) && inputManager != null) inputManager.DisableJoining();

        Debug.Log($"[ClassSelectionLobby] Jogador {seat.PlayerIndex + 1} entrou ({seat.DeviceLabel}).");
        Refresh();
    }

    private void HandlePlayerLeft(PlayerInput playerInput)
    {
        PlayerSeat seat = playerInput != null ? playerInput.GetComponent<PlayerSeat>() : null;
        if (seat == null || !seats.Remove(seat)) return;

        seat.NavigateRequested -= Navigate;
        seat.ConfirmRequested -= Confirm;
        seat.CancelRequested -= Cancel;
        rejectedUntil.Remove(seat);
        if (inputManager != null && seats.Count < MaxPlayers && !matchStarted) inputManager.EnableJoining();
        Refresh();
    }

    private void Navigate(PlayerSeat seat, Vector2Int move)
    {
        int direction = move.x;
        if (direction == 0 || seat.HasCharacter || matchStarted || classes.Length == 0) return;

        if (seat.IsConfirmed)
        {
            seat.ClearConfirmation();
            countdownRemaining = -1f;
            Debug.Log($"[ClassSelectionLobby] Jogador {seat.PlayerIndex + 1} voltou a escolher.");
        }

        int index = seat.CursorIndex;
        for (int step = 0; step < classes.Length; step++)
        {
            index = (index + direction + classes.Length) % classes.Length;
            if (TakenBy(index, seat) == null) break;
        }

        seat.CursorIndex = index;
        rejectedUntil.Remove(seat);
        Refresh();
    }

    private void Confirm(PlayerSeat seat)
    {
        if (seat.IsConfirmed || seat.HasCharacter || classes.Length == 0) return;

        int index = Mathf.Clamp(seat.CursorIndex, 0, classes.Length - 1);
        PlayerSeat owner = TakenBy(index, seat);
        if (owner != null)
        {
            Debug.Log($"[ClassSelectionLobby] Jogador {seat.PlayerIndex + 1} tentou {classes[index].displayName}, que já é do Jogador {owner.PlayerIndex + 1}.");
            rejectedUntil[seat] = Time.unscaledTime + 1.5f;
            Refresh();
            return;
        }

        seat.Confirm(classes[index]);
        Debug.Log($"[ClassSelectionLobby] Jogador {seat.PlayerIndex + 1} escolheu {classes[index].displayName} ({classes[index].classId}).");
        Refresh();
    }

    private void Cancel(PlayerSeat seat)
    {
        if (seat.HasCharacter || !seat.IsConfirmed || matchStarted) return;
        seat.ClearConfirmation();
        countdownRemaining = -1f;
        Refresh();
    }

    private PlayerSeat TakenBy(int classIndex, PlayerSeat except)
    {
        if (classIndex < 0 || classIndex >= classes.Length) return null;
        foreach (PlayerSeat other in seats)
        {
            if (other != null && other != except && other.SelectedClass == classes[classIndex]) return other;
        }

        return null;
    }

    private int FirstFreeClass(PlayerSeat seat, bool avoidCursors)
    {
        for (int i = 0; i < classes.Length; i++)
        {
            if (TakenBy(i, seat) != null) continue;
            if (avoidCursors && seats.Exists(s => s != seat && !s.IsConfirmed && s.CursorIndex == i)) continue;
            return i;
        }

        return avoidCursors ? FirstFreeClass(seat, false) : 0;
    }

    private void StartMatch()
    {
        if (matchStarted) return;
        matchStarted = true;
        countdownRemaining = -1f;

        if (inputManager != null) inputManager.DisableJoining();

        for (int i = 0; i < seats.Count; i++)
        {
            seats[i].SpawnCharacter(transform.position + Vector3.right * (i * 1.5f), Quaternion.identity);
        }

        if (dungeonManager != null) dungeonManager.StartRun();
        else Debug.LogWarning("[ClassSelectionLobby] Nenhum DungeonManager na cena: personagens criados sem dungeon.", this);

        Debug.Log($"[ClassSelectionLobby] Partida iniciada com {seats.Count} jogador(es).");
        Refresh();
    }

    [ContextMenu("Iniciar rápido (1 jogador)")]
    public void QuickStart()
    {
        if (matchStarted) return;

        if (seats.Count == 0 && inputManager != null)
        {
            InputDevice device = Keyboard.current != null ? Keyboard.current : Gamepad.current;
            PlayerInput joined = device != null ? inputManager.JoinPlayer(pairWithDevice: device) : inputManager.JoinPlayer();
            if (joined != null) HandlePlayerJoined(joined);
        }

        foreach (PlayerSeat seat in seats)
        {
            if (seat.IsConfirmed || classes.Length == 0) continue;
            int index = FirstFreeClass(seat, avoidCursors: false);
            seat.CursorIndex = index;
            seat.Confirm(classes[index]);
        }

        StartMatch();
    }

    private void Refresh()
    {
        if (titleText == null) return;

        bool lobby = !matchStarted;
        background.SetActive(lobby);
        titleText.gameObject.SetActive(lobby);
        subtitleText.gameObject.SetActive(lobby);
        rosterText.gameObject.SetActive(lobby);
        countdownText.gameObject.SetActive(lobby && countdownRemaining >= 0f);

        bool anyPending = false;
        for (int i = 0; i < MaxPlayers; i++)
        {
            PlayerSeat seat = i < seats.Count ? seats[i] : null;
            SeatCard card = cards[i];

            if (seat == null)
            {
                card.root.SetActive(lobby);
                card.ShowEmpty(i);
                continue;
            }

            bool pending = !seat.HasCharacter;
            anyPending |= pending;
            card.root.SetActive(lobby || pending);
            if (!card.root.activeSelf || classes.Length == 0) continue;

            ClassDefinition def = classes[Mathf.Clamp(seat.CursorIndex, 0, classes.Length - 1)];
            PlayerSeat owner = TakenBy(seat.CursorIndex, seat);
            string status;
            Color statusColor;
            if (seat.IsConfirmed)
            {
                status = matchStarted ? "Entrando..." : "PRONTO!";
                statusColor = new Color(0.45f, 1f, 0.45f);
            }
            else if (rejectedUntil.ContainsKey(seat))
            {
                status = "Já escolhida! Escolha outra";
                statusColor = new Color(1f, 0.4f, 0.35f);
            }
            else if (owner != null)
            {
                status = $"Ocupada pelo J{owner.PlayerIndex + 1}";
                statusColor = new Color(1f, 0.55f, 0.35f);
            }
            else
            {
                status = "<   escolha   >";
                statusColor = Color.white;
            }

            card.Show($"JOGADOR {seat.PlayerIndex + 1} · {seat.DeviceLabel}", def, status, statusColor, seat.IsConfirmed);
        }

        footerText.gameObject.SetActive(lobby || anyPending);
        inGameHintText.gameObject.SetActive(false);
        rosterText.text = BuildRoster();
    }

    private string BuildRoster()
    {
        var parts = new List<string>();
        for (int i = 0; i < classes.Length; i++)
        {
            PlayerSeat owner = TakenBy(i, null);
            parts.Add($"{classes[i].displayName} ({classes[i].roleName}): {(owner != null ? $"J{owner.PlayerIndex + 1}" : "livre")}");
        }

        return string.Join("     ·     ", parts);
    }

    private void BuildUI()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("ClassSelectCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        RectTransform root = canvasGo.GetComponent<RectTransform>();

        background = CreateImage(root, "Background", new Color(0.03f, 0.02f, 0.05f, 0.94f));
        Stretch(background.GetComponent<RectTransform>());

        titleText = CreateText(root, "Title", "SUBTERRANEORUM", 72, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1600f, 100f));
        titleText.fontStyle = FontStyle.Bold;
        subtitleText = CreateText(root, "Subtitle", "Aperte qualquer botão no teclado ou no controle para entrar  (até 4 jogadores)", 30,
            new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(1700f, 50f));
        subtitleText.color = new Color(0.85f, 0.8f, 0.7f);

        const float cardWidth = 400f;
        const float spacing = 30f;
        float totalWidth = MaxPlayers * cardWidth + (MaxPlayers - 1) * spacing;
        for (int i = 0; i < MaxPlayers; i++)
        {
            float x = -totalWidth * 0.5f + cardWidth * 0.5f + i * (cardWidth + spacing);
            cards[i] = SeatCard.Create(this, root, i, new Vector2(x, 20f), new Vector2(cardWidth, 430f));
        }

        rosterText = CreateText(root, "Roster", "", 24, new Vector2(0.5f, 0f), new Vector2(0f, 220f), new Vector2(1800f, 40f));
        rosterText.color = new Color(0.75f, 0.75f, 0.8f);
        countdownText = CreateText(root, "Countdown", "", 48, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1400f, 110f));
        countdownText.supportRichText = true;
        countdownText.color = new Color(1f, 0.85f, 0.4f);
        countdownText.fontStyle = FontStyle.Bold;
        footerText = CreateText(root, "Footer",
            "Escolher: A/D, setas, analógico ou D-pad      Confirmar: Espaço, Enter, J ou botão A/X      Voltar: Esc ou botão B", 24,
            new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(1800f, 40f));
        footerText.color = new Color(0.7f, 0.7f, 0.7f);
        inGameHintText = CreateText(root, "JoinHint", "Mais alguém? Aperte um botão para entrar", 22,
            new Vector2(0f, 1f), new Vector2(270f, -28f), new Vector2(520f, 34f));
        inGameHintText.color = new Color(1f, 1f, 1f, 0.55f);
    }

    private Text CreateText(Transform parent, string objectName, string content, int size, Vector2 anchor, Vector2 position, Vector2 dimensions, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.text = content;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static GameObject CreateImage(Transform parent, string objectName, Color color)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return go;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private sealed class SeatCard
    {
        public GameObject root;
        private Image frame;
        private Image band;
        private Text header;
        private Text className;
        private Text role;
        private Text description;
        private Text status;

        public static SeatCard Create(ClassSelectionLobby lobby, Transform parent, int index, Vector2 position, Vector2 size)
        {
            var card = new SeatCard();
            card.root = CreateImage(parent, $"SeatCard_{index + 1}", new Color(0.45f, 0.45f, 0.5f, 1f));
            card.frame = card.root.GetComponent<Image>();
            RectTransform rect = card.root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            GameObject inner = CreateImage(card.root.transform, "Inner", new Color(0.08f, 0.07f, 0.1f, 0.97f));
            RectTransform innerRect = inner.GetComponent<RectTransform>();
            Stretch(innerRect);
            innerRect.offsetMin = new Vector2(4f, 4f);
            innerRect.offsetMax = new Vector2(-4f, -4f);
            UiTheme theme = UiTheme.Current;
            if (theme != null && theme.panelDark != null)
            {
                Image innerImage = inner.GetComponent<Image>();
                UiFactory.ApplySprite(innerImage, theme.panelDark);
                innerImage.color = Color.white;
            }

            card.band = CreateImage(inner.transform, "Band", Color.gray).GetComponent<Image>();
            RectTransform bandRect = card.band.rectTransform;
            bandRect.anchorMin = new Vector2(0f, 1f);
            bandRect.anchorMax = new Vector2(1f, 1f);
            bandRect.pivot = new Vector2(0.5f, 1f);
            bandRect.sizeDelta = new Vector2(0f, 56f);
            bandRect.anchoredPosition = Vector2.zero;

            Vector2 top = new Vector2(0.5f, 1f);
            card.header = lobby.CreateText(inner.transform, "Header", "", 24, top, new Vector2(0f, -28f), new Vector2(size.x - 20f, 40f));
            card.header.fontStyle = FontStyle.Bold;
            card.className = lobby.CreateText(inner.transform, "ClassName", "", 44, top, new Vector2(0f, -120f), new Vector2(size.x - 20f, 60f));
            card.className.fontStyle = FontStyle.Bold;
            card.role = lobby.CreateText(inner.transform, "Role", "", 28, top, new Vector2(0f, -170f), new Vector2(size.x - 20f, 40f));
            card.description = lobby.CreateText(inner.transform, "Description", "", 20, top, new Vector2(0f, -250f), new Vector2(size.x - 50f, 110f));
            card.description.color = new Color(0.8f, 0.8f, 0.8f);
            card.status = lobby.CreateText(inner.transform, "Status", "", 30, new Vector2(0.5f, 0f), new Vector2(0f, 45f), new Vector2(size.x - 20f, 50f));
            card.status.fontStyle = FontStyle.Bold;
            return card;
        }

        public void ShowEmpty(int index)
        {
            frame.color = new Color(0.25f, 0.25f, 0.28f, 1f);
            band.color = new Color(0.15f, 0.15f, 0.18f, 1f);
            header.text = $"JOGADOR {index + 1}";
            className.text = "";
            role.text = "";
            description.text = "Aperte um botão\nno teclado ou no controle\npara entrar";
            status.text = "";
        }

        public void Show(string headerText, ClassDefinition def, string statusText, Color statusColor, bool confirmed)
        {
            frame.color = confirmed ? def.color : new Color(0.45f, 0.45f, 0.5f, 1f);
            band.color = def.color * new Color(0.7f, 0.7f, 0.7f, 1f);
            header.text = headerText;
            className.text = def.displayName;
            role.text = def.roleName;
            role.color = def.color;
            description.text = def.description;
            status.text = statusText;
            status.color = statusColor;
        }
    }
}
