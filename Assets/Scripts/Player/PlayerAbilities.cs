using System.Collections;
using UnityEngine;

public class PlayerAbilities : MonoBehaviour
{
    private PlayerIdentity identity;
    private PlayerInputHandler input;
    private PlayerCombat combat;
    private PlayerStatus status;
    private PlayerMovement movement;
    private Rigidbody body;

    private readonly float[] readyAt = new float[2];
    private bool ability2Unlocked;
    private Coroutine shieldRoutine;
    private Coroutine speedRoutine;

    public float CooldownMultiplier { get; set; } = 1f;

    public float PowerMultiplier { get; set; } = 1f;

    private void Awake()
    {
        identity = GetComponent<PlayerIdentity>();
        input = GetComponent<PlayerInputHandler>();
        combat = GetComponent<PlayerCombat>();
        status = GetComponent<PlayerStatus>();
        movement = GetComponent<PlayerMovement>();
        body = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        if (input == null) return;
        input.OnAbility1Input += UseAbility1;
        input.OnAbility2Input += UseAbility2;
    }

    private void OnDisable()
    {
        if (input == null) return;
        input.OnAbility1Input -= UseAbility1;
        input.OnAbility2Input -= UseAbility2;
    }

    public AbilityDefinition Get(int slot)
    {
        ClassDefinition def = identity != null ? identity.Class : null;
        if (def == null) return null;
        return slot == 0 ? def.ability1 : def.ability2;
    }

    public bool IsUnlocked(int slot) => slot == 0 || ability2Unlocked;

    public void UnlockAbility2() => ability2Unlocked = true;

    public float CooldownRemaining(int slot) => Mathf.Max(0f, readyAt[slot] - Time.time);

    public float CooldownDuration(int slot)
    {
        AbilityDefinition ability = Get(slot);
        return ability != null ? ability.cooldown * CooldownMultiplier : 1f;
    }

    private void UseAbility1() => TryUse(0);

    private void UseAbility2() => TryUse(1);

    public bool TryUse(int slot)
    {
        AbilityDefinition ability = Get(slot);
        if (ability == null || !IsUnlocked(slot) || status == null || status.IsDead || !enabled) return false;
        if (Time.time < readyAt[slot]) return false;

        readyAt[slot] = Time.time + ability.cooldown * CooldownMultiplier;
        GameAudio.Play(GameSound.Ability);
        Execute(ability);
        return true;
    }

    private Color ClassColor => identity != null && identity.Class != null ? identity.Class.color : Color.white;

    private void Execute(AbilityDefinition a)
    {
        switch (a.kind)
        {
            case AbilityKind.ShieldBlock:
                if (shieldRoutine != null) StopCoroutine(shieldRoutine);
                shieldRoutine = StartCoroutine(ShieldRoutine(a));
                break;
            case AbilityKind.HeavySlam:
                StartCoroutine(HeavySlamRoutine(a));
                break;
            case AbilityKind.ArrowVolley:
                ArrowVolley(a);
                break;
            case AbilityKind.AgilityBurst:
                movement.ResetDashCooldown();
                if (speedRoutine != null) StopCoroutine(speedRoutine);
                speedRoutine = StartCoroutine(SpeedRoutine(a.amount, a.duration));
                GroundTelegraph.Spawn(transform.position, 1.2f, ClassColor, a.duration, GroundTelegraph.Shape.Ring, false, transform);
                break;
            case AbilityKind.ArcaneBlast:
                StartCoroutine(ArcaneBlastRoutine(a));
                break;
            case AbilityKind.HealingAura:
                HealingAura(a);
                break;
            case AbilityKind.BladeFlurry:
                StartCoroutine(BladeFlurryRoutine(a));
                break;
            case AbilityKind.ShadowStep:
                StartCoroutine(ShadowStepRoutine(a));
                break;
        }
    }

    private IEnumerator ShieldRoutine(AbilityDefinition a)
    {
        status.BlockMultiplier = Mathf.Clamp01(1f - a.amount);
        movement.StatusSpeedMultiplier = 0.6f;
        GroundTelegraph.Spawn(transform.position, 1.3f, new Color(0.6f, 0.8f, 1f, 0.7f), a.duration, GroundTelegraph.Shape.Ring, false, transform);
        yield return new WaitForSeconds(a.duration);
        status.BlockMultiplier = 1f;
        movement.StatusSpeedMultiplier = 1f;
        shieldRoutine = null;
    }

    private IEnumerator HeavySlamRoutine(AbilityDefinition a)
    {
        Vector3 dir = combat.AimDirection(a.range + a.radius);
        Vector3 center = transform.position + dir * a.range;
        GroundTelegraph.Spawn(center, a.radius, new Color(1f, 0.55f, 0.2f, 0.6f), 0.35f);
        combat.PlayAttackAnimation();
        yield return new WaitForSeconds(0.35f);
        if (status.IsDead) yield break;
        int hits = combat.DamageEnemiesInSphere(center + Vector3.up, a.radius, a.damage * PowerMultiplier);
        GroundTelegraph.Spawn(center, a.radius, new Color(1f, 0.7f, 0.3f, 0.8f), 0.3f, GroundTelegraph.Shape.Ring, false);
        GameFeel.Shake(0.35f);
        if (hits > 0) GameFeel.HitStop(0.07f);
    }

