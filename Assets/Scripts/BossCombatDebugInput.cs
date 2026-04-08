using UnityEngine;

public class BossCombatDebugInput : MonoBehaviour
{
    public BossHealth bossHealth;
    public TrainingPlayerHealth playerHealth;
    public BossEpisodeManager episodeManager;

    [Header("Debug Damage")]
    public int bossTestHitDamage = 10;
    public float playerTestHitDamage = 10f;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1) && bossHealth != null)
            bossHealth.TakeDamage(bossTestHitDamage);

        if (Input.GetKeyDown(KeyCode.F2) && playerHealth != null)
            playerHealth.TakeDamage(playerTestHitDamage);

        if (Input.GetKeyDown(KeyCode.F3) && bossHealth != null)
            bossHealth.TakeDamage(9999);

        if (Input.GetKeyDown(KeyCode.F4) && playerHealth != null)
            playerHealth.TakeDamage(9999f);

        if (Input.GetKeyDown(KeyCode.F5) && episodeManager != null)
            episodeManager.ResetEpisode();
    }
}
