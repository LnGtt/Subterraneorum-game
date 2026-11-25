using System;
using UnityEngine;

public class PlayerSkills : MonoBehaviour
{
    public enum Node
    {
        UnlockAbility2,
        Vigor,
        Fury,
        Mastery,
    }

    public static readonly Node[] Nodes = { Node.UnlockAbility2, Node.Vigor, Node.Fury, Node.Mastery };

    private PlayerProgression progression;
    private PlayerAbilities abilities;
    private PlayerStatus status;
    private PlayerCombat combat;
    private PlayerIdentity identity;
    private readonly bool[] owned = new bool[4];

    public event Action OnChanged;

    public int Points => progression != null ? progression.SkillPoints : 0;

    private void Awake()
    {
        progression = GetComponent<PlayerProgression>();
        abilities = GetComponent<PlayerAbilities>();
        status = GetComponent<PlayerStatus>();
        combat = GetComponent<PlayerCombat>();
        identity = GetComponent<PlayerIdentity>();
    }

    public bool Owns(Node node) => owned[(int)node];

    public bool Requirement(Node node) => node != Node.Mastery || Owns(Node.UnlockAbility2);

    public bool CanBuy(Node node) => !Owns(node) && Requirement(node) && Points > 0;

    public string Title(Node node)
    {
        switch (node)
        {
            case Node.UnlockAbility2:
                AbilityDefinition a2 = identity != null && identity.Class != null ? identity.Class.ability2 : null;
                return a2 != null ? $"Destravar: {a2.displayName}" : "Destravar habilidade 2";
            case Node.Vigor: return "Vigor";
            case Node.Fury: return "Fúria";
            default: return "Maestria";
        }
    }

    public string Description(Node node)
    {
        switch (node)
        {
            case Node.UnlockAbility2:
                AbilityDefinition a2 = identity != null && identity.Class != null ? identity.Class.ability2 : null;
                return a2 != null ? a2.description : "";
            case Node.Vigor: return "+25% de vida máxima";
            case Node.Fury: return "+20% de dano";
            default: return "Recargas −30% e habilidades +25% (precisa da habilidade 2)";
        }
    }

    public bool TryBuy(Node node)
    {
        if (!CanBuy(node) || !progression.TrySpendSkillPoint()) return false;

        owned[(int)node] = true;
        switch (node)
        {
            case Node.UnlockAbility2:
                abilities.UnlockAbility2();
                break;
            case Node.Vigor:
                status.SetMaxHealth(status.MaxHealth * 1.25f, healDifference: true);
                break;
            case Node.Fury:
                combat.SkillDamageMultiplier *= 1.2f;
                break;
            case Node.Mastery:
                abilities.CooldownMultiplier *= 0.7f;
                abilities.PowerMultiplier *= 1.25f;
                break;
        }

        GameAudio.Play(GameSound.UiConfirm);
        OnChanged?.Invoke();
        return true;
    }
}
