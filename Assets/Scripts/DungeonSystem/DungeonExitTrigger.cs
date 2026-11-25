using System;
using System.Collections.Generic;
using UnityEngine;

namespace Subterraneorum.DungeonSystem
{
    [RequireComponent(typeof(Collider))]
    public class DungeonExitTrigger : MonoBehaviour
    {
        [SerializeField] private Collider triggerCollider;
        [SerializeField] private GameObject visualIndicator;

        private readonly HashSet<PlayerStatus> playersInside = new HashSet<PlayerStatus>();
        private bool hasTriggered;

        public event Action OnAllPlayersReachedExit;

        public int PlayersInside => CountAliveInside();

        private void Awake()
        {
            if (triggerCollider == null) triggerCollider = GetComponent<Collider>();
            if (triggerCollider != null) triggerCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerStatus player = other.GetComponentInParent<PlayerStatus>();
            if (player == null) return;

            playersInside.Add(player);
            CheckAllInside();
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerStatus player = other.GetComponentInParent<PlayerStatus>();
            if (player != null) playersInside.Remove(player);
        }

        private void Update()
        {
            if (!hasTriggered && playersInside.Count > 0) CheckAllInside();
        }

        private void CheckAllInside()
        {
            if (hasTriggered) return;

            int alive = PlayerRegistry.AliveCount;
            if (alive == 0 || CountAliveInside() < alive) return;

            hasTriggered = true;
            if (triggerCollider != null) triggerCollider.enabled = false;
            if (visualIndicator != null) visualIndicator.SetActive(false);
            OnAllPlayersReachedExit?.Invoke();
        }

        private int CountAliveInside()
        {
            int count = 0;
            foreach (PlayerStatus p in playersInside)
            {
                if (p != null && !p.IsDead) count++;
            }

            return count;
        }
    }
}
