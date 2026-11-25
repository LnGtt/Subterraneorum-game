using System.Collections;
using UnityEngine;

public class EnemyMelee : EnemyBase
{
    [SerializeField] private float meleeDamage = 15f;
    [Tooltip("Alcance do cone. O game design pede ~3x o tamanho do inimigo.")]
    [SerializeField] private float attackRadius = 2.2f;
    [SerializeField, Range(10f, 180f)] private float coneAngle = 90f;
    [Tooltip("Tempo de aviso antes do golpe.")]
    [SerializeField] private float windup = 0.4f;

    private Coroutine attackRoutine;

    protected override void PerformAttack()
    {
        if (attackRoutine == null) attackRoutine = StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        GroundTelegraph.SpawnCone(transform.position, transform.forward, attackRadius, coneAngle, new Color(1f, 0.25f, 0.2f, 0.5f), windup, transform);
        yield return new WaitForSeconds(windup);

        EnemyStatus status = GetComponent<EnemyStatus>();
        if (status == null || !status.IsDead)
        {
            DamagePlayersInArea(transform.position, attackRadius, meleeDamage, coneAngle);
        }

        attackRoutine = null;
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        Gizmos.color = Color.red;
        Vector3 left = Quaternion.Euler(0f, -coneAngle * 0.5f, 0f) * transform.forward * attackRadius;
        Vector3 right = Quaternion.Euler(0f, coneAngle * 0.5f, 0f) * transform.forward * attackRadius;
        Gizmos.DrawLine(transform.position, transform.position + left);
        Gizmos.DrawLine(transform.position, transform.position + right);
        Gizmos.DrawLine(transform.position + left, transform.position + transform.forward * attackRadius);
        Gizmos.DrawLine(transform.position + right, transform.position + transform.forward * attackRadius);
    }
}
