using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DevAutoCapture : MonoBehaviour
{
    private string path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-autocapture");
        if (index < 0 || index + 1 >= args.Length) return;

        var go = new GameObject("DevAutoCapture");
        DontDestroyOnLoad(go);
        go.AddComponent<DevAutoCapture>().path = args[index + 1];
    }

    private IEnumerator Start()
    {
        if (SceneManager.GetActiveScene().name != MainMenuController.GameScene)
        {
            SceneManager.LoadScene(MainMenuController.GameScene);
            yield return null;
            yield return null;
        }

        GameObject lobby = GameObject.Find("ClassSelectLobby");
        if (lobby != null) lobby.SendMessage("QuickStart");

        float end = Time.realtimeSinceStartup + 20f;
        while (GameObject.Find("Dungeon_Generated") == null && Time.realtimeSinceStartup < end) yield return null;
        yield return new WaitForSecondsRealtime(4f);

        ScreenCapture.CaptureScreenshot(path);
        Debug.Log($"[DevAutoCapture] Print salvo em {path}");
        yield return new WaitForSecondsRealtime(1.5f);
        Application.Quit();
    }
}
