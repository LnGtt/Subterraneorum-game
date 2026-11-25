using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace Subterraneorum.DungeonSystem
{
    [RequireComponent(typeof(SafeZone))]
    public class SafeZoneMinigame : MonoBehaviour
    {
        public enum State
        {
            Waiting,
            Countdown,
            Playing,
            Done,
        }

        [SerializeField] private float countdownSeconds = 5f;
        [SerializeField] private float durationSeconds = 20f;
        [SerializeField, Range(0f, 1f)] private float stealPercent = 0.25f;
        [SerializeField] private int orbCount = 4;
        [SerializeField] private float pickupRadius = 1.1f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private SafeZone zone;
        private BoxCollider box;
        private State state;
        private float timer;
        private readonly List<PlayerStatus> participants = new List<PlayerStatus>();
        private readonly Dictionary<PlayerStatus, int> scores = new Dictionary<PlayerStatus, int>();
        private readonly Dictionary<PlayerInputHandler, Action> joinHandlers = new Dictionary<PlayerInputHandler, Action>();
        private readonly List<Transform> orbs = new List<Transform>();
        private Text label;
        private string resultText;

        public State CurrentState => state;
        public string ResultText => resultText;

        private void Awake()
        {
            zone = GetComponent<SafeZone>();
            box = GetComponent<BoxCollider>();
            BuildLabel();
        }

        private void OnEnable()
        {
            zone.OnPlayersInsideChanged += HandleInsideChanged;
        }

        private void OnDisable()
        {
            zone.OnPlayersInsideChanged -= HandleInsideChanged;
            foreach (KeyValuePair<PlayerInputHandler, Action> pair in joinHandlers)
            {
                if (pair.Key != null) pair.Key.OnInteractInput -= pair.Value;
            }

            joinHandlers.Clear();
            ClearOrbs();
        }

        private void HandleInsideChanged(SafeZone _)
        {
            var inside = new HashSet<PlayerInputHandler>();
            foreach (PlayerStatus player in zone.PlayersInside)
            {
                if (player == null) continue;
                PlayerInputHandler handler = player.GetComponent<PlayerInputHandler>();
                if (handler == null) continue;
                inside.Add(handler);
                if (!joinHandlers.ContainsKey(handler))
                {
                    PlayerStatus captured = player;
                    Action join = () => Join(captured);
                    joinHandlers[handler] = join;
                    handler.OnInteractInput += join;
                }
            }

            var left = new List<PlayerInputHandler>();
            foreach (PlayerInputHandler handler in joinHandlers.Keys)
            {
                if (!inside.Contains(handler)) left.Add(handler);
            }

            foreach (PlayerInputHandler handler in left)
            {
                if (handler != null) handler.OnInteractInput -= joinHandlers[handler];
                joinHandlers.Remove(handler);
            }

            RefreshLabel();
        }

        public void JoinPlayer(GameObject player)
        {
            if (player != null) Join(player.GetComponent<PlayerStatus>());
        }

        public void UseShortTimings()
        {
            countdownSeconds = 0.5f;
            durationSeconds = 2f;
        }

        private void Join(PlayerStatus player)
        {
            if (player == null || player.IsDead || participants.Contains(player)) return;
            if (state != State.Waiting && state != State.Countdown) return;

            participants.Add(player);
            scores[player] = 0;
            if (participants.Count >= 2 && state == State.Waiting)
            {
                state = State.Countdown;
                timer = countdownSeconds;
            }

            RefreshLabel();
        }

        private void Update()
        {
            switch (state)
            {
                case State.Countdown:
                    timer -= Time.deltaTime;
                    if (timer <= 0f) StartGame();
                    RefreshLabelIfChanged();
                    break;
                case State.Playing:
                    timer -= Time.deltaTime;
                    CheckPickups();
                    AnimateOrbs();
                    if (timer <= 0f) EndGame();
                    RefreshLabelIfChanged();
                    break;
            }
        }

        private int shownKey = int.MinValue;

        private void RefreshLabelIfChanged()
        {
            int total = 0;
            foreach (int score in scores.Values) total += score;
            int key = ((int)state * 100000) + Mathf.CeilToInt(Mathf.Max(0f, timer)) * 1000 + total;
            if (key == shownKey) return;
            shownKey = key;
            RefreshLabel();
        }

        private void StartGame()
        {
            state = State.Playing;
            timer = durationSeconds;
            for (int i = 0; i < orbCount; i++) orbs.Add(CreateOrb());
            Debug.Log($"[SafeZoneMinigame] Orbes começou com {participants.Count} jogadores.");
        }

        private void CheckPickups()
        {
            foreach (PlayerStatus player in participants)
            {
                if (player == null || player.IsDead) continue;
                foreach (Transform orb in orbs)
                {
                    Vector3 offset = orb.position - player.transform.position;
                    offset.y = 0f;
                    if (offset.sqrMagnitude <= pickupRadius * pickupRadius)
                    {
                        scores[player]++;
                        orb.position = RandomPoint();
                        GameAudio.Play(GameSound.OrbCollect);
                    }
                }
            }
        }

        private void AnimateOrbs()
        {
            float bob = Mathf.Sin(Time.time * 4f) * 0.15f;
            foreach (Transform orb in orbs)
            {
                Vector3 p = orb.position;
                orb.position = new Vector3(p.x, box.bounds.min.y + 0.8f + bob, p.z);
            }
        }

        private void EndGame()
        {
            state = State.Done;
            ClearOrbs();

            PlayerStatus winner = null;
            int best = -1;
            bool tie = false;
            foreach (PlayerStatus player in participants)
            {
                if (player == null) continue;
                int score = scores[player];
                if (score > best)
                {
                    best = score;
                    winner = player;
                    tie = false;
                }
                else if (score == best)
                {
                    tie = true;
                }
            }

            if (winner == null || tie || best <= 0)
            {
                resultText = "Empate! Ninguém roubou XP.";
            }
            else
            {
                float stolen = 0f;
                foreach (PlayerStatus player in participants)
                {
                    if (player == null || player == winner) continue;
                    PlayerProgression progression = player.GetComponent<PlayerProgression>();
                    if (progression != null) stolen += progression.RemoveXP(progression.CurrentXP * stealPercent);
                }

                PlayerProgression winnerProgression = winner.GetComponent<PlayerProgression>();
                if (winnerProgression != null) winnerProgression.AddXP(stolen, applyMultiplier: false);
                resultText = $"{LabelOf(winner)} venceu com {best} orbes e roubou {Mathf.RoundToInt(stolen)} XP!";
            }

            Debug.Log($"[SafeZoneMinigame] {resultText}");
            RefreshLabel();
        }

        private Transform CreateOrb()
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "MinigameOrb";
            orb.layer = 2;
            Destroy(orb.GetComponent<Collider>());
            orb.transform.SetParent(transform, true);
            orb.transform.localScale = Vector3.one * 0.55f;
            orb.transform.position = RandomPoint();

            Renderer renderer = orb.GetComponent<Renderer>();
            if (RuntimeAssets.Glow != null) renderer.sharedMaterial = RuntimeAssets.Glow;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, UiFactory.Gold);
            block.SetColor(EmissionId, UiFactory.Gold * 2.5f);
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return orb.transform;
        }

        private Vector3 RandomPoint()
        {
            Bounds b = box.bounds;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var desired = new Vector3(
                    UnityEngine.Random.Range(b.min.x + 1.5f, b.max.x - 1.5f),
                    b.min.y,
                    UnityEngine.Random.Range(b.min.z + 1.5f, b.max.z - 1.5f));
                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) && b.Contains(new Vector3(hit.position.x, b.center.y, hit.position.z)))
                {
                    return new Vector3(hit.position.x, b.min.y + 0.8f, hit.position.z);
                }
            }

            return new Vector3(b.center.x, b.min.y + 0.8f, b.center.z);
        }

        private void ClearOrbs()
        {
            foreach (Transform orb in orbs)
            {
                if (orb != null) Destroy(orb.gameObject);
            }

            orbs.Clear();
        }

        private static string LabelOf(PlayerStatus player)
        {
            PlayerIdentity identity = player != null ? player.GetComponent<PlayerIdentity>() : null;
            if (identity == null) return "?";
            Color color = identity.Class != null ? identity.Class.color : Color.white;
            return UiFactory.Colored(identity.PlayerLabel, color);
        }

        private void BuildLabel()
        {
            var go = new GameObject("MinigameLabel", typeof(RectTransform), typeof(Canvas), typeof(Billboard));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 3.2f, 0f);
            go.transform.localScale = Vector3.one * 0.012f;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 260f);

            Image back = UiFactory.Box(go.transform, "Back", new Color(0f, 0f, 0f, 0.55f));
            UiFactory.Stretch(back.rectTransform);
            label = UiFactory.Label(go.transform, "Text", "", 40, UiFactory.Gold, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(label.rectTransform, 12f);
            RefreshLabel();
        }

        private void RefreshLabel()
        {
            if (label == null) return;

            var names = new List<string>();
            foreach (PlayerStatus player in participants)
            {
                names.Add(state == State.Playing || state == State.Done ? $"{LabelOf(player)}: {scores[player]}" : LabelOf(player));
            }

            switch (state)
            {
                case State.Waiting:
                    bool anyoneInside = zone != null && zone.PlayersInside.Count > 0;
                    label.text = participants.Count == 0
                        ? "Zona segura (cura)\nMinijogo das Orbes: aperte E / Y para entrar"
                        : $"Orbes: {string.Join(" ", names)} entrou\nFalta mais 1 jogador (E / Y)";
                    label.transform.parent.gameObject.SetActive(anyoneInside || participants.Count > 0);
                    break;
                case State.Countdown:
                    label.transform.parent.gameObject.SetActive(true);
                    label.text = $"Orbes: {string.Join(" ", names)}\nComeça em {Mathf.CeilToInt(Mathf.Max(0f, timer))}... (ainda dá para entrar)";
                    break;
                case State.Playing:
                    label.transform.parent.gameObject.SetActive(true);
                    label.text = $"Pegue as orbes! {Mathf.CeilToInt(Mathf.Max(0f, timer))}s\n{string.Join("   ", names)}";
                    break;
                case State.Done:
                    label.transform.parent.gameObject.SetActive(true);
                    label.text = $"{resultText}\n{string.Join("   ", names)}";
                    break;
            }
        }
    }
}
