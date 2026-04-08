using UnityEngine;

public class BossEpisodeManager : MonoBehaviour
{
    public Transform boss;
    public Transform player;

    public Transform bossSpawn;
    public Transform playerSpawn;

    public BossHealth bossHealth;
    public TrainingPlayerHealth trainingPlayerHealth;
    public TrainingPlayerBot trainingPlayerBot;
    public BossCombatExecutor bossCombatExecutor;
    public BossRLAnimationDriver bossAnimationDriver;
    public TrainingPlayerAnimationDriver playerAnimationDriver;
    public CombatMetricsCollector metricsCollector;

    [Header("Optional Rigidbodies")]
    public Rigidbody bossRigidbody;
    public Rigidbody playerRigidbody;

    private void Awake()
    {
        if (boss != null && bossRigidbody == null)
            bossRigidbody = boss.GetComponent<Rigidbody>();

        if (player != null && playerRigidbody == null)
            playerRigidbody = player.GetComponent<Rigidbody>();

        if (player != null && playerAnimationDriver == null)
            playerAnimationDriver = player.GetComponent<TrainingPlayerAnimationDriver>();

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;
    }

    public void ResetEpisode()
    {
        if (boss != null && bossSpawn != null)
        {
            boss.position = bossSpawn.position;
            boss.rotation = bossSpawn.rotation;
        }

        if (player != null && playerSpawn != null)
        {
            player.position = playerSpawn.position;
            player.rotation = playerSpawn.rotation;
        }

        ResetBodyPhysics(bossRigidbody);
        ResetBodyPhysics(playerRigidbody);

        if (bossHealth != null)
        {
            bossHealth.ResetHealth();
        }
        else
        {
            if (bossCombatExecutor != null)
                bossCombatExecutor.ResetCombatState();

            if (bossAnimationDriver != null)
                bossAnimationDriver.ResetVisualState();
        }

        if (trainingPlayerHealth != null)
        {
            trainingPlayerHealth.ResetHealth();
        }
        else
        {
            if (playerAnimationDriver != null)
                playerAnimationDriver.ResetVisualState();
        }

        if (trainingPlayerBot != null)
            trainingPlayerBot.ResetBot();

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.BeginRun();
    }

    private void ResetBodyPhysics(Rigidbody body)
    {
        if (body == null)
            return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }
}
