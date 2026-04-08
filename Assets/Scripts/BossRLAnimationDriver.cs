using System.Collections;
using UnityEngine;

public class BossRLAnimationDriver : MonoBehaviour
{
    public Animator animator;

    [Header("Animator Parameter Names")]
    public string moveStateParam = "moveState";
    public string attackOneHandParam = "AttackOneHand";
    public string attackTwoHandParam = "AttackTwoHand";
    public string attackSpinParam = "AttackSpin";
    public string punchLeftParam = "PunchLeft";
    public string hitParam = "Hit";
    public string deathParam = "Death";
    public string rollBackParam = "RollBackwards";

    [Header("Optional Real Hitboxes")]
    public BossDamageHitbox[] attackHitboxes;
    public float hitPulseDuration = 0.18f;

    private Coroutine hitRoutine;
    private bool isDead = false;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    public void ResetVisualState()
    {
        isDead = false;
        StopHitRoutine();
        DisableHitboxes();

        if (animator == null)
            return;

        animator.Rebind();
        animator.Update(0f);
        animator.SetInteger(moveStateParam, 0);
        animator.SetBool(attackOneHandParam, false);
        animator.SetBool(attackTwoHandParam, false);
        animator.SetBool(attackSpinParam, false);
        animator.SetBool(punchLeftParam, false);
        animator.SetBool(hitParam, false);
        animator.SetBool(deathParam, false);
        animator.SetBool(rollBackParam, false);
    }

    public void SetMoveState(int state)
    {
        if (animator == null || isDead)
            return;

        animator.SetInteger(moveStateParam, state);
    }

    public void SetAttackOneHand(bool value)
    {
        if (animator == null || isDead)
            return;

        animator.SetBool(attackOneHandParam, value);
    }

    public void SetAttackTwoHand(bool value)
    {
        if (animator == null || isDead)
            return;

        animator.SetBool(attackTwoHandParam, value);
    }

    public void SetAttackSpin(bool value)
    {
        if (animator == null || isDead)
            return;

        animator.SetBool(attackSpinParam, value);
    }

    public void SetPunchLeft(bool value)
    {
        if (animator == null || isDead)
            return;

        animator.SetBool(punchLeftParam, value);
    }

    public void SetRollBackward(bool value)
    {
        if (animator == null || isDead)
            return;

        animator.SetBool(rollBackParam, value);
    }

    public void ClearActionBools()
    {
        if (animator == null)
            return;

        animator.SetBool(attackOneHandParam, false);
        animator.SetBool(attackTwoHandParam, false);
        animator.SetBool(attackSpinParam, false);
        animator.SetBool(punchLeftParam, false);
        animator.SetBool(rollBackParam, false);
    }

    public void EnableHitboxes()
    {
        if (attackHitboxes == null)
            return;

        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] != null)
                attackHitboxes[i].EnableHitbox();
        }
    }

    public void DisableHitboxes()
    {
        if (attackHitboxes == null)
            return;

        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] != null)
                attackHitboxes[i].DisableHitbox();
        }
    }

    public void EnableAttackHitbox()
    {
        EnableHitboxes();
    }

    public void DisableAttackHitbox()
    {
        DisableHitboxes();
    }

    public void EndHit()
    {
        if (animator == null)
            return;

        animator.SetBool(hitParam, false);
    }

    public void PlayHitPulse()
    {
        if (animator == null || isDead)
            return;

        StopHitRoutine();
        hitRoutine = StartCoroutine(HitPulseRoutine());
    }

    public void PlayDeath()
    {
        if (animator == null)
            return;

        isDead = true;
        StopHitRoutine();
        DisableHitboxes();
        animator.SetInteger(moveStateParam, 0);
        ClearActionBools();
        animator.SetBool(hitParam, false);
        animator.SetBool(deathParam, true);
    }

    private IEnumerator HitPulseRoutine()
    {
        animator.SetBool(hitParam, true);
        yield return new WaitForSeconds(hitPulseDuration);
        animator.SetBool(hitParam, false);
        hitRoutine = null;
    }

    private void StopHitRoutine()
    {
        if (hitRoutine != null)
        {
            StopCoroutine(hitRoutine);
            hitRoutine = null;
        }

        if (animator != null)
            animator.SetBool(hitParam, false);
    }
}
