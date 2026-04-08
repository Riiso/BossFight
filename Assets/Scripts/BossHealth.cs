using UnityEngine;

public class BossHealth : MonoBehaviour
{
    public int maxHP = 100;
    public int currentHP;

    [Header("Combat Behavior")]
    public bool preventHitInterruptWhileBusy = true;

    [Header("Debug")]
    public bool debugDamageLogs = false;
    public bool debugInterruptLogs = false;

    public bool IsDead => currentHP <= 0;

    private BossBrainClassic brain;
    private BossRLAnimationDriver rlAnimationDriver;
    private BossCombatExecutor combatExecutor;
    private CombatMetricsCollector metricsCollector;

    private void Awake()
    {
        currentHP = maxHP;
        brain = GetComponent<BossBrainClassic>();
        rlAnimationDriver = GetComponent<BossRLAnimationDriver>();
        combatExecutor = GetComponent<BossCombatExecutor>();
        metricsCollector = CombatMetricsCollector.Instance;
    }

    public void TakeDamage(int amount)
    {
        TakeDamage(amount, "Unknown");
    }

    public void TakeDamage(int amount, string source)
    {
        if (currentHP <= 0)
            return;

        int appliedDamage = Mathf.Max(0, amount);
        int hpBefore = currentHP;

        currentHP = Mathf.Max(0, currentHP - appliedDamage);

        if (debugDamageLogs)
        {
            Debug.Log(
                $"[BossHealth] source={source} dmg={appliedDamage} hp={hpBefore}->{currentHP} frame={Time.frameCount}",
                this);
        }

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterBossTookDamage(appliedDamage, source);

        if (currentHP <= 0)
        {
            currentHP = 0;
            TriggerDeath();
        }
        else
        {
            TriggerHit();
        }
    }

    private void TriggerHit()
    {
        if (preventHitInterruptWhileBusy)
        {
            bool rlBusy = combatExecutor != null && combatExecutor.IsBusy();
            bool classicBusy = brain != null && brain.isActiveAndEnabled && brain.IsBusy();

            if (rlBusy || classicBusy)
            {
                if (debugInterruptLogs)
                {
                    Debug.Log(
                        $"[BossHealth] IGNORE hit reaction because boss is busy frame={Time.frameCount}",
                        this);
                }
                return;
            }
        }

        if (brain != null && brain.isActiveAndEnabled)
        {
            brain.PlayHit();
            return;
        }

        if (rlAnimationDriver != null)
            rlAnimationDriver.PlayHitPulse();
    }

    private void TriggerDeath()
    {
        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterBossDeath();

        if (combatExecutor != null)
            combatExecutor.StopCombatForDeath();

        if (brain != null && brain.isActiveAndEnabled)
        {
            brain.Die();
            return;
        }

        if (rlAnimationDriver != null)
            rlAnimationDriver.PlayDeath();
    }

    public float GetNormalizedHP()
    {
        if (maxHP <= 0f)
            return 0f;

        return Mathf.Clamp01((float)currentHP / maxHP);
    }

    public void ResetHealth()
    {
        currentHP = maxHP;

        if (combatExecutor != null)
            combatExecutor.ResetCombatState();

        if (rlAnimationDriver != null)
            rlAnimationDriver.ResetVisualState();
    }
}