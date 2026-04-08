using UnityEngine;
using System.Collections;
using Invector.vCharacterController;

public class GameFlowManager : MonoBehaviour
{
    public vThirdPersonController playerController;
    public BossHealth bossHealth;
    public PauseMenuController pauseMenuController;
    public CombatMetricsCollector metricsCollector;

    public float loseDelay = 4f;
    public float winDelay = 4f;

    private bool finished = false;

    void Start()
    {
        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.BeginRun();
    }

    void Update()
    {
        if (finished) return;

        if (playerController != null && playerController.isDead)
        {
            finished = true;
            StartCoroutine(ShowLoseAfterDelay());
            return;
        }

        if (bossHealth != null && bossHealth.currentHP <= 0)
        {
            finished = true;
            StartCoroutine(ShowWinAfterDelay());
            return;
        }
    }

    IEnumerator ShowLoseAfterDelay()
    {
        yield return new WaitForSeconds(loseDelay);

        if (pauseMenuController != null)
            pauseMenuController.ShowLoseMenu();
    }

    IEnumerator ShowWinAfterDelay()
    {
        yield return new WaitForSeconds(winDelay);

        if (pauseMenuController != null)
            pauseMenuController.ShowWinMenu();
    }
}