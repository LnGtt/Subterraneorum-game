using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace Subterraneorum.DungeonSystem
{
    public class DungeonDoor : MonoBehaviour
    {
        [SerializeField] private float interactRange = 3.2f;
        [SerializeField] private float openSeconds = 0.6f;
        [SerializeField] private float openAngle = 100f;

        private static readonly List<DungeonDoor> all = new List<DungeonDoor>();
        private readonly List<(Transform leaf, Quaternion closed, float sign)> leaves = new List<(Transform, Quaternion, float)>();
        private readonly Dictionary<PlayerInputHandler, System.Action> subscriptions = new Dictionary<PlayerInputHandler, System.Action>();

        private BoxCollider blocker;
        private NavMeshObstacle obstacle;
        private GameObject prompt;
        private Text promptText;
        private int shownRemaining = -1;
        private Vector3 center;
        private Vector3 normal;
        private bool open;
        private bool opening;
        private DungeonManager dungeon;

        public bool IsOpen => open;
        public Vector3 Center => center;

        public Vector3 Normal => normal;
        public static IReadOnlyList<DungeonDoor> All => all;

        public void Setup(float yaw, float width)
        {
            center = transform.position;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            normal = rotation * Vector3.forward;
            dungeon = FindAnyObjectByType<DungeonManager>();

            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
            {
                if (child == transform) continue;
                if (child.name.EndsWith("_L")) leaves.Add((child, child.localRotation, 1f));
                else if (child.name.EndsWith("_R")) leaves.Add((child, child.localRotation, -1f));
            }

            var blockerGo = new GameObject("DoorBlocker") { layer = LayerMask.NameToLayer("Environment") };
            blockerGo.transform.SetParent(transform.parent, false);
            blockerGo.transform.SetPositionAndRotation(center + Vector3.up * 1.5f, rotation);
            blocker = blockerGo.AddComponent<BoxCollider>();
            blocker.size = new Vector3(width, 3f, 0.5f);

            NavMeshModifier modifier = blockerGo.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = true;
            obstacle = blockerGo.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(width, 3f, 0.6f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;

            BuildPrompt();
        }

        private void OnEnable()
        {
            all.Add(this);
            PlayerRegistry.OnPlayerRegistered += Subscribe;
            PlayerRegistry.OnPlayerUnregistered += Unsubscribe;
            foreach (PlayerStatus player in PlayerRegistry.Players) Subscribe(player);
        }

        private void OnDisable()
        {
            all.Remove(this);
            PlayerRegistry.OnPlayerRegistered -= Subscribe;
            PlayerRegistry.OnPlayerUnregistered -= Unsubscribe;
            foreach (KeyValuePair<PlayerInputHandler, System.Action> pair in subscriptions)
            {
                if (pair.Key == null) continue;
                pair.Key.OnInteractInput -= pair.Value;
                pair.Key.OnAttackInput -= pair.Value;
            }

            subscriptions.Clear();
        }

        private void Subscribe(PlayerStatus player)
        {
            PlayerInputHandler handler = player != null ? player.GetComponent<PlayerInputHandler>() : null;
            if (handler == null || subscriptions.ContainsKey(handler)) return;
            System.Action callback = () => TryOpenBy(player);
            subscriptions[handler] = callback;
            handler.OnInteractInput += callback;
            handler.OnAttackInput += callback;
        }

        private void Unsubscribe(PlayerStatus player)
        {
            PlayerInputHandler handler = player != null ? player.GetComponent<PlayerInputHandler>() : null;
            if (handler == null || !subscriptions.TryGetValue(handler, out System.Action callback)) return;
            handler.OnInteractInput -= callback;
            handler.OnAttackInput -= callback;
            subscriptions.Remove(handler);
        }

        private void TryOpenBy(PlayerStatus player)
        {
            if (open || opening || player == null || player.IsDead || !InRange(player.transform.position)) return;
            if (EnemiesBlocking(player.transform.position) > 0) return;
            Open();
        }

        public int EnemiesBlocking(Vector3 position)
        {
            if (dungeon == null) dungeon = FindAnyObjectByType<DungeonManager>();
            if (dungeon == null) return 0;
            ProceduralRuins.GeneratedRoom room = dungeon.RoomAt(position);
            return room != null ? dungeon.AliveEnemiesInRoom(room.id) : 0;
        }

        private bool InRange(Vector3 position)
        {
            Vector3 offset = position - center;
            offset.y = 0f;
            return offset.sqrMagnitude <= interactRange * interactRange;
        }

        private void Update()
        {
            if (open || opening || prompt == null) return;

            PlayerStatus near = null;
            foreach (PlayerStatus player in PlayerRegistry.Players)
            {
                if (player != null && !player.IsDead && InRange(player.transform.position))
                {
                    near = player;
                    break;
                }
            }

            if (prompt.activeSelf != (near != null)) prompt.SetActive(near != null);
            if (near == null) return;

            int remaining = EnemiesBlocking(near.transform.position);
            if (remaining == shownRemaining) return;
            shownRemaining = remaining;
            promptText.text = remaining > 0
                ? UiFactory.Colored($"Derrote os inimigos da sala ({remaining})", UiFactory.Bad)
                : "E / Y ou atacar: abrir";
        }

        public void Open()
        {
            if (open || opening) return;
            opening = true;
            if (prompt != null) prompt.SetActive(false);
            if (blocker != null) blocker.enabled = false;
            if (obstacle != null) obstacle.enabled = false;
            GameAudio.Play(GameSound.DoorOpen);
            StartCoroutine(OpenRoutine());
        }

        private IEnumerator OpenRoutine()
        {
            Vector3 closedPosition = transform.position;
            float elapsed = 0f;
            while (elapsed < openSeconds)
            {
                elapsed += Time.deltaTime;
                float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / openSeconds), 3f);

                if (leaves.Count > 0)
                {
                    foreach ((Transform leaf, Quaternion closed, float sign) in leaves)
                    {
                        if (leaf != null) leaf.localRotation = Quaternion.AngleAxis(sign * openAngle * t, Vector3.up) * closed;
                    }
                }
                else
                {
                    transform.position = closedPosition + Vector3.down * (4.3f * t);
                }

                yield return null;
            }

            opening = false;
            open = true;
        }

        private void BuildPrompt()
        {
            prompt = new GameObject("DoorPrompt", typeof(RectTransform), typeof(Canvas), typeof(Billboard));
            prompt.transform.SetParent(transform.parent, false);
            prompt.transform.position = center + Vector3.up * 3.6f;
            prompt.transform.localScale = Vector3.one * 0.02f;
            prompt.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            prompt.GetComponent<RectTransform>().sizeDelta = new Vector2(420f, 70f);
            Image back = UiFactory.Box(prompt.transform, "Back", new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Stretch(back.rectTransform);
            promptText = UiFactory.Label(prompt.transform, "Text", "E / Y ou atacar: abrir", 34, UiFactory.Gold, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(promptText.rectTransform);
            prompt.SetActive(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            all.Clear();
        }
    }
}
