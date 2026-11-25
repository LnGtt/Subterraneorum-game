using System;
using UnityEngine;

public enum RunEffect
{
    None,
    PlayerDamage,
    PlayerDamageTaken,
    PlayerSpeed,
    PlayerXPGain,
    PlayerMaxHealth,
    EnemyDamage,
    EnemyHealth,
    EnemySpeed,
}

public static class TeamModifiers
{
    public static float Damage { get; private set; } = 1f;
    public static float DamageTaken { get; private set; } = 1f;
    public static float Speed { get; private set; } = 1f;
    public static float XPGain { get; private set; } = 1f;
    public static float MaxHealthBonus { get; private set; }

    public static event Action OnChanged;

    public static void Apply(RunEffect effect, float value)
    {
        switch (effect)
        {
            case RunEffect.PlayerDamage: Damage = Mathf.Max(0.1f, Damage + value); break;
            case RunEffect.PlayerDamageTaken: DamageTaken = Mathf.Max(0.1f, DamageTaken + value); break;
            case RunEffect.PlayerSpeed: Speed = Mathf.Max(0.3f, Speed + value); break;
            case RunEffect.PlayerXPGain: XPGain = Mathf.Max(0.1f, XPGain + value); break;
            case RunEffect.PlayerMaxHealth: MaxHealthBonus += value; break;
            default: return;
        }

        OnChanged?.Invoke();
    }

    public static void ResetAll()
    {
        Damage = DamageTaken = Speed = XPGain = 1f;
        MaxHealthBonus = 0f;
        OnChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnChanged = null;
        Damage = DamageTaken = Speed = XPGain = 1f;
        MaxHealthBonus = 0f;
    }
}

public static class EnemyModifiers
{
    public static float Damage { get; private set; } = 1f;
    public static float Health { get; private set; } = 1f;
    public static float Speed { get; private set; } = 1f;

    public static event Action<float> OnHealthScaled;
    public static event Action OnChanged;

    public static void Apply(RunEffect effect, float value)
    {
        switch (effect)
        {
            case RunEffect.EnemyDamage: Damage = Mathf.Max(0.1f, Damage + value); break;
            case RunEffect.EnemySpeed: Speed = Mathf.Max(0.3f, Speed + value); break;
            case RunEffect.EnemyHealth:
                float previous = Health;
                Health = Mathf.Max(0.1f, Health + value);
                OnHealthScaled?.Invoke(Health / previous);
                break;
            default: return;
        }

        OnChanged?.Invoke();
    }

    public static void ResetAll()
    {
        Damage = Health = Speed = 1f;
        OnChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnChanged = null;
        OnHealthScaled = null;
        Damage = Health = Speed = 1f;
    }
}

public static class RunEffects
{
    public static bool IsEnemyEffect(RunEffect effect) =>
        effect == RunEffect.EnemyDamage || effect == RunEffect.EnemyHealth || effect == RunEffect.EnemySpeed;

    public static void Apply(RunEffect effect, float value)
    {
        if (IsEnemyEffect(effect)) EnemyModifiers.Apply(effect, value);
        else TeamModifiers.Apply(effect, value);
    }
}
