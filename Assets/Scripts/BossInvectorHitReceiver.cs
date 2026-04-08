using UnityEngine;

public class BossInvectorHitReceiver : MonoBehaviour
{
    public BossHealth bossHealth;
    public Animator playerAnimator;

    [Header("Damage")]
    public int lightAttackDamage = 3;
    public int heavyAttackDamage = 5;

    [Header("Filtering")]
    public float hitCooldown = 0.18f;
    public bool acceptInvectorHits = true;
    public string[] heavyClipKeywords = { "heavy", "strong", "power", "attack2", "attack_2", "attack3", "attack_3" };

    [Header("Debug")]
    public bool debugHitLogs = false;
    public bool debugRejectedHits = false;

    private float lastHitTime = -999f;

    private void Awake()
    {
        if (bossHealth == null)
            bossHealth = GetComponent<BossHealth>();

        if (bossHealth == null)
            bossHealth = GetComponentInParent<BossHealth>();

        if (playerAnimator == null)
            playerAnimator = GetComponentInChildren<Animator>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!acceptInvectorHits || bossHealth == null || bossHealth.IsDead)
            return;

        if (Time.time < lastHitTime + hitCooldown)
        {
            if (debugRejectedHits)
            {
                Debug.Log($"[BossInvectorHitReceiver] REJECT cooldown collider={other.name} frame={Time.frameCount}", this);
            }
            return;
        }

        Component invectorHitbox = other.GetComponent("vHitBox");
        if (invectorHitbox == null)
            return;

        if (!other.enabled || !other.gameObject.activeInHierarchy)
            return;

        int damage = ResolveDamage(out string sourceLabel, out string clipInfo);
        if (damage <= 0)
            return;

        int hpBefore = bossHealth.currentHP;
        bossHealth.TakeDamage(damage, sourceLabel);
        int hpAfter = bossHealth.currentHP;
        int appliedDamage = Mathf.Max(0, hpBefore - hpAfter);

        if (appliedDamage <= 0)
            return;

        lastHitTime = Time.time;

        if (debugHitLogs)
        {
            Debug.Log($"[BossInvectorHitReceiver] ACCEPT collider={other.name} source={sourceLabel} dmg={appliedDamage} clip={clipInfo} bossHP={hpBefore}->{hpAfter} frame={Time.frameCount}", this);
        }
    }

    private int ResolveDamage(out string sourceLabel, out string clipInfo)
    {
        sourceLabel = "InvectorLight";
        clipInfo = "no_animator";

        if (playerAnimator == null)
            return lightAttackDamage;

        if (CurrentAnimationLooksHeavy(out clipInfo))
        {
            sourceLabel = "InvectorHeavy";
            return heavyAttackDamage;
        }

        sourceLabel = "InvectorLight";
        return lightAttackDamage;
    }

    private bool CurrentAnimationLooksHeavy(out string clipInfo)
    {
        clipInfo = "none";

        for (int layer = 0; layer < playerAnimator.layerCount; layer++)
        {
            AnimatorClipInfo[] clips = playerAnimator.GetCurrentAnimatorClipInfo(layer);
            for (int i = 0; i < clips.Length; i++)
            {
                string clipName = clips[i].clip != null ? clips[i].clip.name : "null";
                clipInfo = clipName;

                string lower = clipName.ToLowerInvariant();
                for (int k = 0; k < heavyClipKeywords.Length; k++)
                {
                    string keyword = heavyClipKeywords[k];
                    if (!string.IsNullOrWhiteSpace(keyword) && lower.Contains(keyword.ToLowerInvariant()))
                        return true;
                }
            }
        }

        return false;
    }
}
