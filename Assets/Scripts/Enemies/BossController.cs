using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossController : EnemyBase
{
    public enum BossAttack
    {
        Shockwave,
        RadialProjectiles,
        ImpactUnderPlayers,
    }

    [Header("Chefão")]
    [SerializeField] private string displayName = "Guardião Abissal";
    [Tooltip("Intervalo aleatório (s) entre o fim de um ataque e o próximo.")]
    [SerializeField] private Vector2 attackInterval = new Vector2(1.8f, 3.2f);

    [Header("Onda de choque")]
    [SerializeField] private float shockwaveTelegraph = 1.1f;
    [Tooltip("Até onde o anel chega (m). Menor que a sala: fugir para longe também escapa.")]
    [SerializeField] private float shockwaveMaxRadius = 10f;
    [SerializeField] private float shockwaveDuration = 1.4f;
    [Tooltip("Meia-espessura do anel (m). Fino o bastante para o rolamento (~0,4 s invulnerável) atravessar com folga.")]
    [SerializeField] private float shockwaveWidth = 0.8f;
    [SerializeField] private float shockwaveDamage = 25f;

    [Header("Projéteis radiais")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float radialTelegraph = 0.9f;
    [SerializeField] private int projectilesPerWave = 16;
    [SerializeField] private int waves = 2;
    [SerializeField] private float projectileDamageScale = 1.5f;

    [Header("Impacto embaixo dos jogadores")]
    [SerializeField] private float impactTelegraph = 1.3f;
    [SerializeField] private float impactRadius = 2.3f;
    [SerializeField] private float impactDamage = 35f;

    private static readonly int AttackIndexHash = Animator.StringToHash("attackIndex");

    private EnemyStatus bossStatus;
    private Coroutine attackRoutine;

    public string DisplayName => displayName;
    public bool IsAttacking => attackRoutine != null;
    public int AttacksPerformed { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        bossStatus = GetComponent<EnemyStatus>();
    }

    protected override void Start()
    {
        base.Start();
        GameAudio.Play(GameSound.BossRoar);
        nextAttackTime = Time.time + 1.5f;
    }

    protected override bool IsTargetInAttackPosition(float distanceToPlayer)
    {
        return IsAttacking || distanceToPlayer <= attackRange;
    }

    protected override void ChaseTarget(float distanceToPlayer)
    {
        if (IsAttacking) return;
        base.ChaseTarget(distanceToPlayer);
    }

    protected override void PerformAttack()
    {
        if (IsAttacking) return;

        var attack = (BossAttack)Random.Range(0, 3);
        if (animator != null && animator.isActiveAndEnabled) animator.SetInteger(AttackIndexHash, (int)attack + 1);
        GameAudio.Play(GameSound.BossAttack);
        StartAttack(attack);
    }

    public void ForceAttack(int index)
    {
        if (IsAttacking || bossStatus == null || bossStatus.IsDead) return;
        StartAttack((BossAttack)Mathf.Clamp(index, 0, 2));
    }

    private void StartAttack(BossAttack attack)
    {
        AttacksPerformed++;
        switch (attack)
        {
            case BossAttack.Shockwave: attackRoutine = StartCoroutine(Shockwave()); break;
            case BossAttack.RadialProjectiles: attackRoutine = StartCoroutine(RadialProjectiles()); break;
            default: attackRoutine = StartCoroutine(ImpactUnderPlayers()); break;
        }

        nextAttackTime = float.MaxValue;
    }

    private bool Dead => bossStatus != null && bossStatus.IsDead;

    private void FinishAttack()
    {
        attackRoutine = null;
        nextAttackTime = Time.time + Random.Range(attackInterval.x, attackInterval.y);
    }

    private bool shockwaveHintShown;

    private IEnumerator Shockwave()
    {
        Vector3 origin = transform.position;
        GroundTelegraph.Spawn(origin, shockwaveMaxRadius, new Color(1f, 0.25f, 0.15f, 0.35f), shockwaveTelegraph);

        if (!shockwaveHintShown)
        {
            shockwaveHintShown = true;
            GameHud hud = FindAnyObjectByType<GameHud>();
            if (hud != null) hud.ShowBanner("Onda de choque!\n<size=34>Role (Espaço / A) através do anel ou fuja para longe do chefão</size>", 2.5f);
        }
        yield return new WaitForSeconds(shockwaveTelegraph);
        if (Dead) yield break;

        GroundTelegraph ring = GroundTelegraph.Spawn(origin, 1f, new Color(1f, 0.55f, 0.15f, 0.9f), shockwaveDuration + 0.05f, GroundTelegraph.Shape.Ring, growFill: false);
        GameFeel.Shake(0.5f);
        var hit = new HashSet<PlayerStatus>();
        float elapsed = 0f;
        while (elapsed < shockwaveDuration && !Dead)
        {
            float radius = Mathf.Lerp(1f, shockwaveMaxRadius, elapsed / shockwaveDuration);
            if (ring != null) ring.SetRadius(radius);

            foreach (PlayerStatus player in PlayerRegistry.Players)
            {
                if (player == null || player.IsDead || hit.Contains(player)) continue;
                Vector3 offset = player.transform.position - origin;
                offset.y = 0f;
                if (Mathf.Abs(offset.magnitude - radius) <= shockwaveWidth)
                {
                    hit.Add(player);
                    player.TakeDamage(shockwaveDamage * EffectiveDamageMultiplier);
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        FinishAttack();
    }

    private IEnumerator RadialProjectiles()
    {
        Vector3 origin = transform.position;
        GroundTelegraph.Spawn(origin, 3.5f, new Color(0.75f, 0.3f, 1f, 0.45f), radialTelegraph);
        yield return new WaitForSeconds(radialTelegraph);

        int count = Mathf.Max(4, projectilesPerWave);
        for (int wave = 0; wave < waves && !Dead; wave++)
        {
            float offset = wave * 180f / count;
            for (int i = 0; i < count; i++)
            {
                if (projectilePrefab == null) break;
                Vector3 dir = Quaternion.Euler(0f, i * 360f / count + offset, 0f) * Vector3.forward;
                if (i == 0) GameFeel.Shake(0.25f);
                GameObject projectile = Instantiate(projectilePrefab, origin + Vector3.up * 1.2f + dir * 1.8f, Quaternion.LookRotation(dir));
                EnemyProjectile enemyProjectile = projectile.GetComponent<EnemyProjectile>();
                if (enemyProjectile != null) enemyProjectile.DamageMultiplier = EffectiveDamageMultiplier * projectileDamageScale;
            }

            yield return new WaitForSeconds(0.5f);
        }

        FinishAttack();
    }

    private IEnumerator ImpactUnderPlayers()
    {
        var targets = new List<Vector3>();
        foreach (PlayerStatus player in PlayerRegistry.Players)
        {
            if (player == null || player.IsDead) continue;
            targets.Add(player.transform.position);
            GroundTelegraph.Spawn(player.transform.position, impactRadius, new Color(1f, 0.2f, 0.2f, 0.5f), impactTelegraph);
        }

        yield return new WaitForSeconds(impactTelegraph);
        if (!Dead)
        {
            GameFeel.Shake(0.6f);
            foreach (Vector3 center in targets)
            {
                GroundTelegraph.Spawn(center, impactRadius, new Color(1f, 0.6f, 0.2f, 0.9f), 0.35f, GroundTelegraph.Shape.Disc, growFill: false);
                DamagePlayersInArea(center, impactRadius, impactDamage);
            }
        }

        yield return new WaitForSeconds(0.3f);
        FinishAttack();
    }
}
