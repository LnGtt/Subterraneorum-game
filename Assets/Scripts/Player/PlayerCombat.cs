using System.Collections.Generic;
using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [SerializeField] private float attackDamage = 25f;
    [SerializeField] private float attackCooldown = 0.5f;
    [SerializeField] private Transform attackPoint;
    [SerializeField] private float attackRadius = 1.2f;
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerInputHandler inputHandler;

    [Header("Projétil (classes à distância)")]
    [SerializeField] private AttackStyle attackStyle = AttackStyle.Melee;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 18f;
    [SerializeField] private float projectileRange = 12f;
    [Tooltip("Mira assistida: vira para o inimigo mais próximo dentro deste ângulo à frente.")]
    [SerializeField] private float aimAssistAngle = 35f;

    [Header("Resposta do ataque")]
    [Tooltip("Apertar o ataque até este tempo (s) antes da recarga acabar guarda o golpe e ele sai assim que possível.")]
    [SerializeField] private float inputBufferTime = 0.2f;
    [Tooltip("Abertura (graus) do arco à frente do personagem em que o golpe corpo a corpo acerta.")]
    [SerializeField] private float meleeArcAngle = 140f;
    [Tooltip("Inimigos mais perto que isto (m) são atingidos em qualquer direção.")]
    [SerializeField] private float meleeCloseRange = 0.6f;

    private static readonly int AttackHash = Animator.StringToHash("attack");
    private readonly Collider[] hitBuffer = new Collider[32];
    private readonly HashSet<IDamageable> damagedTargets = new HashSet<IDamageable>();

    private float nextAttackTime;
    private float bufferedAttackUntil = -1f;
    private PlayerProgression progression;
    private PlayerStatus status;
    private PlayerMovement movement;
    private float lifeSteal;

    public float DamageMultiplier { get; set; } = 1f;

    public float SkillDamageMultiplier { get; set; } = 1f;

    public LayerMask EnemyLayer => enemyLayer;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (inputHandler == null) inputHandler = GetComponent<PlayerInputHandler>();
        progression = GetComponent<PlayerProgression>();
        status = GetComponent<PlayerStatus>();
        movement = GetComponent<PlayerMovement>();

        if (enemyLayer.value == 0)
        {
            enemyLayer = LayerMask.GetMask("Enemy");
        }
    }

    private void OnEnable()
    {
        if (inputHandler != null)
        {
            inputHandler.OnAttackInput += HandleAttackInput;
        }
    }

    private void OnDisable()
    {
        if (inputHandler != null)
        {
            inputHandler.OnAttackInput -= HandleAttackInput;
        }
    }

    public void Configure(ClassDefinition definition)
    {
        if (definition == null) return;
        attackStyle = definition.attackStyle;
        attackDamage = definition.attackDamage;
        attackCooldown = definition.attackCooldown;
        attackRadius = definition.attackRadius;
        projectileSpeed = definition.projectileSpeed;
        projectileRange = definition.projectileRange;
        if (definition.projectilePrefab != null) projectilePrefab = definition.projectilePrefab;
        lifeSteal = definition.lifeSteal;
    }

    private void Update()
    {
        if (bufferedAttackUntil < 0f) return;

        if (Time.time > bufferedAttackUntil)
        {
            bufferedAttackUntil = -1f;
        }
        else if (Time.time >= nextAttackTime)
        {
            bufferedAttackUntil = -1f;
            StartAttack();
        }
    }

    private void HandleAttackInput()
    {
        if (Time.time < nextAttackTime)
        {
            bufferedAttackUntil = Time.time + inputBufferTime;
            return;
        }

        StartAttack();
    }

    private void StartAttack()
    {
        nextAttackTime = Time.time + attackCooldown;
        PerformAttack();
    }

    private void PerformAttack()
    {
        PlayAttackAnimation();
        GameAudio.Play(GameSound.PlayerAttack);

        if (attackStyle == AttackStyle.Projectile && projectilePrefab != null)
        {
            Vector3 direction = AimDirection(projectileRange);
            FireProjectile(direction, attackDamage, projectilePrefab, projectileSpeed, projectileRange);
            return;
        }

        AimDirection(attackRadius + 2f);
        Vector3 origin = attackPoint != null ? attackPoint.position : transform.position + transform.forward;

        PlayerIdentity identity = GetComponent<PlayerIdentity>();
        Color tint = identity != null && identity.Class != null ? identity.Class.color : Color.white;
        GroundTelegraph.Spawn(new Vector3(origin.x, transform.position.y, origin.z), attackRadius, new Color(tint.r, tint.g, tint.b, 0.45f), 0.15f, GroundTelegraph.Shape.Disc, growFill: false);

        Vector3 flatOffset = origin - transform.position;
        flatOffset.y = 0f;
        float reach = flatOffset.magnitude + attackRadius;

        int hits = DamageEnemiesInArc(reach, attackDamage);
        if (hits > 0)
        {
            GameFeel.HitStop(0.045f);
            GameFeel.Shake(0.12f);
        }
    }

    public void PlayAttackAnimation()
    {
        if (animator != null && animator.isActiveAndEnabled)
        {
            animator.SetTrigger(AttackHash);
        }
    }

    public float ScaleDamage(float baseDamage) => baseDamage * DamageMultiplier * SkillDamageMultiplier;

    public void DealDamage(IDamageable target, float baseDamage, bool applyLifeSteal)
    {
        if (target == null) return;
        float damage = ScaleDamage(baseDamage);

        if (target is EnemyStatus enemy)
        {
            if (enemy.IsDead) return;
            enemy.TakeDamage(damage, progression);
            GameFeel.DamageNumber(enemy.transform.position + Vector3.up * 1.6f, damage, GameFeel.EnemyDamageColor);

            Vector3 away = enemy.transform.position - transform.position;
            away.y = 0f;
            if (!enemy.IsDead && away.sqrMagnitude > 0.0001f) enemy.Knockback(away.normalized, 0.45f);
        }
        else
        {
            target.TakeDamage(damage);
        }

        GameAudio.Play(GameSound.EnemyHit);
        if (applyLifeSteal && lifeSteal > 0f && status != null)
        {
            status.Heal(damage * lifeSteal);
        }
    }

    public int DamageEnemiesInSphere(Vector3 center, float radius, float baseDamage, bool applyLifeSteal = false)
    {
        int count = Physics.OverlapSphereNonAlloc(center, radius, hitBuffer, enemyLayer, QueryTriggerInteraction.Ignore);
        damagedTargets.Clear();

        for (int i = 0; i < count; i++)
        {
            IDamageable target = hitBuffer[i].GetComponentInParent<IDamageable>();
            if (target == null || !damagedTargets.Add(target)) continue;
            DealDamage(target, baseDamage, applyLifeSteal);
        }

        return damagedTargets.Count;
    }

    private int DamageEnemiesInArc(float reach, float baseDamage)
    {
        Vector3 center = transform.position;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        int count = Physics.OverlapSphereNonAlloc(center, reach, hitBuffer, enemyLayer, QueryTriggerInteraction.Ignore);
        damagedTargets.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider hit = hitBuffer[i];
            IDamageable target = hit.GetComponentInParent<IDamageable>();
            if (target == null || damagedTargets.Contains(target)) continue;

            Vector3 toTarget = hit.ClosestPoint(center) - center;
            toTarget.y = 0f;
            bool closeEnough = toTarget.sqrMagnitude <= meleeCloseRange * meleeCloseRange;
            if (!closeEnough && Vector3.Angle(forward, toTarget) > meleeArcAngle * 0.5f) continue;

            damagedTargets.Add(target);
            DealDamage(target, baseDamage, applyLifeSteal: true);
        }

        return damagedTargets.Count;
    }

    public PlayerProjectile FireProjectile(Vector3 direction, float baseDamage, GameObject prefab, float speed, float range)
    {
        if (prefab == null) return null;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;

        Vector3 origin = transform.position + Vector3.up * 0.85f + direction.normalized * 0.6f;
        GameObject go = Instantiate(prefab, origin, Quaternion.LookRotation(direction.normalized));
        PlayerProjectile projectile = go.GetComponent<PlayerProjectile>();
        if (projectile == null) projectile = go.AddComponent<PlayerProjectile>();
        projectile.Launch(this, direction.normalized, baseDamage, speed, range);
        return projectile;
    }

    public Vector3 AimDirection(float maxDistance)
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        int count = Physics.OverlapSphereNonAlloc(transform.position, maxDistance, hitBuffer, enemyLayer, QueryTriggerInteraction.Ignore);
        Vector3 best = forward;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            EnemyStatus enemy = hitBuffer[i].GetComponentInParent<EnemyStatus>();
            if (enemy == null || enemy.IsDead) continue;

            Vector3 to = enemy.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f || Vector3.Angle(forward, to) > aimAssistAngle) continue;
            if (to.sqrMagnitude < bestSqr)
            {
                bestSqr = to.sqrMagnitude;
                best = to.normalized;
            }
        }

        if (movement != null && best != forward) movement.FaceDirection(best);
        return best;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 origin = attackPoint != null ? attackPoint.position : transform.position + transform.forward;
        Gizmos.DrawWireSphere(origin, attackRadius);
    }
}
