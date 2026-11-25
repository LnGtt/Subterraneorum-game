using UnityEngine;

public enum AbilityKind
{
    ShieldBlock,
    HeavySlam,
    ArrowVolley,
    AgilityBurst,
    ArcaneBlast,
    HealingAura,
    BladeFlurry,
    ShadowStep,
}

[CreateAssetMenu(menuName = "Subterraneorum/Habilidade", fileName = "Ability")]
public class AbilityDefinition : ScriptableObject
{
    public AbilityKind kind;
    public string displayName = "Habilidade";
    [TextArea] public string description;
    [Min(0.1f)] public float cooldown = 6f;

    [Tooltip("Dano por acerto (golpe, flecha, explosão).")]
    public float damage = 30f;
    [Tooltip("Raio da área.")]
    public float radius = 2.5f;
    [Tooltip("Distância (à frente do personagem, do avanço ou das flechas).")]
    public float range = 4f;
    [Tooltip("Duração do efeito (escudo, aceleração).")]
    public float duration = 2f;
    [Tooltip("Quantidade (flechas, golpes).")]
    public int count = 3;
    [Tooltip("Valor do efeito: fração de redução de dano, multiplicador de velocidade ou vida curada.")]
    public float amount = 0.5f;
}
