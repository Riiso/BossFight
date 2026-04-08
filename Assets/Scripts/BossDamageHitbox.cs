using UnityEngine;
using Invector;
using Invector.vCharacterController;

public interface IBossHitboxOwner
{
    void ProcessBossHitboxHit(Collider other, int damage, bool acceptTrainingPlayerHealth, bool acceptInvectorPlayer);
}

[RequireComponent(typeof(Collider))]
public class BossDamageHitbox : MonoBehaviour
{
    [Header("Fallback / Debug")]
    public int damage = 3;
    public bool acceptTrainingPlayerHealth = true;
    public bool acceptInvectorPlayer = true;

    [HideInInspector] public MonoBehaviour ownerBehaviour;

    private Collider cachedCollider;
    private IBossHitboxOwner owner;
    private bool canHit = false;

    private void Awake()
    {
        cachedCollider = GetComponent<Collider>();
        if (cachedCollider != null)
            cachedCollider.isTrigger = true;

        ResolveOwner();
        ForceDisable();
    }

    public void SetOwner(MonoBehaviour newOwner)
    {
        ownerBehaviour = newOwner;
        owner = newOwner as IBossHitboxOwner;
    }

    public void SetDamage(int newDamage)
    {
        damage = Mathf.Max(0, newDamage);
    }

    public void EnableHitbox()
    {
        canHit = true;
        if (cachedCollider != null)
            cachedCollider.enabled = true;
    }

    public void DisableHitbox()
    {
        canHit = false;
        if (cachedCollider != null)
            cachedCollider.enabled = false;
    }

    public void ForceDisable()
    {
        DisableHitbox();
    }

    private void ResolveOwner()
    {
        if (owner == null && ownerBehaviour != null)
            owner = ownerBehaviour as IBossHitboxOwner;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!canHit)
            return;

        ResolveOwner();
        if (owner != null)
        {
            owner.ProcessBossHitboxHit(other, damage, acceptTrainingPlayerHealth, acceptInvectorPlayer);
            return;
        }

        // Emergency fallback for scenes that still use the hitbox without an owner.
        if (acceptInvectorPlayer)
        {
            vThirdPersonController player = other.GetComponentInParent<vThirdPersonController>();
            if (player != null && !player.isDead)
                player.TakeDamage(new vDamage(damage));
        }

        if (acceptTrainingPlayerHealth)
        {
            TrainingPlayerHealth trainingHealth = other.GetComponentInParent<TrainingPlayerHealth>();
            if (trainingHealth != null && !trainingHealth.IsDead)
                trainingHealth.TakeDamage(damage);
        }
    }
}
