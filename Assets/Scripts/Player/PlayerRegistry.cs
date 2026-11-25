using System;
using System.Collections.Generic;
using UnityEngine;

public static class PlayerRegistry
{
    private static readonly List<PlayerStatus> players = new List<PlayerStatus>();

    public static IReadOnlyList<PlayerStatus> Players => players;

    public static event Action<PlayerStatus> OnPlayerRegistered;

    public static event Action<PlayerStatus> OnPlayerUnregistered;

    public static void Register(PlayerStatus player)
    {
        if (player != null && !players.Contains(player))
        {
            players.Add(player);
            OnPlayerRegistered?.Invoke(player);
        }
    }

    public static void Unregister(PlayerStatus player)
    {
        if (players.Remove(player))
        {
            OnPlayerUnregistered?.Invoke(player);
        }
    }

    public static int AliveCount
    {
        get
        {
            int count = 0;
            foreach (PlayerStatus p in players)
            {
                if (p != null && !p.IsDead) count++;
            }

            return count;
        }
    }

    public static PlayerStatus GetClosestAlive(Vector3 position, float maxDistance = float.PositiveInfinity)
    {
        PlayerStatus best = null;
        float bestSqr = maxDistance * maxDistance;

        foreach (PlayerStatus p in players)
        {
            if (p == null || p.IsDead) continue;
            float sqr = (p.transform.position - position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = p;
            }
        }

        return best;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        players.Clear();
        OnPlayerRegistered = null;
        OnPlayerUnregistered = null;
    }
}
