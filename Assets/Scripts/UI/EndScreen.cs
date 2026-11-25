using System.Collections.Generic;
using System.Text;
using Subterraneorum.DungeonSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EndScreen : MonoBehaviour
{
    [SerializeField] private float delaySeconds = 2.5f;

    private OptionsMenuView view;
    private bool pending;
    private bool victory;
    private float showAt;

    public bool IsOpen => view != null && view.IsOpen;
    public bool ShowingVictory => IsOpen && victory;

    private void Awake()
    {
        view = new OptionsMenuView(transform, "EndCanvas", 90);
    }

    private void OnEnable()
    {
        DungeonManager.OnVictory += HandleVictory;
        DungeonManager.OnDefeat += HandleDefeat;
    }

    private void OnDisable()
    {
        DungeonManager.OnVictory -= HandleVictory;
        DungeonManager.OnDefeat -= HandleDefeat;
    }

    private void HandleVictory() => Schedule(true);

    private void HandleDefeat() => Schedule(false);

    private void Schedule(bool won)
    {
        pending = true;
        victory = won;
        showAt = Time.unscaledTime + delaySeconds;
    }

    private void Update()
    {
        if (!pending || Time.unscaledTime < showAt) return;
        if (!GamePause.TryAcquire(this)) return;

        pending = false;
        view.Open(victory ? "VITÓRIA!" : "DERROTA", Summary(), PlayerSeat.All, null,
            ("Jogar de novo", PlayAgain),
            ("Menu principal", MainMenu));
    }

    private string Summary()
    {
        var text = new StringBuilder(victory ? "O Guardião Abissal caiu. As ruínas estão livres!\n\n" : "Todos caíram nas profundezas...\n\n");
        var lines = new List<string>();
        foreach (PlayerSeat seat in PlayerSeat.All)
        {
            if (seat == null || seat.Character == null) continue;
            PlayerIdentity identity = seat.Character.GetComponent<PlayerIdentity>();
            PlayerProgression progression = seat.Character.GetComponent<PlayerProgression>();
            string name = identity != null && identity.Class != null ? UiFactory.Colored(identity.Class.displayName, identity.Class.color) : "?";
            lines.Add($"J{seat.PlayerIndex + 1} · {name} — nível {(progression != null ? progression.CurrentLevel : 1)}");
        }

        text.Append(string.Join("\n", lines));
        return text.ToString();
    }

    private void PlayAgain()
    {
        view.Close();
        GamePause.ForceRelease();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void MainMenu()
    {
        view.Close();
        GamePause.ForceRelease();
        if (Application.CanStreamedLevelBeLoaded(PauseMenu.MainMenuScene)) SceneManager.LoadScene(PauseMenu.MainMenuScene);
        else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