    private void ArrowVolley(AbilityDefinition a)
    {
        ClassDefinition def = identity.Class;
        Vector3 dir = combat.AimDirection(a.range);
        combat.PlayAttackAnimation();
        int count = Mathf.Max(1, a.count);
        const float spread = 50f;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            Vector3 shot = Quaternion.Euler(0f, Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t), 0f) * dir;
            combat.FireProjectile(shot, a.damage * PowerMultiplier, def.projectilePrefab, def.projectileSpeed, a.range);
        }
    }

    private IEnumerator SpeedRoutine(float multiplier, float duration)
    {
        movement.StatusSpeedMultiplier = Mathf.Max(1f, multiplier);
        yield return new WaitForSeconds(duration);
        movement.StatusSpeedMultiplier = 1f;
        speedRoutine = null;
    }

    private IEnumerator ArcaneBlastRoutine(AbilityDefinition a)
    {
        Vector3 dir = combat.AimDirection(a.range + a.radius);
        Vector3 center = transform.position + dir * a.range;
        GroundTelegraph.Spawn(center, a.radius, new Color(0.45f, 0.55f, 1f, 0.55f), 0.45f);
        combat.PlayAttackAnimation();
        yield return new WaitForSeconds(0.45f);
        if (status.IsDead) yield break;
        int hits = combat.DamageEnemiesInSphere(center + Vector3.up, a.radius, a.damage * PowerMultiplier);
        GroundTelegraph.Spawn(center, a.radius, new Color(0.6f, 0.7f, 1f, 0.9f), 0.35f, GroundTelegraph.Shape.Disc, false);
        GameFeel.Shake(0.3f);
        if (hits > 0) GameFeel.HitStop(0.06f);
    }

    private void HealingAura(AbilityDefinition a)
    {
        combat.PlayAttackAnimation();
        float heal = a.amount * PowerMultiplier;
        foreach (PlayerStatus ally in PlayerRegistry.Players)
        {
            if (ally == null || ally.IsDead) continue;
            Vector3 offset = ally.transform.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= a.radius * a.radius) ally.Heal(heal);
        }

        GroundTelegraph.Spawn(transform.position, a.radius, new Color(0.4f, 1f, 0.5f, 0.45f), 0.6f, GroundTelegraph.Shape.Disc, false);
    }

    private IEnumerator BladeFlurryRoutine(AbilityDefinition a)
    {
        int hits = Mathf.Max(1, a.count);
        for (int i = 0; i < hits; i++)
        {
            if (status.IsDead) yield break;
            Vector3 dir = combat.AimDirection(a.range + a.radius);
            combat.PlayAttackAnimation();
            Vector3 center = transform.position + dir * a.range + Vector3.up;
            combat.DamageEnemiesInSphere(center, a.radius, a.damage * PowerMultiplier, applyLifeSteal: true);
            yield return new WaitForSeconds(0.15f);
        }
    }

    private IEnumerator ShadowStepRoutine(AbilityDefinition a)
    {
        Vector3 dir = combat.AimDirection(a.range);
        Vector3 start = transform.position;
        float distance = a.range;

        int env = LayerMask.GetMask("Environment");
        if (Physics.SphereCast(start + Vector3.up, 0.35f, dir, out RaycastHit wall, a.range, env, QueryTriggerInteraction.Ignore))
        {
            distance = Mathf.Max(0f, wall.distance - 0.2f);
        }

        Vector3 end = PartyLeash.ClampDestination(status, start, start + dir * distance);
        GroundTelegraph.Spawn(start, 0.9f, new Color(0.6f, 0.3f, 0.8f, 0.6f), 0.4f, GroundTelegraph.Shape.Disc, false);

        const int samples = 5;
        var hit = new System.Collections.Generic.HashSet<EnemyStatus>();
        foreach (Collider c in Physics.OverlapCapsule(start + Vector3.up, end + Vector3.up, a.radius, combat.EnemyLayer, QueryTriggerInteraction.Ignore))
        {
            EnemyStatus enemy = c.GetComponentInParent<EnemyStatus>();
            if (enemy != null && hit.Add(enemy)) combat.DealDamage(enemy, a.damage * PowerMultiplier, applyLifeSteal: true);
        }

        for (int i = 1; i <= samples; i++)
        {
            Vector3 p = Vector3.Lerp(start, end, i / (float)samples);
            if (body != null) body.MovePosition(p);
            else transform.position = p;
            yield return new WaitForFixedUpdate();
        }

        GroundTelegraph.Spawn(end, 0.9f, new Color(0.6f, 0.3f, 0.8f, 0.6f), 0.4f, GroundTelegraph.Shape.Disc, false);
    }
}
