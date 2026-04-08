using UnityEngine;

public class TrainingPlayerHealth : MonoBehaviour
{
    public float maxHP = 100f;
    public float currentHP = 100f;

    [Header("Optional Feedback")]
    public Renderer targetRenderer;
    public Color normalColor = Color.white;
    public Color hitColor = Color.red;
    public float hitFlashDuration = 0.12f;
    public TrainingPlayerAnimationDriver animationDriver;

    [Header("Debug")]
    public bool debugDamageLogs = false;

    public bool IsDead => currentHP <= 0f;

    private float hitTimer = 0f;
    private CombatMetricsCollector metricsCollector;

    private void Awake()
    {
        if (animationDriver == null)
            animationDriver = GetComponent<TrainingPlayerAnimationDriver>();

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();

        currentHP = maxHP;
        SetNormalColor();
        metricsCollector = CombatMetricsCollector.Instance;
    }

    private void Update()
    {
        if (hitTimer > 0f)
        {
            hitTimer -= Time.deltaTime;
            if (hitTimer <= 0f)
                SetNormalColor();
        }
    }

    public void ResetHealth()
    {
        currentHP = maxHP;
        hitTimer = 0f;
        SetNormalColor();

        if (debugDamageLogs)
        {
            Debug.Log(
                $"[TrainingPlayerHealth] RESET hp={currentHP} frame={Time.frameCount}",
                this);
        }

        if (animationDriver != null)
            animationDriver.ResetVisualState();
    }

    public float GetNormalizedHP()
    {
        if (maxHP <= 0f)
            return 0f;

        return Mathf.Clamp01(currentHP / maxHP);
    }

    public void TakeDamage(float damage)
    {
        if (currentHP <= 0f)
            return;

        float appliedDamage = Mathf.Max(0f, damage);
        float hpBefore = currentHP;
        currentHP = Mathf.Max(0f, currentHP - appliedDamage);

        if (debugDamageLogs)
        {
            Debug.Log(
                $"[TrainingPlayerHealth] dmg={appliedDamage} hp={hpBefore}->{currentHP} frame={Time.frameCount}",
                this);
        }

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterPlayerTookDamage(appliedDamage, "BossAttack");

        FlashHit();

        if (currentHP > 0f)
        {
            if (animationDriver != null)
                animationDriver.PlayHit();
        }
        else
        {
            if (debugDamageLogs)
            {
                Debug.Log(
                    $"[TrainingPlayerHealth] DEATH frame={Time.frameCount}",
                    this);
            }

            if (metricsCollector == null)
                metricsCollector = CombatMetricsCollector.Instance;

            if (metricsCollector != null)
                metricsCollector.RegisterPlayerDeath();

            if (animationDriver != null)
                animationDriver.PlayDeath();
        }
    }

    public void ForceKill()
    {
        if (currentHP <= 0f)
            return;

        currentHP = 0f;
        FlashHit();

        if (debugDamageLogs)
        {
            Debug.Log(
                $"[TrainingPlayerHealth] FORCE DEATH frame={Time.frameCount}",
                this);
        }

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterPlayerDeath();

        if (animationDriver != null)
            animationDriver.PlayDeath();
    }

    private void FlashHit()
    {
        if (targetRenderer == null)
            return;

        targetRenderer.material.color = hitColor;
        hitTimer = hitFlashDuration;
    }

    private void SetNormalColor()
    {
        if (targetRenderer == null)
            return;

        targetRenderer.material.color = normalColor;
    }
}
