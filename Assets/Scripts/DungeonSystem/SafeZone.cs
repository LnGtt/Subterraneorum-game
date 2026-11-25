using System;
using System.Collections.Generic;
using UnityEngine;

namespace Subterraneorum.DungeonSystem
{
    [RequireComponent(typeof(Collider))]
    public class SafeZone : MonoBehaviour
    {
        [SerializeField] private float healPerSecond = 10f;

        private readonly HashSet<PlayerStatus> playersInside = new HashSet<PlayerStatus>();

        public IReadOnlyCollection<PlayerStatus> PlayersInside => playersInside;

        public event Action<SafeZone> OnPlayersInsideChanged;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerStatus player = other.GetComponentInParent<PlayerStatus>();
            if (player != null && playersInside.Add(player))
            {
                OnPlayersInsideChanged?.Invoke(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerStatus player = other.GetComponentInParent<PlayerStatus>();
            if (player != null && playersInside.Remove(player))
            {
                OnPlayersInsideChanged?.Invoke(this);
            }
        }

        private void Update()
        {
            if (healPerSecond <= 0f) return;

            foreach (PlayerStatus player in playersInside)
            {
                if (player != null && !player.IsDead)
                {
                    player.Heal(healPerSecond * Time.deltaTime);
                }
            }
        }
    }
}
