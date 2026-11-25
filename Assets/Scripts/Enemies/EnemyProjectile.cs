using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EnemyProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 10f;
    [SerializeField] private float damage = 10f;
    [SerializeField] private float lifeTime = 5f;
    [SerializeField] private LayerMask collisionLayers;
    [Tooltip("Raio do dano em área no impacto. 0 = só acerta quem foi tocado.")]
    [SerializeField] private float impactRadius = 1f;

    private Rigidbody projectileRigidbody;
    private bool hasExploded;

    public float DamageMultiplier { get; set; } = 1f;

    private void Awake()
    {
        projectileRigidbody = GetComponent<Rigidbody>();
        projectileRigidbody.useGravity = false;
    }

    private void Start()
    {
        projectileRigidbody.linearVelocity = transform.forward * speed;
        Destroy(gameObject, lifeTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasExploded)
        {
            return;
        }

        if (collisionLayers.value != 0 && (collisionLayers.value & (1 << other.gameObject.layer)) == 0)
        {
            return;
        }

        bool hitPlayer = other.GetComponentInParent<PlayerStatus>() != null;
        bool hitWorld = other.gameObject.layer == LayerMask.NameToLayer("Environment");
        if (!hitPlayer && !hitWorld)
        {
            return;
        }

        hasExploded = true;
        float finalDamage = damage * DamageMultiplier;

        if (impactRadius > 0f)
        {
            foreach (PlayerStatus player in PlayerRegistry.Players)
            {
                if (player == null || player.IsDead) continue;
                Vector3 offset = player.transform.position - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= impactRadius * impactRadius)
                {
                    player.TakeDamage(finalDamage);
                }
            }
        }
        else if (hitPlayer)
        {
            other.GetComponentInParent<PlayerStatus>().TakeDamage(finalDamage);
        }

        Destroy(gameObject);
    }
}
