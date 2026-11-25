using UnityEngine;

public enum AttackStyle
{
    Melee,
    Projectile,
}

[CreateAssetMenu(menuName = "Subterraneorum/Classe", fileName = "ClassDefinition")]
public class ClassDefinition : ScriptableObject
{
    [Tooltip("Identificador estável (Warrior, Ranger, Wizard, Rogue).")]
    public string classId = "Warrior";

    [Tooltip("Nome do personagem mostrado na seleção (ex.: Nanus Ferox).")]
    public string displayName = "Nanus Ferox";

    [Tooltip("Papel da classe (ex.: Guerreiro).")]
    public string roleName = "Guerreiro";

    [TextArea] public string description;

    [Tooltip("Cor do jogador na interface.")]
    public Color color = Color.white;

    [Tooltip("Prefab variant do personagem (Player_Warrior etc.).")]
    public GameObject prefab;

    [Header("Atributos")]
    public float maxHealth = 100f;
    public float moveSpeed = 6f;
    public float dashCooldown = 0.8f;

    [Header("Ataque básico")]
    public AttackStyle attackStyle = AttackStyle.Melee;
    public float attackDamage = 25f;
    public float attackCooldown = 0.5f;
    [Tooltip("Corpo a corpo: raio do golpe à frente.")]
    public float attackRadius = 1.2f;
    [Tooltip("Projétil: velocidade e alcance.")]
    public float projectileSpeed = 18f;
    public float projectileRange = 12f;
    public GameObject projectilePrefab;
    [Tooltip("Fração do dano causado que volta como vida (Assassino).")]
    [Range(0f, 1f)] public float lifeSteal;

    [Header("Habilidades")]
    [Tooltip("Disponível desde o início (K / botão direito / B / RT).")]
    public AbilityDefinition ability1;
    [Tooltip("Destravada com 1 ponto de habilidade (L / Q / LB / LT).")]
    public AbilityDefinition ability2;
}
