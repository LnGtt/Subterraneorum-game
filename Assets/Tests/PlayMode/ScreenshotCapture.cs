using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

[Explicit("Só para diagnóstico visual; precisa de placa de vídeo.")]
public class ScreenshotCapture
{
    private static string Folder => Path.Combine(Application.dataPath, "..", "Logs", "Screenshots");

    [TearDown]
    public void TearDown() => TestHelpers.ResetPause();

    [UnityTest]
    public IEnumerator CaptureGameplay()
    {
        Directory.CreateDirectory(Folder);
        SceneManager.LoadScene("Game");
        yield return TestHelpers.WaitRealtime(1f);
        yield return Capture("01_lobby");

        GameObject.Find("ClassSelectLobby").SendMessage("QuickStart");
        yield return TestHelpers.WaitForDungeon();
        yield return TestHelpers.WaitRealtime(2f);
        yield return Capture("02_inicio_1jogador");

        yield return TestHelpers.WaitRealtime(3f);
        yield return Capture("03_apos_5s");
        yield return CaptureMap("07_mapa_nivel1");
        LogObjectsOutsideFloor();

        GameObject player = GameObject.FindWithTag("Player");
        GameObject enemies = GameObject.Find("Enemies");
        if (player != null && enemies != null && enemies.transform.childCount > 0)
        {
            Vector3 enemy = enemies.transform.GetChild(0).position;
            TestHelpers.Teleport(player, new Vector3(enemy.x, player.transform.position.y, enemy.z - 5f));
            yield return TestHelpers.WaitRealtime(1.5f);
            yield return Capture("04_perto_de_inimigos");

            LogEnemySizes(enemies.transform);

            Transform first = enemies.transform.GetChild(0);
            var closeGo = new GameObject("CloseCam");
            Camera close = closeGo.AddComponent<Camera>();
            close.transform.position = first.position + new Vector3(0f, 2.5f, -4f);
            close.transform.LookAt(first.position + Vector3.up * 0.5f);
            yield return CaptureWith(close, "06_inimigo_close");
            Object.Destroy(closeGo);
        }

        Component dungeon = TestHelpers.FindComponent("DungeonManager");
        dungeon.SendMessage("GoToFinalLevel");
        yield return TestHelpers.WaitUntilRealtime(() => TestHelpers.Get<bool>(dungeon, "IsOnFinalLevel"), 20f, "Sem último andar.");
        yield return TestHelpers.WaitRealtime(1.5f);
        GameObject enemies2 = GameObject.Find("Enemies");
        if (enemies2 != null) LogEnemySizes(enemies2.transform);
        yield return CaptureMap("08_mapa_nivel2");
        dungeon.SendMessage("SpawnBossNow");
        yield return null;
        Component boss = TestHelpers.FindComponent("BossController");
        player = GameObject.FindWithTag("Player");
        if (boss != null && player != null)
        {
            Vector3 b = boss.transform.position;
            TestHelpers.Teleport(player, new Vector3(b.x, player.transform.position.y, b.z - 6f));
            boss.SendMessage("ForceAttack", 2);
            yield return TestHelpers.WaitRealtime(0.8f);
            yield return Capture("05_chefao");
        }
    }

