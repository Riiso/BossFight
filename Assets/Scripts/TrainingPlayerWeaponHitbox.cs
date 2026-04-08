using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TrainingPlayerWeaponHitbox : MonoBehaviour
{
    public TrainingPlayerBot owner;
    public BossHealth fallbackBossHealth;

    [Header("Debug")]
    public bool debugHitLogs = false;
    public bool debugRejectedHits = false;

    private readonly HashSet<Transform> alreadyHit = new HashSet<Transform>();
    private Collider cachedCollider;
    private bool windowOpen = false;
    private int currentDamage = 0;

    private void Awake()
    {
        cachedCollider = GetComponent<Collider>();
        if (cachedCollider != null)
            cachedCollider.isTrigger = true;

        ForceDisable();
    }

    public void OpenWindow(int damage)
    {
        currentDamage = Mathf.Max(0, damage);
        alreadyHit.Clear();
        windowOpen = currentDamage > 0;

        if (cachedCollider != null)
            cachedCollider.enabled = windowOpen;
    }

    public void CloseWindow()
    {
        windowOpen = false;
        currentDamage = 0;
        alreadyHit.Clear();

        if (cachedCollider != null)
            cachedCollider.enabled = false;
    }

    public void ForceDisable()
    {
        CloseWindow();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!windowOpen || currentDamage <= 0)
            return;

        Transform hitRoot = other.transform.root;
        if (hitRoot == null)
            return;

        if (alreadyHit.Contains(hitRoot))
        {
            if (debugRejectedHits)
            {
                Debug.Log(
                    $"[TrainingPlayerWeaponHitbox] REJECT duplicate hitbox={name} target={other.name} dmg={currentDamage} frame={Time.frameCount}",
                    this);
            }
            return;
        }

        Transform bossRoot = GetExpectedBossRoot();
        if (bossRoot == null)
        {
            if (debugRejectedHits)
            {
                Debug.Log(
                    $"[TrainingPlayerWeaponHitbox] REJECT no boss root configured hitbox={name} target={other.name} frame={Time.frameCount}",
                    this);
            }
            return;
        }

        if (hitRoot != bossRoot)
        {
            if (debugRejectedHits)
            {
                Debug.Log(
                    $"[TrainingPlayerWeaponHitbox] REJECT non-boss target hitbox={name} target={other.name} root={hitRoot.name} expectedBossRoot={bossRoot.name} frame={Time.frameCount}",
                    this);
            }
            return;
        }

        BossHealth bossHealth = other.GetComponentInParent<BossHealth>();
        if (bossHealth == null)
            bossHealth = fallbackBossHealth;

        if (bossHealth == null || bossHealth.IsDead)
            return;

        alreadyHit.Add(hitRoot);

        int hpBefore = bossHealth.currentHP;
        bossHealth.TakeDamage(currentDamage, $"TrainingWeaponHitbox:{name}");
        int hpAfter = bossHealth.currentHP;
        bool confirmed = hpAfter < hpBefore;

        if (debugHitLogs)
        {
            Debug.Log(
                $"[TrainingPlayerWeaponHitbox] ACCEPT hitbox={name} target={other.name} dmg={currentDamage} bossHP={hpBefore}->{hpAfter} confirmed={confirmed} frame={Time.frameCount}",
                this);
        }

        if (owner != null)
            owner.NotifyWeaponConnected(currentDamage, hpBefore, hpAfter, name);
    }

    private Transform GetExpectedBossRoot()
    {
        if (fallbackBossHealth != null)
            return fallbackBossHealth.transform.root;

        if (owner != null)
        {
            if (owner.bossHealth != null)
                return owner.bossHealth.transform.root;

            if (owner.boss != null)
                return owner.boss.root;
        }

        return null;
    }
}
