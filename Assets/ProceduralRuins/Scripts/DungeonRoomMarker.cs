using System;
using UnityEngine;

namespace ProceduralRuins
{
    public enum DungeonRoomType
    {
        Start,
        Normal,
        Treasure,
        Exit,
        Boss,
        SafeZone,
    }

    public class DungeonRoomMarker : MonoBehaviour
    {
        public DungeonRoomType roomType = DungeonRoomType.Normal;

        [Tooltip("Limites da sala em células do grid.")]
        public RectInt bounds;

        public int roomId;

        public bool Visited { get; private set; }

        public static event Action<DungeonRoomMarker, GameObject> OnFirstPlayerEntered;

        private void OnTriggerEnter(Collider other)
        {
            if (Visited) return;

            PlayerStatus player = other.GetComponentInParent<PlayerStatus>();
            if (player == null || player.IsDead) return;

            Visited = true;
            OnFirstPlayerEntered?.Invoke(this, player.gameObject);
        }
    }
}
