using UnityEngine;

public class PlayerBuffs : MonoBehaviour
{
    private PlayerCombat combat;
    private PlayerStatus status;
    private PlayerMovement movement;
    private PlayerProgression progression;
    private float appliedHealthBonus;

    private void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        status = GetComponent<PlayerStatus>();
        movement = GetComponent<PlayerMovement>();
        progression = GetComponent<PlayerProgression>();
    }

    private void OnEnable()
    {
        TeamModifiers.OnChanged += Apply;
    }

    private void OnDisable()
    {
        TeamModifiers.OnChanged -= Apply;
    }

    private void Start()
    {
        Apply();
    }

    private void Apply()
    {
        if (combat != null) combat.DamageMultiplier = TeamModifiers.Damage;
        if (status != null) status.DamageTakenMultiplier = TeamModifiers.DamageTaken;
        if (movement != null) movement.SpeedMultiplier = TeamModifiers.Speed;
        if (progression != null) progression.XPGainMultiplier = TeamModifiers.XPGain;

        float delta = TeamModifiers.MaxHealthBonus - appliedHealthBonus;
        if (status != null && Mathf.Abs(delta) > 0.01f)
        {
            status.SetMaxHealth(status.MaxHealth + delta, healDifference: true);
            appliedHealthBonus = TeamModifiers.MaxHealthBonus;
        }
    }
}
