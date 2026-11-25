using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public class GameplayPhasesTest : MatchFixture
{
    [UnityTest]
    public IEnumerator Phase2_ClassStats_RangedAttack_Ability_AndRevive()
    {
        var pads = new List<Gamepad>();
        yield return StartMatch(pads, 2);

        GameObject warrior = TestHelpers.Character("Player_Warrior_P1");
        GameObject ranger = TestHelpers.Character("Player_Ranger_P2");
        Component warriorStatus = TestHelpers.Comp(warrior, "PlayerStatus");
        Component rangerStatus = TestHelpers.Comp(ranger, "PlayerStatus");

        Assert.AreEqual(150f, TestHelpers.Get<float>(warriorStatus, "MaxHealth"), 0.01f, "Vida do Guerreiro.");
        Assert.AreEqual(90f, TestHelpers.Get<float>(rangerStatus, "MaxHealth"), 0.01f, "Vida da Arqueira.");

        yield return Tap(pads[1].buttonWest);
        yield return null;
        Assert.IsNotNull(GameObject.Find("PlayerArrow(Clone)"), "A Arqueira não disparou flecha.");

        yield return Tap(pads[0].buttonEast);
        Assert.Less(TestHelpers.Get<float>(warriorStatus, "BlockMultiplier"), 1f, "O escudo do Guerreiro não ativou.");

        Component abilities = TestHelpers.Comp(warrior, "PlayerAbilities");
        Assert.IsFalse((bool)TestHelpers.Call(abilities, "IsUnlocked", 1), "Habilidade 2 deveria começar travada.");

        rangerStatus.SendMessage("TakeDamage", 9999f);
        yield return null;
        Assert.IsTrue(TestHelpers.Get<bool>(rangerStatus, "IsDead"), "A Arqueira deveria ter caído.");
        TestHelpers.Teleport(warrior, ranger.transform.position + Vector3.right * 1f);
        yield return TestHelpers.WaitRealtime(0.6f);

        Press(pads[0].buttonNorth);
        yield return TestHelpers.WaitRealtime(2.6f);
        Release(pads[0].buttonNorth);
        yield return null;
        Assert.IsFalse(TestHelpers.Get<bool>(rangerStatus, "IsDead"), "A Arqueira não foi revivida segurando Y.");
        Assert.AreEqual(45f, TestHelpers.Get<float>(rangerStatus, "CurrentHealth"), 1f, "Reviver deveria devolver 50% da vida.");
    }

    [UnityTest]
    public IEnumerator RangedClasses_ProjectilesDamageEnemies()
    {
        var pads = new List<Gamepad>();
        yield return StartMatch(pads, 3);

        foreach ((string name, Gamepad pad) in new[] { ("Player_Ranger_P2", pads[1]), ("Player_Wizard_P3", pads[2]) })
        {
            GameObject shooter = TestHelpers.Character(name);
            Component enemy = AliveEnemy();
            Assert.IsNotNull(enemy, "Nenhum inimigo vivo para o teste.");

            Vector3 front = shooter.transform.position + Vector3.forward * 4f;
            UnityEngine.AI.NavMeshAgent agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
            Assert.IsTrue(UnityEngine.AI.NavMesh.SamplePosition(front, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas), "Sem NavMesh à frente do atirador.");
            agent.Warp(hit.position);
            shooter.transform.rotation = Quaternion.LookRotation(hit.position - shooter.transform.position);
            shooter.GetComponent<Rigidbody>().rotation = shooter.transform.rotation;
            yield return null;

            float before = TestHelpers.Get<float>(enemy, "CurrentHealth");
            Vector3 shooterPos = shooter.transform.position;
            Vector3 enemyPos = enemy.transform.position;
            yield return Tap(pad.buttonWest);
            GameObject projectile = GameObject.Find("PlayerArrow(Clone)") ?? GameObject.Find("PlayerMagicBolt(Clone)");
            string projectileInfo = projectile != null ? $"projétil em {projectile.transform.position} indo para {projectile.transform.forward}" : "nenhum projétil encontrado";
            yield return TestHelpers.WaitRealtime(0.8f);
            float after = TestHelpers.Get<float>(enemy, "CurrentHealth");
            bool dead = TestHelpers.Get<bool>(enemy, "IsDead");

            string diagnosis = "";
            if (!dead && after >= before)
            {
                Vector3 from = shooterPos + Vector3.up * 0.85f;
                Vector3 to = enemyPos + Vector3.up * 0.85f;
                var hits = new List<string>();
                foreach (RaycastHit h in Physics.SphereCastAll(from, 0.5f, (to - from).normalized, (to - from).magnitude + 1f, ~0, QueryTriggerInteraction.Ignore))
                {
                    hits.Add($"{h.collider.name}[{LayerMask.LayerToName(h.collider.gameObject.layer)}]@{h.distance:0.0}");
                }

                Collider enemyCollider = enemy.GetComponent<Collider>();
                diagnosis = $" | inimigo {enemy.name} em {enemyPos} (agora {enemy.transform.position}), atirador em {shooterPos}, " +
                            $"colisor do inimigo {(enemyCollider != null ? enemyCollider.bounds.ToString() : "NENHUM")} ativo={(enemyCollider != null && enemyCollider.enabled)}, " +
                            $"{projectileInfo}, no caminho: {string.Join(", ", hits)}";
            }

            Assert.IsTrue(dead || after < before, $"{name}: o projétil não causou dano ({before} → {after}){diagnosis}");
        }
    }

    private static Vector3 StartRoomCenter()
    {
        GameObject dungeon = GameObject.Find("Dungeon_Generated");
        foreach (Transform marker in dungeon.transform.Find("RoomMarkers"))
        {
            if (marker.name.EndsWith("_Start")) return marker.GetComponent<BoxCollider>().bounds.center;
        }

        Assert.Fail("Sala inicial não encontrada.");
        return default;
    }

    private static Component AliveEnemy()
    {
        foreach (Component status in TestHelpers.FindComponents("EnemyStatus"))
        {
            if (!TestHelpers.Get<bool>(status, "IsDead") && TestHelpers.Comp(status.gameObject, "BossController") == null) return status;
        }

        return null;
    }

    [UnityTest]
    public IEnumerator PartyLeash_PlayersStayInsideCameraView()
    {
        var pads = new List<Gamepad>();
        yield return StartMatch(pads, 2);

        Camera cam = Camera.main;
        GameObject warrior = TestHelpers.Character("Player_Warrior_P1");
        GameObject ranger = TestHelpers.Character("Player_Ranger_P2");
        Vector3 spot = StartRoomCenter();
        spot.y = warrior.transform.position.y;
        TestHelpers.Teleport(warrior, spot);
        TestHelpers.Teleport(ranger, spot + Vector3.forward * 1.5f);
        yield return TestHelpers.WaitRealtime(1f);

        var ground = new Plane(Vector3.up, new Vector3(0f, ranger.transform.position.y, 0f));
        Ray ray = cam.ViewportPointToRay(new Vector3(0.985f, 0.5f, 0f));
        Assert.IsTrue(ground.Raycast(ray, out float enter), "Não achei o chão na borda da tela.");
        Vector3 edge = ray.GetPoint(enter);
        TestHelpers.Teleport(ranger, new Vector3(edge.x, ranger.transform.position.y, edge.z));
        yield return null;
        float before = cam.WorldToViewportPoint(ranger.transform.position).x;

        Set(pads[1].leftStick, Vector2.right);
        yield return TestHelpers.WaitRealtime(1.5f);
        Set(pads[1].leftStick, Vector2.zero);
        yield return TestHelpers.WaitRealtime(0.2f);

        float after = cam.WorldToViewportPoint(ranger.transform.position).x;
        Assert.LessOrEqual(after, 0.975f, $"A Arqueira saiu da área da câmera (x de tela {before:0.000} → {after:0.000}, limite 0,96).");
        Assert.GreaterOrEqual(cam.WorldToViewportPoint(warrior.transform.position).x, 0.025f, "O Guerreiro saiu pela borda esquerda da tela.");

        float x0 = ranger.transform.position.x;
        Set(pads[1].leftStick, Vector2.left);
        yield return TestHelpers.WaitRealtime(0.6f);
        Set(pads[1].leftStick, Vector2.zero);
        yield return TestHelpers.WaitRealtime(0.2f);
        Assert.Less(ranger.transform.position.x, x0 - 1f, "A Arqueira não conseguiu voltar para dentro da tela.");
    }

    [UnityTest]
    public IEnumerator Doors_LockedUntilRoomIsCleared()
    {
        var pads = new List<Gamepad>();
        yield return StartMatch(pads, 1);

        Component dungeon = TestHelpers.FindComponent("DungeonManager");
        Component door = null;
        Vector3 inside = default;
        int roomId = -1;
        foreach (Component candidate in TestHelpers.FindComponents("DungeonDoor"))
        {
            Vector3 center = TestHelpers.Get<Vector3>(candidate, "Center");
            Vector3 normal = TestHelpers.Get<Vector3>(candidate, "Normal");
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 point = center + normal * side * 2.2f;
                object room = TestHelpers.Call(dungeon, "RoomAt", point);
                if (room == null) continue;
                int id = TestHelpers.Get<int>(room, "id");
                if ((int)TestHelpers.Call(dungeon, "AliveEnemiesInRoom", id) == 0) continue;
                if (!UnityEngine.AI.NavMesh.SamplePosition(point, out UnityEngine.AI.NavMeshHit hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (TestHelpers.Call(dungeon, "RoomAt", hit.position) == null) continue;
                door = candidate;
                inside = hit.position;
                roomId = id;
                break;
            }

            if (door != null) break;
        }

        if (door == null)
        {
            Assert.Inconclusive("Nenhuma porta de sala com inimigos neste nível.");
            yield break;
        }

        GameObject warrior = TestHelpers.Character("Player_Warrior_P1");
        TestHelpers.Teleport(warrior, new Vector3(inside.x, warrior.transform.position.y, inside.z));
        yield return TestHelpers.WaitRealtime(0.3f);

        yield return Tap(pads[0].buttonNorth);
        yield return TestHelpers.WaitRealtime(0.6f);
        Assert.IsFalse(TestHelpers.Get<bool>(door, "IsOpen"), "A porta abriu com inimigos vivos na sala.");

        foreach (Component enemy in TestHelpers.FindComponents("EnemyStatus"))
        {
            if (TestHelpers.Get<int>(enemy, "HomeRoomId") == roomId && !TestHelpers.Get<bool>(enemy, "IsDead"))
            {
                TestHelpers.Call(enemy, "TakeDamage", 1e7f);
            }
        }

        yield return TestHelpers.WaitRealtime(0.3f);
        TestHelpers.Teleport(warrior, new Vector3(inside.x, warrior.transform.position.y, inside.z));
        yield return Tap(pads[0].buttonNorth);
        yield return TestHelpers.WaitRealtime(1f);
        Assert.IsTrue(TestHelpers.Get<bool>(door, "IsOpen"), "A porta não abriu depois de limpar a sala.");
    }

    [UnityTest]
    public IEnumerator Phase4_CardsNeedUnanimousVote_AndDuelTransfersLevel()
    {
        var pads = new List<Gamepad>();
        yield return StartMatch(pads, 2);

        GameObject warrior = TestHelpers.Character("Player_Warrior_P1");
        GameObject ranger = TestHelpers.Character("Player_Ranger_P2");
        Component warriorProgression = TestHelpers.Comp(warrior, "PlayerProgression");
        Component rangerProgression = TestHelpers.Comp(ranger, "PlayerProgression");
        Component draft = TestHelpers.FindComponent("CardDraftManager");
        Assert.IsNotNull(draft, "CardDraftManager não está na cena.");

        warriorProgression.SendMessage("DebugAddXP");
        yield return TestHelpers.WaitUntilRealtime(() => TestHelpers.Get(draft, "CurrentPhase").ToString() == "Voting", 3f, "O sorteio de cartas não abriu.");
        Assert.AreEqual(0f, Time.timeScale, "O jogo deveria pausar durante as cartas.");
        Assert.AreEqual(3, TestHelpers.Get<int>(draft, "OfferedCount"), "Com 2 jogadores devem ser 3 cartas.");

        yield return TestHelpers.WaitRealtime(0.3f);
        yield return Tap(pads[0].buttonSouth);
        yield return Tap(pads[1].buttonSouth);
        Assert.AreEqual("Voting", TestHelpers.Get(draft, "CurrentPhase").ToString(), "Votos diferentes não podem resolver a carta.");

        yield return Tap(pads[1].buttonEast);
        yield return Push(pads[1].leftStick, Vector2.left);
        yield return Tap(pads[1].buttonSouth);
        Assert.AreEqual("Result", TestHelpers.Get(draft, "CurrentPhase").ToString(), "Voto unânime deveria aplicar a carta.");
        Assert.IsFalse(string.IsNullOrEmpty((string)TestHelpers.Get(draft, "LastResult")));

        yield return TestHelpers.WaitUntilRealtime(() => TestHelpers.Get(draft, "CurrentPhase").ToString() == "ChallengeOffer", 4f, "A oferta de desafio não apareceu.");
        yield return TestHelpers.WaitRealtime(0.3f);
        yield return Tap(pads[1].buttonSouth);
        Assert.AreEqual("DuelWait", TestHelpers.Get(draft, "CurrentPhase").ToString(), "O duelo não começou.");
        yield return TestHelpers.WaitUntilRealtime(() => TestHelpers.Get(draft, "CurrentPhase").ToString() == "DuelGo", 5f, "O \"JÁ!\" não apareceu.");
        yield return Tap(pads[1].buttonSouth);
        Assert.AreEqual("DuelResult", TestHelpers.Get(draft, "CurrentPhase").ToString());

        Assert.AreEqual(1, TestHelpers.Get<int>(warriorProgression, "CurrentLevel"), "Quem perdeu o duelo deveria voltar ao nível 1.");
        Assert.AreEqual(2, TestHelpers.Get<int>(rangerProgression, "CurrentLevel"), "Quem venceu deveria ficar com o XP do nível.");
    }

    [UnityTest]
    public IEnumerator Phase5_SafeZoneOrbs_WinnerIsWhoCollectsMore()
    {
        var pads = new List<Gamepad>();
        yield return StartMatch(pads, 2);

        Component minigame = TestHelpers.FindComponent("SafeZoneMinigame");
        if (minigame == null)
        {
            Assert.Inconclusive("Este nível foi gerado sem zona segura.");
            yield break;
        }

        GameObject warrior = TestHelpers.Character("Player_Warrior_P1");
        GameObject ranger = TestHelpers.Character("Player_Ranger_P2");
        minigame.SendMessage("UseShortTimings");
        minigame.SendMessage("JoinPlayer", warrior);
        Assert.AreEqual("Waiting", TestHelpers.Get(minigame, "CurrentState").ToString(), "Com 1 participante ainda espera.");
        minigame.SendMessage("JoinPlayer", ranger);
        Assert.AreEqual("Countdown", TestHelpers.Get(minigame, "CurrentState").ToString(), "Com 2 participantes começa a contagem.");

        yield return TestHelpers.WaitUntilRealtime(() => TestHelpers.Get(minigame, "CurrentState").ToString() == "Playing", 3f, "O minijogo não começou.");

        TestHelpers.Teleport(ranger, ranger.transform.position + Vector3.forward * 40f);
        float end = Time.realtimeSinceStartup + 4f;
        while (TestHelpers.Get(minigame, "CurrentState").ToString() == "Playing" && Time.realtimeSinceStartup < end)
        {
            GameObject orb = GameObject.Find("MinigameOrb");
            if (orb != null) TestHelpers.Teleport(warrior, new Vector3(orb.transform.position.x, warrior.transform.position.y, orb.transform.position.z));
            yield return null;
            yield return null;
        }

        Assert.AreEqual("Done", TestHelpers.Get(minigame, "CurrentState").ToString(), "O minijogo não terminou.");
        StringAssert.Contains("venceu", (string)TestHelpers.Get(minigame, "ResultText"));
        StringAssert.Contains("J1", (string)TestHelpers.Get(minigame, "ResultText"));
    }
}
