using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class GameSceneSmokeTest
{
    [UnityTest]
    public IEnumerator SampleScene_GeneratesDungeonAndPlacesPlayer()
    {
        SceneManager.LoadScene("Game");
        yield return null;

        GameObject lobby = GameObject.Find("ClassSelectLobby");
        Assert.IsNotNull(lobby, "Tela de seleção de classe não encontrada.");
        lobby.SendMessage("QuickStart");

        GameObject dungeon = null;
        float timeout = Time.realtimeSinceStartup + 20f;
        while (dungeon == null && Time.realtimeSinceStartup < timeout)
        {
            dungeon = GameObject.Find("Dungeon_Generated");
            yield return null;
        }

        Assert.IsNotNull(dungeon, "A dungeon não foi gerada em runtime.");

        yield return new WaitForSeconds(1.5f);

        Transform walls = dungeon.transform.Find("Walls");
        Transform colliders = dungeon.transform.Find("Colliders");
        Assert.IsNotNull(walls, "Sem paredes.");
        Assert.Greater(walls.childCount, 50, "Poucas paredes geradas.");
        Assert.IsNotNull(colliders, "Sem colisores de piso/parede.");

        GameObject enemies = GameObject.Find("Enemies");
        Assert.IsNotNull(enemies, "Container de inimigos não encontrado.");
        Assert.Greater(enemies.transform.childCount, 3, "Poucos inimigos gerados.");

        GameObject player = GameObject.FindWithTag("Player");
        Assert.IsNotNull(player, "Jogador não encontrado.");

        GameObject startMarker = null;
        foreach (Transform marker in dungeon.transform.Find("RoomMarkers"))
        {
            if (marker.name.EndsWith("_Start")) startMarker = marker.gameObject;
        }

        Assert.IsNotNull(startMarker, "Sala inicial não encontrada.");
        Bounds startBounds = startMarker.GetComponent<BoxCollider>().bounds;
        startBounds.Expand(new Vector3(2f, 10f, 2f));
        Assert.IsTrue(startBounds.Contains(player.transform.position),
            $"Jogador em {player.transform.position} fora da sala inicial {startBounds}.");

        Assert.AreEqual(0f, player.transform.position.y, 0.2f, "Jogador fora da altura do piso.");

        yield return new WaitForSeconds(3f);
        Assert.AreEqual(0f, player.transform.position.y, 0.2f, "Jogador saiu da altura do piso durante o jogo.");
    }
}
