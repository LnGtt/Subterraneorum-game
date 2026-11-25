using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    public const string GameScene = "Game";

    private GameObject mainPanel;
    private GameObject helpPanel;
    private Button playButton;
    private Button helpBackButton;

    private void Awake()
    {
        GamePause.ForceRelease();
        BuildUI();
    }

    private void Start()
    {
        Select(playButton);
    }

    public void Play()
    {
        SceneManager.LoadScene(GameScene);
    }

    public void ShowHelp(bool show)
    {
        mainPanel.SetActive(!show);
        helpPanel.SetActive(show);
        Select(show ? helpBackButton : playButton);
    }

    public void Quit()
    {
        Debug.Log("[MainMenu] Sair do jogo.");
        Application.Quit();
    }

    private static void Select(Button button)
    {
        if (button != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    private void BuildUI()
    {
        RectTransform root = UiFactory.Canvas(transform, "MainMenuCanvas", 0);
        root.gameObject.AddComponent<GraphicRaycaster>();
        Image background = UiFactory.Box(root, "Background", new Color(0.04f, 0.03f, 0.06f, 1f));
        UiFactory.Stretch(background.rectTransform);

        Text title = UiFactory.Label(root, "Title", "SUBTERRANEORUM", 110, UiFactory.Gold, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(1600f, 150f));
        Text subtitle = UiFactory.Label(root, "Subtitle", "Cooperativo local de 1 a 4 jogadores", 34, UiFactory.TextLight);
        UiFactory.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -290f), new Vector2(1600f, 50f));

        UiTheme theme = UiTheme.Current;
        mainPanel = UiFactory.Empty(root, "Main").gameObject;
        UiFactory.Place((RectTransform)mainPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(520f, 380f));
        playButton = CreateButton(mainPanel.transform, "Jogar", 0, theme, Play);
        Button help = CreateButton(mainPanel.transform, "Como jogar", 1, theme, () => ShowHelp(true));
        Button quit = CreateButton(mainPanel.transform, "Sair", 2, theme, Quit);
        Chain(playButton, help, quit);

        helpPanel = UiFactory.Panel(root, "Help", theme != null ? theme.panelDark : null, Color.white).gameObject;
        UiFactory.Place((RectTransform)helpPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1300f, 560f));
        Text helpText = UiFactory.Label(helpPanel.transform, "Text",
            "<b>Entrar:</b> na seleção de classe, aperte um botão no teclado ou no controle (até 4 jogadores).\n" +
            "<b>Mover:</b> WASD / analógico      <b>Atacar:</b> clique, J / X\n" +
            "<b>Dash:</b> Espaço / A, RB      <b>Habilidade 1:</b> K, botão direito / B, RT\n" +
            "<b>Habilidade 2:</b> L, Q / LB, LT (destrave na árvore)      <b>Árvore:</b> Tab / Select\n" +
            "<b>Reviver aliado:</b> segure E / Y perto dele      <b>Pausa:</b> Esc / Start\n\n" +
            "Ao subir de nível, todos escolhem juntos uma carta (precisa ser unânime);\nas que sobrarem fortalecem os inimigos. Na zona segura, aperte E / Y para o minijogo das orbes.",
            26, UiFactory.TextLight, TextAnchor.UpperLeft);
        UiFactory.Stretch(helpText.rectTransform, 40f);
        helpBackButton = CreateButton(helpPanel.transform, "Voltar", 0, theme, () => ShowHelp(false));
        UiFactory.Place((RectTransform)helpBackButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(360f, 80f), new Vector2(0.5f, 0f));
        helpPanel.SetActive(false);
    }

    private static Button CreateButton(Transform parent, string label, int index, UiTheme theme, UnityEngine.Events.UnityAction onClick)
    {
        Image image = UiFactory.Panel(parent, $"Button_{label}", theme != null ? theme.panelLight : null, Color.white);
        image.raycastTarget = true;
        UiFactory.Place(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -index * 120f), new Vector2(460f, 96f), new Vector2(0.5f, 1f));
        Button button = image.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        Color selected = new Color(1f, 0.9f, 0.55f);
        colors.normalColor = new Color(0.85f, 0.85f, 0.85f);
        colors.highlightedColor = selected;
        colors.selectedColor = selected;
        colors.pressedColor = new Color(1f, 0.75f, 0.4f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(onClick);
        image.gameObject.AddComponent<SelectOnHover>();

        Text text = UiFactory.Label(image.rectTransform, "Label", label, 40, UiFactory.TextDark, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Stretch(text.rectTransform);
        return button;
    }

    private static void Chain(params Button[] buttons)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            Navigation nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnUp = buttons[(i - 1 + buttons.Length) % buttons.Length];
            nav.selectOnDown = buttons[(i + 1) % buttons.Length];
            buttons[i].navigation = nav;
        }
    }
}
