using System;
using UnityEngine;
using UnityEngine.AI;

public class EnemyStatus : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 60f;
    [SerializeField] private float xpReward = 25f;
    [SerializeField] private Animator animator;
    [SerializeField] private HitFeedback hitFeedback;
    [SerializeField] private GameObject hpBarFill;
    [SerializeField] private Collider mainCollider;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private float destroyDelay = 2.5f;

    private static readonly int TakeDamageHash = Animator.StringToHash("takeDamage");
    private static readonly int DieHash = Animator.StringToHash("die");

    private float currentHealth;
    private bool isDead;
    private PlayerProgression lastAttacker;

    public bool IsDead => isDead;
    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;

    public event Action<float, float> OnHealthChanged;
    public event Action<EnemyStatus> OnEnemyDied;
    public event Action OnDamaged;
    public event Action OnDeath;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (hitFeedback == null) hitFeedback = GetComponent<HitFeedback>();
        if (mainCollider == null) mainCollider = GetComponent<Collider>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        currentHealth = maxHealth;
    }

    private static readonly System.Collections.Generic.List<EnemyStatus> all = new System.Collections.Generic.List<EnemyStatus>();

    public static System.Collections.Generic.IReadOnlyList<EnemyStatus> All => all;

    public bool IsBoss => isBoss ??= GetComponent<BossController>() != null;

    public int HomeRoomId { get; set; } = -1;

    private void OnEnable()
    {
        all.Add(this);
        EnemyModifiers.OnHealthScaled += ApplyHealthRatio;
    }

    private void OnDisable()
    {
        all.Remove(this);
        EnemyModifiers.OnHealthScaled -= ApplyHealthRatio;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => all.Clear();

    private void Start()
    {
        if (!Mathf.Approximately(EnemyModifiers.Health, 1f))
        {
            maxHealth *= EnemyModifiers.Health;
            currentHealth = maxHealth;
        }

        NotifyHealthChanged();
    }

    private void ApplyHealthRatio(float ratio)
    {
        if (isDead || ratio <= 0f) return;
        maxHealth *= ratio;
        currentHealth *= ratio;
        NotifyHealthChanged();
    }

    public void ScaleHealth(float multiplier)
    {
        maxHealth *= Mathf.Max(0.1f, multiplier);
        currentHealth = maxHealth;
        NotifyHealthChanged();
    }

    public void TakeDamage(float amount)
    {
        TakeDamage(amount, null);
    }

    public void TakeDamage(float amount, PlayerProgression attacker)
    {
        if (amount <= 0f || isDead)
        {
            return;
        }

        if (attacker != null)
        {
            lastAttacker = attacker;
        }

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        NotifyHealthChanged();
        OnDamaged?.Invoke();

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        if (animator != null) animator.SetTrigger(TakeDamageHash);
        if (hitFeedback != null) hitFeedback.TriggerFlash();
    }

    public void Knockback(Vector3 direction, float distance)
    {
        if (isDead || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        if (IsBoss) return;
        agent.Move(direction * distance);
    }

    private bool? isBoss;

    private void NotifyHealthChanged()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (hpBarFill != null)
        {
            Vector3 scale = hpBarFill.transform.localScale;
            float healthPercent = maxHealth > 0f ? currentHealth / maxHealth : 0f;
            hpBarFill.transform.localScale = new Vector3(Mathf.Clamp01(healthPercent), scale.y, scale.z);
        }
    }

    private void Die()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;
        if (animator != null) animator.SetTrigger(DieHash);
        if (agent != null) agent.enabled = false;
        if (mainCollider != null) mainCollider.enabled = false;
        if (hpBarFill != null && hpBarFill.transform.parent != null) hpBarFill.transform.parent.gameObject.SetActive(false);

        AwardExperience();
        GameAudio.Play(GameSound.EnemyDeath);
        OnEnemyDied?.Invoke(this);
        OnDeath?.Invoke();
        Destroy(gameObject, destroyDelay);
    }

    private void AwardExperience()
    {
        PlayerProgression receiver = lastAttacker;
        if (receiver == null)
        {
            PlayerStatus closest = PlayerRegistry.GetClosestAlive(transform.position);
            receiver = closest != null ? closest.GetComponent<PlayerProgression>() : null;
        }

        if (receiver != null)
        {
            receiver.AddXP(xpReward);
        }
    }
}
