using UnityEngine;
using UnityEngine.AI;

public abstract class EnemyBase : MonoBehaviour
{
    [SerializeField] protected float detectionRadius = 10f;
    [SerializeField] protected float attackRange = 2f;
    [SerializeField] protected float attackCooldown = 1.5f;
    [SerializeField] protected NavMeshAgent agent;
    [SerializeField] protected Animator animator;
    [SerializeField] protected LayerMask playerLayer;
    [Tooltip("Depois de perceber um jogador, continua perseguindo até esta distância.")]
    [SerializeField] protected float loseTargetRadius = 18f;
    [Tooltip("Intervalo (s) entre trocas de alvo, para não ficar alternando a cada frame.")]
    [SerializeField] protected float retargetInterval = 0.5f;

    private static readonly int AttackHash = Animator.StringToHash("attack");
    private static readonly int IsMovingHash = Animator.StringToHash("isMoving");

    protected Transform targetPlayer;
    protected PlayerStatus targetStatus;
    protected float nextAttackTime;

    private EnemyState currentState;
    private bool isDead;
    private bool isAggro;
    private float nextRetargetTime;
    private EnemyStatus status;

    public float DamageMultiplier { get; set; } = 1f;

    public float EffectiveDamageMultiplier => DamageMultiplier * EnemyModifiers.Damage;

    private float baseAgentSpeed = -1f;

    public EnemyState CurrentState => currentState;

    protected virtual void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        status = GetComponent<EnemyStatus>();
        if (status != null)
        {
            status.OnEnemyDied += HandleEnemyDied;
            status.OnDamaged += HandleDamaged;
        }
    }

    protected virtual void Start()
    {
        SetState(EnemyState.Idle);
    }

    protected virtual void OnDestroy()
    {
        if (status != null)
        {
            status.OnEnemyDied -= HandleEnemyDied;
            status.OnDamaged -= HandleDamaged;
        }
    }

    protected virtual void Update()
    {
        if (isDead)
        {
            return;
        }

        if (agent != null)
        {
            if (baseAgentSpeed < 0f) baseAgentSpeed = agent.speed;
            agent.speed = baseAgentSpeed * EnemyModifiers.Speed;
        }

        UpdateTarget();

        if (targetPlayer == null)
        {
            SetState(EnemyState.Idle);
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.position);
        if (IsTargetInAttackPosition(distanceToPlayer))
        {
            SetState(EnemyState.Attack);
            return;
        }

        SetState(EnemyState.Chase);
    }

    private void UpdateTarget()
    {
        bool targetInvalid = targetStatus == null || targetStatus.IsDead;
        if (!targetInvalid && Time.time < nextRetargetTime)
        {
            float sqr = (targetStatus.transform.position - transform.position).sqrMagnitude;
            if (sqr <= loseTargetRadius * loseTargetRadius)
            {
                return;
            }
        }

        nextRetargetTime = Time.time + retargetInterval;
        float radius = isAggro ? loseTargetRadius : detectionRadius;
        PlayerStatus closest = PlayerRegistry.GetClosestAlive(transform.position, radius);

        targetStatus = closest;
        targetPlayer = closest != null ? closest.transform : null;
        isAggro = closest != null;
    }

    public void EnterDie()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;
        SetState(EnemyState.Die);
    }

    protected virtual bool IsTargetInAttackPosition(float distanceToPlayer)
    {
        return distanceToPlayer <= attackRange;
    }

    protected virtual void ChaseTarget(float distanceToPlayer)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(targetPlayer.position);
    }

    protected virtual void PerformAttack()
    {
    }

    protected int DamagePlayersInArea(Vector3 center, float radius, float damage, float coneAngle = 360f)
    {
        int hits = 0;
        foreach (PlayerStatus player in PlayerRegistry.Players)
        {
            if (player == null || player.IsDead) continue;

            Vector3 toPlayer = player.transform.position - center;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > radius * radius) continue;

            if (coneAngle < 360f && toPlayer.sqrMagnitude > 0.0001f)
            {
                float angle = Vector3.Angle(transform.forward, toPlayer);
                if (angle > coneAngle * 0.5f) continue;
            }

            player.TakeDamage(damage * EffectiveDamageMultiplier);
            hits++;
        }

        return hits;
    }

    private void SetState(EnemyState nextState)
    {
        currentState = nextState;
        ExecuteState(currentState);
    }

    private void ExecuteState(EnemyState state)
    {
        switch (state)
        {
            case EnemyState.Idle:
                StopMovement();
                break;
            case EnemyState.Chase:
                if (targetPlayer != null)
                {
                    ChaseTarget(Vector3.Distance(transform.position, targetPlayer.position));
                }

                SetMovingAnimation(true);
                break;
            case EnemyState.Attack:
                StopMovement();
                FaceTarget();
                TryAttack();
                break;
            case EnemyState.Die:
                StopMovement();
                break;
        }
    }

    private void TryAttack()
    {
        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + attackCooldown;
        if (animator != null)
        {
            animator.SetTrigger(AttackHash);
        }

        PerformAttack();
    }

    private void StopMovement()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        SetMovingAnimation(false);
    }

    private void FaceTarget()
    {
        if (targetPlayer == null)
        {
            return;
        }

        Vector3 direction = targetPlayer.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10f);
        }
    }

    private void SetMovingAnimation(bool isMoving)
    {
        if (animator != null)
        {
            animator.SetBool(IsMovingHash, isMoving);
        }
    }

    private void HandleDamaged()
    {
        isAggro = true;
        nextRetargetTime = 0f;
    }

    private void HandleEnemyDied(EnemyStatus enemyStatus)
    {
        EnterDie();
    }

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
