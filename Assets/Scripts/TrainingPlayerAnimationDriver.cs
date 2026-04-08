using UnityEngine;

public class TrainingPlayerAnimationDriver : MonoBehaviour
{
    public Animator animator;

    [Header("Locomotion Params")]
    public string horizontalParam = "InputHorizontal";
    public string verticalParam = "InputVertical";
    public string moveAmountParam = "InputMagnitude";
    public string groundedBool = "isGrounded";
    public string strafingBool = "isStrafing";
    public string sprintingBool = "isSprinting";
    public float locomotionDamp = 0.04f;
    public bool forceCombatStrafing = true;

    [Header("Weapon-Aware Combat Params")]
    public string moveSetIdParam = "MoveSet_ID";
    public int swordMoveSetId = 3;
    public string upperBodyIdParam = "UpperBody_ID";
    public int swordUpperBodyId = 0;
    public string attackIdParam = "AttackID";
    public int swordAttackId = 1;
    public string defenseIdParam = "DefenseID";
    public int swordDefenseId = 1;
    public string actionStateParam = "ActionState";
    public int neutralActionState = 0;
    public bool forceSwordCombatMode = true;

    [Header("Triggers / States")]
    public string weakAttackTrigger = "WeakAttack";
    public string strongAttackTrigger = "StrongAttack";
    public string rollTrigger = "";
    public string hitTrigger = "TriggerReaction";
    public string deathBool = "isDead";
    public string weakAttackState = "";
    public string strongAttackState = "";
    public string rollState = "";
    public string hitState = "";
    public string deathTrigger = "";
    public string deathState = "";

    [Header("Crossfade")]
    public float weakAttackCrossfade = 0.04f;
    public float strongAttackCrossfade = 0.05f;
    public float rollCrossfade = 0.05f;
    public float hitCrossfade = 0.04f;
    public float deathCrossfade = 0.06f;

    private bool isDead;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    private void LateUpdate()
    {
        if (animator == null || isDead)
            return;

        if (forceSwordCombatMode)
            ApplySwordCombatDefaults();
    }

    public void ResetVisualState()
    {
        isDead = false;

        if (animator == null)
            return;

        animator.Rebind();
        animator.Update(0f);

        if (forceSwordCombatMode)
            ApplySwordCombatDefaults();

        SetMovement(0f, 0f, 0f, true, forceCombatStrafing, false);

        if (!string.IsNullOrEmpty(deathBool))
            animator.SetBool(deathBool, false);
    }

    public void ApplySwordCombatDefaults()
    {
        if (animator == null)
            return;

        if (!string.IsNullOrEmpty(moveSetIdParam))
            animator.SetInteger(moveSetIdParam, swordMoveSetId);

        if (!string.IsNullOrEmpty(upperBodyIdParam))
            animator.SetInteger(upperBodyIdParam, swordUpperBodyId);

        if (!string.IsNullOrEmpty(attackIdParam))
            animator.SetInteger(attackIdParam, swordAttackId);

        if (!string.IsNullOrEmpty(defenseIdParam))
            animator.SetInteger(defenseIdParam, swordDefenseId);

        if (!string.IsNullOrEmpty(actionStateParam))
            animator.SetInteger(actionStateParam, neutralActionState);
    }

    public void SetMovement(float localX, float localZ, float magnitude, bool grounded, bool strafing, bool sprinting)
    {
        if (animator == null || isDead)
            return;

        if (forceSwordCombatMode)
            ApplySwordCombatDefaults();

        if (!string.IsNullOrEmpty(horizontalParam))
            animator.SetFloat(horizontalParam, Mathf.Clamp(localX, -1f, 1f), locomotionDamp, Time.deltaTime);

        if (!string.IsNullOrEmpty(verticalParam))
            animator.SetFloat(verticalParam, Mathf.Clamp(localZ, -1f, 1f), locomotionDamp, Time.deltaTime);

        if (!string.IsNullOrEmpty(moveAmountParam))
            animator.SetFloat(moveAmountParam, Mathf.Clamp01(magnitude), locomotionDamp, Time.deltaTime);

        if (!string.IsNullOrEmpty(groundedBool))
            animator.SetBool(groundedBool, grounded);

        if (!string.IsNullOrEmpty(strafingBool))
            animator.SetBool(strafingBool, forceCombatStrafing || strafing);

        if (!string.IsNullOrEmpty(sprintingBool))
            animator.SetBool(sprintingBool, sprinting);
    }

    public bool PlayWeakAttack() => PlayAction(weakAttackState, weakAttackTrigger, weakAttackCrossfade);
    public bool PlayStrongAttack() => PlayAction(strongAttackState, strongAttackTrigger, strongAttackCrossfade);
    public bool PlayRoll() => PlayAction(rollState, rollTrigger, rollCrossfade);
    public bool PlayHit() => PlayAction(hitState, hitTrigger, hitCrossfade);

    public void PlayDeath()
    {
        if (animator == null)
            return;

        isDead = true;
        SetMovement(0f, 0f, 0f, true, forceCombatStrafing, false);

        if (!string.IsNullOrEmpty(deathBool))
            animator.SetBool(deathBool, true);

        PlayAction(deathState, deathTrigger, deathCrossfade);
    }

    private bool PlayAction(string stateName, string triggerName, float crossfade)
    {
        if (animator == null || isDead)
            return false;

        if (forceSwordCombatMode)
            ApplySwordCombatDefaults();

        if (!string.IsNullOrEmpty(stateName))
        {
            animator.CrossFadeInFixedTime(stateName, crossfade);
            return true;
        }

        if (!string.IsNullOrEmpty(triggerName))
        {
            animator.ResetTrigger(triggerName);
            animator.SetTrigger(triggerName);
            return true;
        }

        return false;
    }
}
