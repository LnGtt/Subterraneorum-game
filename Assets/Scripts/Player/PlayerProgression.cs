using System;
using UnityEngine;

public class PlayerProgression : MonoBehaviour
{
    [SerializeField] private float baseXPToNextLevel = 100f;
    [SerializeField] private float xpMultiplier = 1.5f;
    [SerializeField] private int skillPointsPerLevel = 1;

    private float currentXP;
    private float xpToNextLevel;
    private int currentLevel = 1;

    public float CurrentXP => currentXP;
    public float XPToNextLevel => xpToNextLevel;
    public int CurrentLevel => currentLevel;
    public int SkillPoints { get; private set; }

    public float XPGainMultiplier { get; set; } = 1f;

    public event Action<PlayerProgression, int> OnLevelUp;

    private void Awake()
    {
        xpToNextLevel = CalculateXPToNextLevel(currentLevel);
    }

    public void AddXP(float amount)
    {
        AddXP(amount, applyMultiplier: true);
    }

    public void AddXP(float amount, bool applyMultiplier)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentXP += applyMultiplier ? amount * XPGainMultiplier : amount;

        while (currentXP >= xpToNextLevel)
        {
            LevelUp();
        }
    }

    public float RemoveXP(float amount)
    {
        float removed = Mathf.Clamp(amount, 0f, currentXP);
        currentXP -= removed;
        return removed;
    }

    public float XPOfCurrentLevel => currentLevel > 1 ? CalculateXPToNextLevel(currentLevel - 1) : 0f;

    public float LoseLevel()
    {
        if (currentLevel <= 1) return 0f;

        float lost = XPOfCurrentLevel;
        currentLevel--;
        xpToNextLevel = CalculateXPToNextLevel(currentLevel);
        currentXP = Mathf.Min(currentXP, xpToNextLevel - 1f);
        SkillPoints = Mathf.Max(0, SkillPoints - skillPointsPerLevel);
        return lost;
    }

    public bool TrySpendSkillPoint()
    {
        if (SkillPoints <= 0)
        {
            return false;
        }

        SkillPoints--;
        return true;
    }

    [ContextMenu("Debug: + 100 de XP")]
    public void DebugAddXP()
    {
        AddXP(100);
    }

    private void LevelUp()
    {
        currentXP -= xpToNextLevel;
        currentLevel++;
        SkillPoints += skillPointsPerLevel;
        xpToNextLevel = CalculateXPToNextLevel(currentLevel);
        GameAudio.Play(GameSound.LevelUp);
        LevelUpEffect.Play(transform, currentLevel);
        OnLevelUp?.Invoke(this, currentLevel);
    }

    private float CalculateXPToNextLevel(int level)
    {
        return Mathf.Max(1f, baseXPToNextLevel * Mathf.Pow(xpMultiplier, level - 1));
    }
}
