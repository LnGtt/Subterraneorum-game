using UnityEngine;

public class EnemyRanged : EnemyBase
{
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float preferredDistance = 6f;
    [SerializeField] private float positionTolerance = 0.5f;

    [Header("Saraivada")]
    [SerializeField, Min(1)] private int projectilesPerVolley = 3;
    [SerializeField, Range(0f, 90f)] private float spreadAngle = 25f;

    protected override bool IsTargetInAttackPosition(float distanceToPlayer)
    {
        return distanceToPlayer >= preferredDistance - positionTolerance && distanceToPlayer <= preferredDistance + positionTolerance;
    }

    protected override void ChaseTarget(float distanceToPlayer)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || targetPlayer == null)
        {
            return;
        }

        agent.isStopped = false;
        if (distanceToPlayer < preferredDistance - positionTolerance)
        {
            Vector3 away = (transform.position - targetPlayer.position).normalized;
            Vector3 desired = transform.position + away * (preferredDistance - distanceToPlayer);
            if (UnityEngine.AI.NavMesh.SamplePosition(desired, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }

            return;
        }

        agent.SetDestination(targetPlayer.position);
    }

    protected override void PerformAttack()
    {
        if (projectilePrefab == null || targetPlayer == null)
        {
            return;
        }

        Vector3 origin = firePoint != null ? firePoint.position : transform.position + Vector3.up;
        Vector3 direction = targetPlayer.position - origin;
        direction.y = 0f;
        Quaternion baseRotation = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : transform.rotation;

        int count = Mathf.Max(1, projectilesPerVolley);
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float yaw = Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, t);
            GameObject projectile = Instantiate(projectilePrefab, origin, baseRotation * Quaternion.Euler(0f, yaw, 0f));

            EnemyProjectile enemyProjectile = projectile.GetComponent<EnemyProjectile>();
            if (enemyProjectile != null)
            {
                enemyProjectile.DamageMultiplier = EffectiveDamageMultiplier;
            }
        }
    }
}