    private static void LogObjectsOutsideFloor()
    {
        Component generator = TestHelpers.FindComponent("DungeonGenerator");
        GameObject dungeon = GameObject.Find("Dungeon_Generated");
        if (generator == null || dungeon == null) return;

        float cell = TestHelpers.Get<float>(generator, "CellSize");
        Vector3 origin = generator.transform.position;
        int found = 0;
        foreach (Transform category in dungeon.transform)
        {
            if (category.name == "Walls" || category.name == "Colliders" || category.name == "RoomMarkers") continue;
            foreach (Transform piece in category)
            {
                Renderer[] renderers = piece.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds b = renderers[0].bounds;
                foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                int cx = Mathf.RoundToInt((b.center.x - origin.x) / cell);
                int cz = Mathf.RoundToInt((b.center.z - origin.z) / cell);
                if (category.name == "Doors" || category.name == "Transitions")
                {
                    Debug.Log($"[ScreenshotCapture] {category.name}/{piece.name} tamanho {b.size} rot {piece.eulerAngles}");
                }

                bool floor = (bool)TestHelpers.Call(generator, "IsFloor", cx, cz);
                if (!floor)
                {
                    found++;
                    Debug.Log($"[ScreenshotCapture] FORA DO CHÃO: {category.name}/{piece.name} centro {b.center} tamanho {b.size} célula ({cx},{cz})");
                }
            }
        }

        Debug.Log($"[ScreenshotCapture] peças fora do chão: {found}");

        foreach (Renderer r in dungeon.GetComponentsInChildren<Renderer>())
        {
            if (r.bounds.max.y <= 4.3f) continue;
            Transform piece = r.transform;
            string path = piece.name;
            while (piece.parent != null && piece.parent != dungeon.transform)
            {
                piece = piece.parent;
                path = piece.name + "/" + path;
            }

            string mats = string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m != null ? m.name : "null"));
            Debug.Log($"[ScreenshotCapture] ALTO: {path} topo={r.bounds.max.y:0.0} centro {r.bounds.center} tamanho {r.bounds.size} mats=[{mats}]");
        }

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (r.transform.IsChildOf(dungeon.transform) && r.transform.parent != null && r.transform.parent.name.StartsWith("Doors")) continue;
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || !(m.name.Contains("Wood") || m.name.Contains("wood"))) continue;
                Transform top = r.transform;
                string path = top.name;
                while (top.parent != null)
                {
                    top = top.parent;
                    path = top.name + "/" + path;
                }

                Debug.Log($"[ScreenshotCapture] MADEIRA: {path} mat={m.name} centro {r.bounds.center} tamanho {r.bounds.size}");
                break;
            }
        }
    }

    private static IEnumerator CaptureMap(string name)
    {
        GameObject dungeon = GameObject.Find("Dungeon_Generated");
        if (dungeon == null) yield break;

        Renderer[] renderers = dungeon.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) yield break;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

        var go = new GameObject("MapCam");
        Camera cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(b.extents.z, b.extents.x * 900f / 1600f) + 2f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 500f;
        go.transform.position = new Vector3(b.center.x, b.max.y + 60f, b.center.z);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        yield return CaptureWith(cam, name);
        Object.Destroy(go);
    }

    private static void LogEnemySizes(Transform root)
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Transform each in root)
        {
            if (!seen.Add(each.name)) continue;
            foreach (SkinnedMeshRenderer skinned in each.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh();
                skinned.BakeMesh(baked, true);
                Debug.Log($"[ScreenshotCapture] inimigo {each.name}: tamanho animado {baked.bounds.size} | bounds do renderer {skinned.bounds.size}");
                Object.Destroy(baked);
            }
        }
    }

    private static IEnumerator Capture(string name)
    {
        yield return CaptureWith(null, name);
    }

    private static IEnumerator CaptureWith(Camera overrideCamera, string name)
    {
        yield return null;
        Camera cam = overrideCamera != null ? overrideCamera : Camera.main;
        Assert.IsNotNull(cam, "Sem câmera principal.");

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);
        var modes = new RenderMode[canvases.Length];
        for (int i = 0; i < canvases.Length; i++)
        {
            modes[i] = canvases[i].renderMode;
            if (canvases[i].isRootCanvas && canvases[i].renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = cam;
                canvases[i].planeDistance = cam.nearClipPlane + 0.05f;
            }
        }

        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(1600, 900, 24);
        RenderTexture previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        cam.targetTexture = previous;

        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null) canvases[i].renderMode = modes[i];
        }

        string path = Path.Combine(Folder, name + ".png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.Destroy(texture);
        rt.Release();
        Debug.Log($"[ScreenshotCapture] {path} | câmera em {cam.transform.position} rot {cam.transform.eulerAngles} fov {cam.fieldOfView:0.0}");
    }
}
