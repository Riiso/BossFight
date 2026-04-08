using UnityEngine;

// Final analysis version: player-side combat metrics are intentionally disabled.
public class PlayerCombatMetricsTracker : MonoBehaviour
{
    public Animator playerAnimator;

    private void Awake()
    {
        if (playerAnimator == null)
            playerAnimator = GetComponentInChildren<Animator>();
    }

    public void NotifyConfirmedHit(string sourceLabel, int damage)
    {
        // Intentionally disabled in the final metrics version.
    }

    public void FlushPendingAttackMetrics(bool registerMissIfNoHit = true)
    {
        // Intentionally disabled in the final metrics version.
    }
}
