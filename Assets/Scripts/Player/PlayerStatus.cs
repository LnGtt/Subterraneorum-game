using System;
using UnityEngine;

public class PlayerStatus : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private Animator animator;
    [SerializeField] private HitFeedback hitFeedback;
    [SerializeField] private GameObject hpBarFill;
    [Tooltip("Tempo de invencibilidade após levar dano, para evitar que vários acertos no mesmo frame zerem a vida.")]
    [SerializeField] private float invulnerabilityAfterHit = 0.25f;

    private static readonly int TakeDamageHash = Animator.StringToHash("takeDamage");
    private static readonly int DieHash = Animator.StringToHash("die");

    private float currentHealth;
    private bool isDead;
    private float invulnerableUntil;
    private PlayerMovement movement;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;

    public float DamageTakenMultiplier { get; set; } = 1f;

    public float BlockMultiplier { get; set; } = 1f;

    public event Action<float> OnDamaged;
    public event Action<float, float> OnHealthChanged;
    public event Action OnPlayerDied;
    public event Action OnPlayerRevived;
    public event Action OnDeath;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (hitFeedback == null) hitFeedback = GetComponent<HitFeedback>();
        movement = GetComponent<PlayerMovement>();
        currentHealth = maxHealth;
    }

    private void OnEnable()
    {
        PlayerRegistry.Register(this);
    }

    private void OnDisable()
    {
        PlayerRegistry.Unregister(this);
    }

    private void Start()
    {
        NotifyHealthChanged();
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || isDead || Time.time < invulnerableUntil)
        {
            return;
        }

        if (movement != null && movement.IsInvulnerable)
        {
            return;
        }

        invulnerableUntil = Time.time + invulnerabilityAfterHit;
        float finalDamage = amount * DamageTakenMultiplier * BlockMultiplier;
        currentHealth = Mathf.Max(currentHealth - finalDamage, 0f);
        NotifyHealthChanged();
        OnDamaged?.Invoke(finalDamage);
        GameAudio.Play(GameSound.PlayerHurt);
        GameFeel.DamageNumber(transform.position + Vector3.up * 2.2f, finalDamage, GameFeel.PlayerDamageColor);
        GameFeel.Shake(Mathf.Clamp(finalDamage / 60f, 0.12f, 0.45f));

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        if (animator != null) animator.SetTrigger(TakeDamageHash);
        if (hitFeedback != null) hitFeedback.TriggerFlash();
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || isDead)
        {
            return;
        }

        float before = currentHealth;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        NotifyHealthChanged();

        if (currentHealth - before >= 5f) GameFeel.DamageNumber(transform.position + Vector3.up * 2.2f, currentHealth - before, GameFeel.HealColor);
    }

    public void InitializeMaxHealth(float value)
    {
        maxHealth = Mathf.Max(1f, value);
        currentHealth = maxHealth;
        NotifyHealthChanged();
    }

    public void SetMaxHealth(float newMax, bool healDifference)
    {
        float difference = newMax - maxHealth;
        maxHealth = Mathf.Max(1f, newMax);
        if (healDifference && difference > 0f) currentHealth += difference;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        NotifyHealthChanged();
    }

    public void Revive(float healthPercent = 0.5f)
    {
        if (!isDead)
        {
            return;
        }

        isDead = false;
        currentHealth = Mathf.Max(1f, maxHealth * Mathf.Clamp01(healthPercent));
        SetGameplayComponentsEnabled(true);

        if (animator != null)
        {
            animator.ResetTrigger(DieHash);
            animator.Play("Idle", 0, 0f);
        }

        NotifyHealthChanged();
        GameAudio.Play(GameSound.PlayerRevived);
        OnPlayerRevived?.Invoke();
    }

    [ContextMenu("Debug: Receber 25 de Dano")]
    public void DebugTakeDamage()
    {
        TakeDamage(25);
    }

    [ContextMenu("Debug: Reviver")]
    public void DebugRevive()
    {
        Revive(1f);
    }

    private void NotifyHealthChanged()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (hpBarFill != null)
        {
            float healthPercent = maxHealth > 0f ? currentHealth / maxHealth : 0f;
            Vector3 scale = hpBarFill.transform.localScale;
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
        BlockMultiplier = 1f;
        GameAudio.Play(GameSound.PlayerDown);

        if (animator != null) animator.SetTrigger(DieHash);
        SetGameplayComponentsEnabled(false);

        OnPlayerDied?.Invoke();
        OnDeath?.Invoke();
    }

    private void SetGameplayComponentsEnabled(bool value)
    {
        if (movement != null)
        {
            if (!value) movement.StopMovement();
            movement.SetDowned(!value);
            movement.enabled = value;
        }

        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = value;
    }
}
