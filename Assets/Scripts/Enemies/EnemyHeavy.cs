using System.Collections;
using UnityEngine;

public class EnemyHeavy : EnemyBase
{
    [SerializeField] private float heavyDamage = 35f;
    [SerializeField] private float slamRadius = 2.5f;
    [SerializeField] private float attackWindup = 0.6f;

    private Coroutine attackRoutine;

    protected override void PerformAttack()
    {
        if (attackRoutine == null)
        {
            attackRoutine = StartCoroutine(PerformHeavyAttack());
        }
    }

    private IEnumerator PerformHeavyAttack()
    {
        GroundTelegraph.Spawn(transform.position, slamRadius, new Color(1f, 0.3f, 0.15f, 0.45f), attackWindup, GroundTelegraph.Shape.Disc, true, transform);
        yield return new WaitForSeconds(attackWindup);

        EnemyStatus status = GetComponent<EnemyStatus>();
        if (status == null || !status.IsDead)
        {
            int hits = DamagePlayersInArea(transform.position, slamRadius, heavyDamage);
            if (hits > 0) GameFeel.Shake(0.3f);
        }

        attackRoutine = null;
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, slamRadius);
    }
}
