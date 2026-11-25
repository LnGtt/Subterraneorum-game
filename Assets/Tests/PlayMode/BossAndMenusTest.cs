using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class BossAndMenusTest
{
    [TearDown]
    public void TearDown()
    {
        TestHelpers.ResetPause();
    }

    private static IEnumerator StartQuick()
    {
        GameObject lobby = GameObject.Find("ClassSelectLobby");
        Assert.IsNotNull(lobby, "Tela de seleção não encontrada.");
        lobby.SendMessage("QuickStart");
        yield return TestHelpers.WaitForDungeon();
    }

    [UnityTest]
    public IEnumerator Phase3_BossOnFinalLevel_RandomAreaAttacks_AndVictoryScreen()
    {
        SceneManager.LoadScene("Game");
        yield return null;
        yield return StartQuick();

        Component dungeon = TestHelpers.FindComponent("DungeonManager");
        dungeon.SendMessage("GoToFinalLevel");
        yield return TestHelpers.WaitUntilRealtime(() => TestHelpers.Get<bool>(dungeon, "IsOnFinalLevel"), 20f, "Não foi para o último andar.");
        yield return TestHelpers.WaitRealtime(1.5f);

        dungeon.SendMessage("SpawnBossNow");
        yield return null;
        Component boss = TestHelpers.FindComponent("BossController");
        Assert.IsNotNull(boss, "O chefão não apareceu na sala Boss.");

        for (int attack = 0; attack < 3; attack++)
        {
            boss.SendMessage("ForceAttack", attack);
            yield return null;
            Assert.IsTrue(GameObject.Find("Telegraph_Disc") != null || GameObject.Find("Telegraph_Ring") != null, $"Ataque {attack} sem aviso no chão.");
            yield return TestHelpers.WaitUntilRealtime(() => !TestHelpers.Get<bool>(boss, "IsAttacking"), 8f, $"Ataque {attack} não terminou.");
        }

        Assert.GreaterOrEqual(TestHelpers.Get<int>(boss, "AttacksPerformed"), 3);

        Component bossStatus = boss.GetComponent("EnemyStatus");
        TestHelpers.Call(bossStatus, "TakeDamage", 1e7f);
        Component end = TestHelpers.FindComponent("EndScreen");
        yield return TestHelpers.WaitUntilRealtime(() => TestHelpers.Get<bool>(end, "IsOpen"), 6f, "A tela de vitória não apareceu.");
        Assert.IsTrue(TestHelpers.Get<bool>(end, "ShowingVictory"), "Deveria ser a tela de VITÓRIA.");
        Assert.AreEqual(0f, Time.timeScale, "A tela de fim deveria pausar o jogo.");
    }

    [UnityTest]
    public IEnumerator Phase6_MainMenuPlayLoadsGame_AndPauseMenuToggles()
    {
        SceneManager.LoadScene("MainMenu");
        yield return null;
        yield return null;
        Component menu = TestHelpers.FindComponent("MainMenuController");
        Assert.IsNotNull(menu, "Menu principal não encontrado.");
        menu.SendMessage("Play");
        yield return TestHelpers.WaitUntilRealtime(() => SceneManager.GetActiveScene().name == "Game", 10f, "\"Jogar\" não abriu a cena Game.");
        yield return null;

        yield return StartQuick();
        Assert.IsNotNull(TestHelpers.FindComponent("GameHud"), "HUD não está na cena.");
        yield return TestHelpers.WaitRealtime(0.3f);
        Assert.IsNotNull(GameObject.Find("Minimap"), "O minimapa não apareceu depois de gerar o nível.");

        Component pause = TestHelpers.FindComponent("PauseMenu");
        pause.SendMessage("OpenForAnyone");
        yield return null;
        Assert.IsTrue(TestHelpers.Get<bool>(pause, "IsOpen"), "A pausa não abriu.");
        Assert.AreEqual(0f, Time.timeScale);
        pause.SendMessage("Resume");
        yield return null;
        Assert.IsFalse(TestHelpers.Get<bool>(pause, "IsOpen"));
        Assert.AreEqual(1f, Time.timeScale, "Continuar deveria despausar.");
    }
}
