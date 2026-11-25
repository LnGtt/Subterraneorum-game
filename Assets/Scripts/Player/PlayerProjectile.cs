using UnityEngine;

public class PlayerProjectile : MonoBehaviour
{
    [SerializeField] private float hitRadius = 0.5f;
    [Tooltip("Quantos inimigos atravessa antes de sumir (0 = para no primeiro).")]
    [SerializeField] private int pierce;

    private PlayerCombat owner;
    private Vector3 direction;
    private float damage;
    private float speed;
    private float remaining;
    private int hitMask;
    private int environmentLayer;
    private int piercesLeft;
    private readonly System.Collections.Generic.HashSet<EnemyStatus> alreadyHit = new System.Collections.Generic.HashSet<EnemyStatus>();

    public void Launch(PlayerCombat shooter, Vector3 dir, float baseDamage, float projectileSpeed, float range)
    {
        owner = shooter;
        direction = dir.normalized;
        damage = baseDamage;
        speed = Mathf.Max(1f, projectileSpeed);
        remaining = Mathf.Max(0.5f, range);
        piercesLeft = pierce;
        environmentLayer = LayerMask.NameToLayer("Environment");
        int enemies = shooter != null ? shooter.EnemyLayer.value : LayerMask.GetMask("Enemy");
        hitMask = enemies | (environmentLayer >= 0 ? 1 << environmentLayer : 0);
        transform.rotation = Quaternion.LookRotation(direction);
    }

    private void Update()
    {
        if (owner == null && damage <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        float step = speed * Time.deltaTime;
        if (Physics.SphereCast(transform.position, hitRadius, direction, out RaycastHit hit, step, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.gameObject.layer == environmentLayer)
            {
                Destroy(gameObject);
                return;
            }

            EnemyStatus enemy = hit.collider.GetComponentInParent<EnemyStatus>();
            if (enemy != null && !enemy.IsDead && alreadyHit.Add(enemy))
            {
                if (owner != null) owner.DealDamage(enemy, damage, applyLifeSteal: true);
                else enemy.TakeDamage(damage);

                if (piercesLeft-- <= 0)
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }

        transform.position += direction * step;
        remaining -= step;
        if (remaining <= 0f) Destroy(gameObject);
    }
}
