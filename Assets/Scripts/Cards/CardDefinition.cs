using UnityEngine;

[CreateAssetMenu(menuName = "Subterraneorum/Carta", fileName = "Card")]
public class CardDefinition : ScriptableObject
{
    public string title = "Carta";

    [Header("Se escolhida")]
    public RunEffect effect = RunEffect.PlayerDamage;
    [Tooltip("Frações somam ao multiplicador (0.15 = +15%). Vida máxima é em pontos.")]
    public float value = 0.15f;
    public string effectText = "Jogadores: +15% de dano";

    [Header("Se NÃO escolhida (buff dos inimigos)")]
    public RunEffect leftoverEffect = RunEffect.EnemyDamage;
    public float leftoverValue = 0.1f;
    public string leftoverText = "Inimigos: +10% de dano";

    [Header("Visual")]
    [Tooltip("Carta do atlas: x = coluna (0..8 = 2..10, 9 J, 10 Q, 11 K, 12 A, 13 Coringa), y = naipe (0 espadas, 1 copas, 2 paus, 3 ouros).")]
    public Vector2Int atlasCell = new Vector2Int(12, 0);
}
