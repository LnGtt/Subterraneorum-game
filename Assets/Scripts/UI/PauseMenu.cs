using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    public const string MainMenuScene = "MainMenu";

    private readonly Dictionary<PlayerInputHandler, System.Action> subscribed = new Dictionary<PlayerInputHandler, System.Action>();
    private OptionsMenuView view;
    private PlayerSeat pausedBy;

    public bool IsOpen => view != null && view.IsOpen;

    private void Awake()
    {
        view = new OptionsMenuView(transform, "PauseCanvas", 80);
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
        foreach (KeyValuePair<PlayerInputHandler, System.Action> pair in subscribed)
        {
            if (pair.Key != null) pair.Key.OnPauseInput -= pair.Value;
        }

        subscribed.Clear();
        if (IsOpen) Resume();
    }

    private void Subscribe(PlayerStatus player)
    {
        PlayerInputHandler handler = player != null ? player.GetComponent<PlayerInputHandler>() : null;
        if (handler == null || subscribed.ContainsKey(handler)) return;
        System.Action callback = () => HandlePause(handler);
        subscribed[handler] = callback;
        handler.OnPauseInput += callback;
    }

    private void Unsubscribe(PlayerStatus player)
    {
        PlayerInputHandler handler = player != null ? player.GetComponent<PlayerInputHandler>() : null;
        if (handler != null && subscribed.TryGetValue(handler, out System.Action callback))
        {
            handler.OnPauseInput -= callback;
            subscribed.Remove(handler);
        }
    }

    private void HandlePause(PlayerInputHandler handler)
    {
        if (IsOpen || handler == null) return;
        Open(handler.Seat);
    }

    public void Open(PlayerSeat seat)
    {
        if (IsOpen || !GamePause.TryAcquire(this)) return;
        pausedBy = seat;
        ShowMenu(0);
    }

    private void ShowMenu(int cursor)
    {
        IEnumerable<PlayerSeat> controllers = pausedBy != null ? new[] { pausedBy } : (IEnumerable<PlayerSeat>)PlayerSeat.All;
        string who = pausedBy != null ? $"J{pausedBy.PlayerIndex + 1} pausou o jogo" : "Jogo pausado";
        view.Open("PAUSADO", $"{who}\n\nCima/baixo escolhe · Confirmar · Voltar continua", controllers, Resume, cursor,
            ("Continuar", Resume),
            ("Recomeçar partida", Restart),
            ($"Música: {Percent(MusicDirector.MusicVolume)}", CycleMusicVolume),
            ($"Efeitos: {Percent(GameAudio.SfxVolume)}", CycleSfxVolume),
            ("Menu principal", GoToMainMenu),
            ("Sair do jogo", Quit));
    }

    private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

    private static float NextVolume(float value)
    {
        float next = Mathf.Round(value * 5f + 1f) / 5f;
        return next > 1.001f ? 0f : next;
    }

    private void CycleMusicVolume()
    {
        MusicDirector.MusicVolume = NextVolume(MusicDirector.MusicVolume);
        ShowMenu(2);
    }

    private void CycleSfxVolume()
    {
        GameAudio.SfxVolume = NextVolume(GameAudio.SfxVolume);
        ShowMenu(3);
    }

    public void OpenForAnyone() => Open(null);

    public void Resume()
    {
        view.Close();
        GamePause.Release(this);
    }

    private void Restart()
    {
        view.Close();
        GamePause.ForceRelease();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void GoToMainMenu()
    {
        view.Close();
        GamePause.ForceRelease();
        if (Application.CanStreamedLevelBeLoaded(MainMenuScene)) SceneManager.LoadScene(MainMenuScene);
        else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void Quit()
    {
        Debug.Log("[PauseMenu] Sair do jogo.");
        GamePause.ForceRelease();
        Application.Quit();
    }
}
