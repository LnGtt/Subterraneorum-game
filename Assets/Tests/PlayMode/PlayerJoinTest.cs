using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class PlayerJoinTest : InputTestFixture
{
    public override void Setup()
    {
        DisableLeftoverPlayerInput();
        base.Setup();
    }

    public override void TearDown()
    {
        DisableLeftoverPlayerInput();
        base.TearDown();
    }

    private static void DisableLeftoverPlayerInput()
    {
        foreach (PlayerInput player in Object.FindObjectsByType<PlayerInput>(FindObjectsInactive.Exclude))
        {
            player.gameObject.SetActive(false);
        }

        foreach (PlayerInputManager manager in Object.FindObjectsByType<PlayerInputManager>(FindObjectsInactive.Exclude))
        {
            manager.gameObject.SetActive(false);
        }
    }

    [UnityTest]
    public IEnumerator TwoGamepadsAndKeyboard_PickDistinctClasses_AndLateJoinerSpawnsInStartRoom()
    {
        Gamepad pad1 = InputSystem.AddDevice<Gamepad>();
        Gamepad pad2 = InputSystem.AddDevice<Gamepad>();
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();

        SceneManager.LoadScene("Game");
        yield return null;
        yield return null;

        Assert.IsNotNull(GameObject.Find("ClassSelectLobby"), "Tela de seleção de classe não está na cena.");
        Assert.AreEqual(0, PlayerInput.all.Count, "Ninguém deveria entrar antes de apertar um botão.");
        Assert.IsNull(GameObject.Find("Dungeon_Generated"), "A dungeon só deve ser gerada depois da seleção.");

        yield return Tap(pad1.buttonSouth);
        yield return Tap(pad2.buttonSouth);
        yield return Tap(keyboard.spaceKey);

        Assert.AreEqual(3, PlayerInput.all.Count, "2 controles + teclado deveriam criar 3 jogadores.");
        AssertOwnedBySinglePlayer(pad1);
        AssertOwnedBySinglePlayer(pad2);
        AssertOwnedBySinglePlayer(keyboard);
        AssertOwnedBySinglePlayer(mouse);

        yield return Tap(pad1.buttonEast);
        Assert.AreEqual(3, PlayerInput.all.Count);

        yield return new WaitForSeconds(0.5f);

        yield return Push(pad2.leftStick, Vector2.left);
        yield return Tap(pad1.buttonSouth);
        yield return Tap(pad2.buttonSouth);
        yield return Tap(keyboard.enterKey);

        yield return new WaitForSeconds(3f);
        Assert.AreEqual(0, FindCharacters().Count, "A partida começou mesmo com J2 tentando a classe do J1.");

        yield return Push(pad2.leftStick, Vector2.right);
        yield return Tap(pad2.buttonSouth);

        yield return new WaitForSeconds(2.5f);
        Assert.AreEqual(0, FindCharacters().Count, "A partida começou antes da contagem de 5 s.");

        yield return Push(pad2.leftStick, Vector2.right);
        yield return new WaitForSeconds(6f);
        Assert.AreEqual(0, FindCharacters().Count, "A contagem não parou quando o J2 foi trocar de personagem.");

        yield return Push(pad2.leftStick, Vector2.left);
        yield return Tap(pad2.buttonSouth);

        GameObject dungeon = null;
        float timeout = Time.realtimeSinceStartup + 20f;
        while (dungeon == null && Time.realtimeSinceStartup < timeout)
        {
            dungeon = GameObject.Find("Dungeon_Generated");
            yield return null;
        }

        Assert.IsNotNull(dungeon, "A dungeon não foi gerada depois que todos confirmaram.");
        yield return new WaitForSeconds(1f);

        List<GameObject> characters = FindCharacters();
        Assert.AreEqual(3, characters.Count, "Deveriam existir 3 personagens.");
        AssertHasCharacter(characters, "Player_Warrior_P1");
        AssertHasCharacter(characters, "Player_Ranger_P2");
        AssertHasCharacter(characters, "Player_Wizard_P3");

        Bounds start = StartRoomBounds(dungeon);
        foreach (GameObject character in characters)
        {
            Assert.IsTrue(start.Contains(character.transform.position), $"{character.name} em {character.transform.position} fora da sala Start {start}.");
            Assert.IsNotNull(character.GetComponentInChildren<Animator>().runtimeAnimatorController, $"{character.name} sem Animator de classe.");
        }

        CinemachineTargetGroup group = Object.FindAnyObjectByType<CinemachineTargetGroup>();
        Assert.IsNotNull(group, "Grupo de alvos da câmera não encontrado.");
        Assert.AreEqual(3, CountMembers(group), "A câmera deveria enquadrar os 3 jogadores.");

        Gamepad pad3 = InputSystem.AddDevice<Gamepad>();
        yield return Tap(pad3.buttonSouth);
        yield return new WaitForSeconds(0.5f);
        yield return Tap(pad3.buttonSouth);
        yield return new WaitForSeconds(0.5f);

        Assert.AreEqual(3, PlayerInput.all.Count, "Ninguém pode entrar depois que a partida começou.");
        Assert.AreEqual(3, FindCharacters().Count, "Nenhum personagem pode nascer depois que a partida começou.");
        Assert.AreEqual(3, CountMembers(group), "A câmera continua com os mesmos 3 jogadores.");
    }

    private IEnumerator Tap(ButtonControl button)
    {
        Press(button);
        yield return null;
        yield return null;
        Release(button);
        yield return null;
        yield return null;
    }

    private IEnumerator Push(StickControl stick, Vector2 direction)
    {
        Set(stick, direction);
        yield return null;
        yield return null;
        Set(stick, Vector2.zero);
        yield return null;
        yield return null;
    }

    private static void AssertOwnedBySinglePlayer(InputDevice device)
    {
        int owners = 0;
        foreach (PlayerInput player in PlayerInput.all)
        {
            foreach (InputDevice paired in player.devices)
            {
                if (paired == device) owners++;
            }
        }

        Assert.AreEqual(1, owners, $"{device} deveria pertencer a exatamente 1 jogador.");
    }

    private static List<GameObject> FindCharacters()
    {
        return new List<GameObject>(GameObject.FindGameObjectsWithTag("Player"));
    }

    private static GameObject AssertHasCharacter(List<GameObject> characters, string expectedName)
    {
        GameObject found = characters.Find(c => c.name == expectedName);
        Assert.IsNotNull(found, $"Personagem '{expectedName}' não encontrado. Existentes: {string.Join(", ", characters.ConvertAll(c => c.name))}");
        return found;
    }

    private static Bounds StartRoomBounds(GameObject dungeon)
    {
        foreach (Transform marker in dungeon.transform.Find("RoomMarkers"))
        {
            if (marker.name.EndsWith("_Start"))
            {
                Bounds bounds = marker.GetComponent<BoxCollider>().bounds;
                bounds.Expand(new Vector3(2f, 10f, 2f));
                return bounds;
            }
        }

        Assert.Fail("Sala inicial não encontrada.");
        return default;
    }

    private static int CountMembers(CinemachineTargetGroup group)
    {
        int count = 0;
        foreach (CinemachineTargetGroup.Target target in group.Targets)
        {
            if (target.Object != null) count++;
        }

        return count;
    }
}
