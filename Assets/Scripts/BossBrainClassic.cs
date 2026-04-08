using System.Collections.Generic;
using UnityEngine;
using Invector;
using Invector.vCharacterController;

public class BossBrainClassic : MonoBehaviour, IBossHitboxOwner
{
    public Transform player;

    [Header("Animator Parameter Names")]
    public string moveStateParam = "moveState";
    public string attackOneHandParam = "AttackOneHand";
    public string attackTwoHandParam = "AttackTwoHand";
    public string attackSpinParam = "AttackSpin";
    public string punchLeftParam = "PunchLeft";
    public string hitParam = "Hit";
    public string deathParam = "Death";
    public string rollBackParam = "RollBackwards";

    [Header("Movement Speeds")]
    public float walkSpeed = 2f;
    public float runSpeed = 3.5f;
    public float sprintSpeed = 5f;
    public float backwardSpeed = 1.8f;
    public float strafeSpeed = 2f;
    public float diagonalSpeed = 2.2f;
    public float rollBackSpeed = 4f;
    public float rotateSpeed = 5f;

    private CapsuleCollider capsuleCollider;
    private Rigidbody bossRigidbody;

    [Header("Collision Fix")]
    public float floorNormalIgnoreY = 0.55f;
    public float minBlockingDistance = 0.03f;
    public float wallFacingThreshold = 0.10f;

    [Header("Collision Blocking")]
    public LayerMask movementBlockMask = ~0;
    public float collisionProbeRadius = 0.35f;
    public float collisionProbeHeight = 1.4f;
    public float collisionSkin = 0.05f;
    public bool debugCollision = false;

    [Header("Distance Thresholds")]
    public float chaseDistance = 10f;
    public float runDistance = 6f;
    public float walkDistance = 3.5f;
    public float attackDistance = 2.5f;
    public float tooCloseDistance = 1.6f;

    [Header("Preferred Combat Spacing")]
    public float preferredCombatDistance = 2.1f;
    public float preferredCombatBuffer = 0.4f;
    public float attackCommitDistance = 1.85f;

    [Header("Attack")]
    public float attackCooldown = 2f;
    public float attackOneHandDuration = 1.0f;
    public float attackTwoHandDuration = 1.2f;
    public float attackSpinDuration = 1.1f;
    public float punchLeftDuration = 0.9f;
    public float postAttackRecovery = 0.25f;
    public float attackFacingAngle = 45f;

    [Header("Attack Damage")]
    public int attackOneHandDamage = 3;
    public int attackTwoHandDamage = 5;
    public int attackSpinDamage = 4;
    public int punchLeftDamage = 2;

    [Header("Roll")]
    public float rollCooldown = 3f;
    public float rollDuration = 0.9f;
    public float postRollRecovery = 0.15f;

    [Header("Decision Windows")]
    public float chaseDecisionMin = 0.18f;
    public float chaseDecisionMax = 0.45f;
    public float combatDecisionMin = 0.30f;
    public float combatDecisionInterval = 0.85f;
    public float repositionDecisionMin = 0.22f;
    public float repositionDecisionInterval = 0.45f;

    [Header("Behavior Chances")]
    [Range(0f, 1f)] public float attackChanceInRange = 0.65f;
    [Range(0f, 1f)] public float rollChanceWhenTooClose = 0.35f;
    [Range(0f, 1f)] public float idleChanceInCombat = 0.14f;
    [Range(0f, 1f)] public float backwardChanceInCombat = 0.16f;
    [Range(0f, 1f)] public float diagonalMoveBias = 0.35f;
    [Range(0f, 1f)] public float chasePauseChance = 0.12f;
    [Range(0f, 1f)] public float closeChasePauseChance = 0.20f;

    [Header("Reactive Refresh")]
    public float decisionRefreshDistance = 0.55f;
    public float decisionRefreshPlayerShift = 0.65f;
    public float playerMovementSpeedForRefresh = 1.75f;

    [Tooltip("Sem daj sword hitbox aj left hand hitbox.")]
    public BossDamageHitbox[] attackHitboxes;

    private Animator animator;

    private float attackCooldownTimer = 0f;
    private float actionDurationTimer = 0f;
    private float rollCooldownTimer = 0f;
    private float decisionTimer = 0f;
    private float recoveryTimer = 0f;

    private bool isDead = false;
    private bool isAttacking = false;
    private bool isRolling = false;
    private bool isHit = false;

    private float distanceToPlayer = 999f;
    private float playerMoveSpeed = 0f;

    private Vector3 lastPlayerFlatPos;
    private Vector3 decisionStartPlayerFlatPos;
    private float decisionStartDistance = 999f;

    private BossAIState currentAIState = BossAIState.Idle;
    private BossMoveState currentMoveState = BossMoveState.Idle;
    private BossMoveState currentTacticalMove = BossMoveState.Idle;
    private BossAttackType lastAttackType = BossAttackType.None;
    private CombatBand currentCombatBand = CombatBand.Far;
    private CombatBand decisionStartBand = CombatBand.Far;

    private readonly RaycastHit[] movementHits = new RaycastHit[12];
    private readonly HashSet<Transform> hitRootsThisAttack = new HashSet<Transform>();
    private CombatMetricsCollector metricsCollector;
    private BossAttackType currentAttackType = BossAttackType.None;
    private bool currentAttackConnected = false;

    public enum BossAIState
    {
        Idle,
        Chase,
        Combat,
        Reposition,
        Attack,
        Hit,
        Dead
    }

    public enum BossMoveState
    {
        Idle = 0,
        WalkForward = 1,
        RunForward = 2,
        Sprint = 3,
        WalkBackward = 4,
        StrafeLeft = 5,
        StrafeRight = 6,
        WalkForwardLeft = 7,
        WalkForwardRight = 8
    }

    private enum BossAttackType
    {
        None,
        AttackOneHand,
        AttackTwoHand,
        AttackSpin,
        PunchLeft
    }

    private enum CombatBand
    {
        Far,
        Outer,
        Ideal,
        Tight,
        TooClose
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        bossRigidbody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        EnsureAnimator();
        metricsCollector = CombatMetricsCollector.Instance;
        SetMoveState(BossMoveState.Idle);
        SetAllActionBoolsFalse();
        WireHitboxes();
        DisableHitbox();

        if (player != null)
            lastPlayerFlatPos = FlatPosition(player.position);
    }

    private void WireHitboxes()
    {
        if (attackHitboxes == null)
            return;

        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] == null)
                continue;

            attackHitboxes[i].SetOwner(this);
        }
    }

    private bool EnsureAnimator()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        return animator != null;
    }

    private void Update()
    {
        if (player == null || isDead)
            return;

        UpdateTimers();
        UpdatePerception();

        if (isHit)
        {
            currentAIState = BossAIState.Hit;
            return;
        }

        if (isRolling)
        {
            currentAIState = BossAIState.Reposition;
            HandleRoll();
            return;
        }

        if (isAttacking)
        {
            currentAIState = BossAIState.Attack;
            HandleAttackTimer();
            return;
        }

        RotateTowardsPlayer();
        EvaluateStateTransitions();

        if (ShouldForceDecisionRefresh())
            decisionTimer = 0f;

        ExecuteCurrentState();
    }

    private void UpdateTimers()
    {
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;

        if (rollCooldownTimer > 0f)
            rollCooldownTimer -= Time.deltaTime;

        if (decisionTimer > 0f)
            decisionTimer -= Time.deltaTime;

        if (recoveryTimer > 0f)
            recoveryTimer -= Time.deltaTime;
    }

    private void UpdatePerception()
    {
        Vector3 currentPlayerFlat = FlatPosition(player.position);
        float playerFrameMove = Vector3.Distance(currentPlayerFlat, lastPlayerFlatPos);
        playerMoveSpeed = playerFrameMove / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPlayerFlatPos = currentPlayerFlat;

        distanceToPlayer = FlatDistanceToPlayer();
        currentCombatBand = GetCombatBand(distanceToPlayer);
    }

    private void EvaluateStateTransitions()
    {
        BossAIState nextState;

        if (distanceToPlayer < tooCloseDistance)
        {
            nextState = BossAIState.Reposition;
        }
        else if (distanceToPlayer > walkDistance + preferredCombatBuffer)
        {
            nextState = BossAIState.Chase;
        }
        else
        {
            nextState = BossAIState.Combat;
        }

        SetAIState(nextState);
    }

    private void SetAIState(BossAIState nextState)
    {
        if (currentAIState == nextState)
            return;

        currentAIState = nextState;
        decisionTimer = 0f;
        currentTacticalMove = BossMoveState.Idle;
    }

    private bool ShouldForceDecisionRefresh()
    {
        if (decisionTimer <= 0f)
            return true;

        if (currentAIState != BossAIState.Chase &&
            currentAIState != BossAIState.Combat &&
            currentAIState != BossAIState.Reposition)
            return false;

        float playerShiftSinceDecision = Vector3.Distance(FlatPosition(player.position), decisionStartPlayerFlatPos);
        float distanceShiftSinceDecision = Mathf.Abs(distanceToPlayer - decisionStartDistance);

        if (currentCombatBand != decisionStartBand && distanceShiftSinceDecision > preferredCombatBuffer * 0.5f)
            return true;

        switch (currentAIState)
        {
            case BossAIState.Chase:
                if (distanceToPlayer > runDistance && (currentTacticalMove == BossMoveState.Idle || currentTacticalMove == BossMoveState.WalkForward))
                    return true;

                if (playerShiftSinceDecision > decisionRefreshPlayerShift && distanceShiftSinceDecision > decisionRefreshDistance)
                    return true;

                return false;

            case BossAIState.Combat:
                if (distanceToPlayer > preferredCombatDistance + preferredCombatBuffer &&
                    (IsLateralOrIdle(currentTacticalMove) || currentTacticalMove == BossMoveState.WalkBackward))
                    return true;

                if (distanceToPlayer < preferredCombatDistance - preferredCombatBuffer &&
                    (IsForwardMove(currentTacticalMove) || currentTacticalMove == BossMoveState.Idle))
                    return true;

                if (playerMoveSpeed > playerMovementSpeedForRefresh &&
                    playerShiftSinceDecision > decisionRefreshPlayerShift &&
                    distanceShiftSinceDecision > decisionRefreshDistance * 0.7f)
                    return true;

                if (currentTacticalMove == BossMoveState.Idle && distanceToPlayer > attackDistance + 0.2f)
                    return true;

                return false;

            case BossAIState.Reposition:
                if (distanceToPlayer > preferredCombatDistance - 0.1f)
                    return true;

                if (playerShiftSinceDecision > decisionRefreshPlayerShift &&
                    distanceShiftSinceDecision > decisionRefreshDistance * 0.5f)
                    return true;

                return false;
        }

        return false;
    }

    private void ExecuteCurrentState()
    {
        switch (currentAIState)
        {
            case BossAIState.Chase:
                HandleChaseState();
                break;

            case BossAIState.Combat:
                HandleCombatState();
                break;

            case BossAIState.Reposition:
                HandleRepositionState();
                break;

            default:
                SetMoveState(BossMoveState.Idle);
                break;
        }
    }

    private void HandleChaseState()
    {
        if (decisionTimer <= 0f)
            ChooseChaseAction();

        ExecuteTacticalMove(currentTacticalMove);
    }

    private void ChooseChaseAction()
    {
        if (distanceToPlayer > chaseDistance)
        {
            if (Random.value < chasePauseChance * 0.35f)
            {
                SetTacticalMove(BossMoveState.Idle, Random.Range(0.18f, 0.32f));
                return;
            }

            SetTacticalMove(BossMoveState.Sprint, Random.Range(chaseDecisionMin, chaseDecisionMax));
            return;
        }

        if (distanceToPlayer > runDistance)
        {
            float r = Random.value;

            if (r < chasePauseChance * 0.5f)
            {
                SetTacticalMove(BossMoveState.Idle, Random.Range(0.18f, 0.35f));
            }
            else if (r < 0.82f)
            {
                SetTacticalMove(BossMoveState.RunForward, Random.Range(chaseDecisionMin, chaseDecisionMax));
            }
            else
            {
                SetTacticalMove(BossMoveState.WalkForward, Random.Range(chaseDecisionMin, chaseDecisionMax));
            }

            return;
        }

        if (distanceToPlayer > walkDistance)
        {
            float r = Random.value;

            if (r < closeChasePauseChance)
            {
                SetTacticalMove(BossMoveState.Idle, Random.Range(0.22f, 0.40f));
            }
            else if (r < closeChasePauseChance + diagonalMoveBias * 0.65f)
            {
                SetTacticalMove(GetRandomDiagonalMove(), Random.Range(0.24f, 0.50f));
            }
            else
            {
                SetTacticalMove(BossMoveState.WalkForward, Random.Range(0.22f, 0.45f));
            }

            return;
        }

        if (Random.value < closeChasePauseChance * 0.7f)
        {
            SetTacticalMove(BossMoveState.Idle, Random.Range(0.18f, 0.30f));
        }
        else if (Random.value < diagonalMoveBias)
        {
            SetTacticalMove(GetRandomDiagonalMove(), Random.Range(0.22f, 0.40f));
        }
        else
        {
            SetTacticalMove(BossMoveState.WalkForward, Random.Range(0.18f, 0.32f));
        }
    }

    private void HandleCombatState()
    {
        if (recoveryTimer > 0f)
        {
            SetMoveState(BossMoveState.Idle);
            return;
        }

        if (decisionTimer <= 0f)
            DecideCombatAction();

        if (!isAttacking)
            ExecuteTacticalMove(currentTacticalMove);
    }

    private void DecideCombatAction()
    {
        float sweetMin = preferredCombatDistance - preferredCombatBuffer;
        float sweetMax = preferredCombatDistance + preferredCombatBuffer;
        bool canAttack = CanAttackNow();

        if (!canAttack &&
            attackCooldownTimer <= 0f &&
            IsFacingPlayer() &&
            distanceToPlayer <= attackDistance + 0.6f)
        {
            SetTacticalMove(BossMoveState.WalkForward, Random.Range(0.18f, 0.32f));
            return;
        }

        if (distanceToPlayer > sweetMax)
        {
            float r = Random.value;

            if (r < idleChanceInCombat * 0.45f)
            {
                SetTacticalMove(BossMoveState.Idle, Random.Range(0.16f, 0.30f));
                return;
            }

            if (r < idleChanceInCombat * 0.45f + diagonalMoveBias)
            {
                SetTacticalMove(GetRandomDiagonalMove(), Random.Range(combatDecisionMin, combatDecisionInterval * 0.75f));
                return;
            }

            if (distanceToPlayer > runDistance - 0.5f)
            {
                SetTacticalMove(BossMoveState.RunForward, Random.Range(combatDecisionMin, combatDecisionInterval * 0.70f));
                return;
            }

            SetTacticalMove(BossMoveState.WalkForward, Random.Range(combatDecisionMin, combatDecisionInterval * 0.70f));
            return;
        }

        if (distanceToPlayer < sweetMin)
        {
            if (canAttack && Random.value < Mathf.Clamp01(GetDynamicAttackChance() + 0.10f))
            {
                StartChosenAttack();
                return;
            }

            if (rollCooldownTimer <= 0f &&
                distanceToPlayer < tooCloseDistance + 0.15f &&
                Random.value < rollChanceWhenTooClose * 0.75f)
            {
                StartRollBackward();
                return;
            }

            float r = Random.value;

            if (r < backwardChanceInCombat + 0.18f)
            {
                SetTacticalMove(BossMoveState.WalkBackward, Random.Range(combatDecisionMin * 0.8f, combatDecisionInterval * 0.65f));
                return;
            }

            if (r < backwardChanceInCombat + 0.18f + diagonalMoveBias * 0.45f)
            {
                SetTacticalMove(GetRandomDiagonalMove(), Random.Range(combatDecisionMin * 0.8f, combatDecisionInterval * 0.60f));
                return;
            }

            SetTacticalMove(GetRandomLateralMove(), Random.Range(combatDecisionMin * 0.8f, combatDecisionInterval * 0.60f));
            return;
        }

        if (canAttack && Random.value < GetDynamicAttackChance())
        {
            StartChosenAttack();
            return;
        }

        {
            float r = Random.value;
            float backwardSlice = backwardChanceInCombat * 0.65f;
            float diagonalSlice = diagonalMoveBias;
            float idleSlice = idleChanceInCombat;

            if (r < idleSlice)
            {
                SetTacticalMove(BossMoveState.Idle, Random.Range(0.15f, 0.28f));
                return;
            }

            if (r < idleSlice + backwardSlice)
            {
                SetTacticalMove(BossMoveState.WalkBackward, Random.Range(combatDecisionMin * 0.8f, combatDecisionInterval * 0.55f));
                return;
            }

            if (r < idleSlice + backwardSlice + diagonalSlice)
            {
                SetTacticalMove(GetRandomDiagonalMove(), Random.Range(combatDecisionMin, combatDecisionInterval * 0.80f));
                return;
            }

            SetTacticalMove(GetRandomLateralMove(), Random.Range(combatDecisionMin, combatDecisionInterval));
        }
    }

    private void HandleRepositionState()
    {
        if (distanceToPlayer < tooCloseDistance)
        {
            if (rollCooldownTimer <= 0f && Random.value < rollChanceWhenTooClose)
            {
                StartRollBackward();
                return;
            }

            if (decisionTimer <= 0f)
            {
                float r = Random.value;

                if (r < 0.60f)
                    SetTacticalMove(BossMoveState.WalkBackward, Random.Range(repositionDecisionMin, repositionDecisionInterval));
                else
                    SetTacticalMove(GetRandomLateralMove(), Random.Range(repositionDecisionMin, repositionDecisionInterval));
            }

            ExecuteTacticalMove(currentTacticalMove);
            return;
        }

        if (decisionTimer <= 0f)
        {
            float r = Random.value;

            if (r < 0.18f)
                SetTacticalMove(BossMoveState.Idle, Random.Range(0.12f, 0.22f));
            else if (r < 0.48f)
                SetTacticalMove(BossMoveState.WalkBackward, Random.Range(repositionDecisionMin, repositionDecisionInterval));
            else
                SetTacticalMove(GetRandomLateralMove(), Random.Range(repositionDecisionMin, repositionDecisionInterval));
        }

        ExecuteTacticalMove(currentTacticalMove);
    }

    private void SetTacticalMove(BossMoveState move, float duration)
    {
        if (currentTacticalMove != move && move != BossMoveState.Idle)
        {
            if (metricsCollector == null)
                metricsCollector = CombatMetricsCollector.Instance;

            if (metricsCollector != null)
                metricsCollector.RegisterBossAction(GetMetricMoveName(move));
        }

        currentTacticalMove = move;
        decisionTimer = duration;
        decisionStartDistance = distanceToPlayer;
        decisionStartPlayerFlatPos = FlatPosition(player.position);
        decisionStartBand = currentCombatBand;
    }

    private bool CanAttackNow()
    {
        if (attackCooldownTimer > 0f)
            return false;

        if (distanceToPlayer > attackCommitDistance)
            return false;

        if (!IsFacingPlayer())
            return false;

        return true;
    }

    private float GetDynamicAttackChance()
    {
        float chance = attackChanceInRange;

        if (distanceToPlayer <= preferredCombatDistance + 0.1f)
            chance += 0.08f;

        if (distanceToPlayer <= preferredCombatDistance - preferredCombatBuffer * 0.25f)
            chance += 0.08f;

        if (playerMoveSpeed > playerMovementSpeedForRefresh && distanceToPlayer > preferredCombatDistance + 0.1f)
            chance -= 0.15f;

        if (currentTacticalMove == BossMoveState.Idle)
            chance += 0.05f;

        return Mathf.Clamp01(chance);
    }

    private void ExecuteTacticalMove(BossMoveState move)
    {
        SetMoveState(move);

        switch (move)
        {
            case BossMoveState.Idle:
                break;

            case BossMoveState.WalkForward:
                MoveForward(walkSpeed);
                break;

            case BossMoveState.RunForward:
                MoveForward(runSpeed);
                break;

            case BossMoveState.Sprint:
                MoveForward(sprintSpeed);
                break;

            case BossMoveState.WalkBackward:
                MoveBackward(backwardSpeed);
                break;

            case BossMoveState.StrafeLeft:
                StrafeLeft(strafeSpeed);
                break;

            case BossMoveState.StrafeRight:
                StrafeRight(strafeSpeed);
                break;

            case BossMoveState.WalkForwardLeft:
                MoveForwardLeft(diagonalSpeed);
                break;

            case BossMoveState.WalkForwardRight:
                MoveForwardRight(diagonalSpeed);
                break;
        }
    }

    private void StartChosenAttack()
    {
        BossAttackType chosenAttack = ChooseAttackType();

        switch (chosenAttack)
        {
            case BossAttackType.AttackOneHand:
                StartAttack(BossAttackType.AttackOneHand, attackOneHandParam, attackOneHandDuration);
                break;

            case BossAttackType.AttackTwoHand:
                StartAttack(BossAttackType.AttackTwoHand, attackTwoHandParam, attackTwoHandDuration);
                break;

            case BossAttackType.AttackSpin:
                StartAttack(BossAttackType.AttackSpin, attackSpinParam, attackSpinDuration);
                break;

            case BossAttackType.PunchLeft:
                StartAttack(BossAttackType.PunchLeft, punchLeftParam, punchLeftDuration);
                break;
        }
    }

    private BossAttackType ChooseAttackType()
    {
        float oneHandWeight = 1.0f;
        float twoHandWeight = 1.0f;
        float spinWeight = 0.6f;
        float punchWeight = 0.5f;

        if (distanceToPlayer <= preferredCombatDistance - preferredCombatBuffer * 0.25f)
        {
            punchWeight += 0.9f;
            oneHandWeight += 0.35f;
            twoHandWeight += 0.15f;
            spinWeight -= 0.10f;
        }
        else if (distanceToPlayer <= attackDistance)
        {
            oneHandWeight += 0.45f;
            twoHandWeight += 0.35f;
            spinWeight += 0.15f;
        }
        else
        {
            spinWeight += 0.45f;
            twoHandWeight += 0.20f;
            punchWeight -= 0.15f;
        }

        if (playerMoveSpeed > playerMovementSpeedForRefresh)
        {
            oneHandWeight += 0.20f;
            punchWeight += 0.20f;
            twoHandWeight -= 0.10f;
        }
        else
        {
            twoHandWeight += 0.15f;
            spinWeight += 0.10f;
        }

        switch (lastAttackType)
        {
            case BossAttackType.AttackOneHand:
                oneHandWeight *= 0.25f;
                break;
            case BossAttackType.AttackTwoHand:
                twoHandWeight *= 0.25f;
                break;
            case BossAttackType.AttackSpin:
                spinWeight *= 0.20f;
                break;
            case BossAttackType.PunchLeft:
                punchWeight *= 0.25f;
                break;
        }

        oneHandWeight = Mathf.Max(0.05f, oneHandWeight);
        twoHandWeight = Mathf.Max(0.05f, twoHandWeight);
        spinWeight = Mathf.Max(0.05f, spinWeight);
        punchWeight = Mathf.Max(0.05f, punchWeight);

        float total = oneHandWeight + twoHandWeight + spinWeight + punchWeight;
        float roll = Random.value * total;

        if (roll < oneHandWeight)
            return BossAttackType.AttackOneHand;

        roll -= oneHandWeight;
        if (roll < twoHandWeight)
            return BossAttackType.AttackTwoHand;

        roll -= twoHandWeight;
        if (roll < spinWeight)
            return BossAttackType.AttackSpin;

        return BossAttackType.PunchLeft;
    }

    private void StartAttack(BossAttackType attackType, string attackParam, float duration)
    {
        isAttacking = true;
        attackCooldownTimer = attackCooldown;
        actionDurationTimer = duration;
        lastAttackType = attackType;
        currentAttackType = attackType;
        currentAttackConnected = false;
        hitRootsThisAttack.Clear();

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        ConfigureAttackHitboxes(GetAttackDamage(attackType));

        if (metricsCollector != null)
            metricsCollector.RegisterBossAttackAttempt(GetMetricAttackName(attackType));
        currentAIState = BossAIState.Attack;
        currentTacticalMove = BossMoveState.Idle;

        SetMoveState(BossMoveState.Idle);
        SetAllActionBoolsFalse();

        if (!EnsureAnimator()) return;
        animator.SetBool(attackParam, true);
    }

    private void HandleAttackTimer()
    {
        actionDurationTimer -= Time.deltaTime;

        if (actionDurationTimer <= 0f)
            EndAttack();
    }

    public void EndAttack()
    {
        if (!isActiveAndEnabled || !EnsureAnimator()) return;

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (currentAttackType != BossAttackType.None && !currentAttackConnected && metricsCollector != null)
            metricsCollector.RegisterBossAttackMiss(GetMetricAttackName(currentAttackType));

        isAttacking = false;
        recoveryTimer = postAttackRecovery;
        decisionTimer = 0f;
        currentAttackType = BossAttackType.None;
        currentAttackConnected = false;
        hitRootsThisAttack.Clear();

        DisableHitbox();
        animator.SetBool(attackOneHandParam, false);
        animator.SetBool(attackTwoHandParam, false);
        animator.SetBool(attackSpinParam, false);
        animator.SetBool(punchLeftParam, false);

        SetMoveState(BossMoveState.Idle);
    }

    public void FlushPendingAttackMetrics(bool registerMissIfNoHit = true)
    {
        if (currentAttackType == BossAttackType.None)
            return;

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (!currentAttackConnected && registerMissIfNoHit && metricsCollector != null)
            metricsCollector.RegisterBossAttackMiss(GetMetricAttackName(currentAttackType));

        currentAttackType = BossAttackType.None;
        currentAttackConnected = false;
        hitRootsThisAttack.Clear();
    }

    public void PlayHit()
    {
        PlayHitReaction();
    }

    public void EnableAttackHitbox()
    {
        if (!isActiveAndEnabled) return;
        EnableHitbox();
    }

    public void DisableAttackHitbox()
    {
        if (!isActiveAndEnabled) return;
        DisableHitbox();
    }

    private void StartRollBackward()
    {
        isRolling = true;
        rollCooldownTimer = rollCooldown;
        actionDurationTimer = rollDuration;
        currentAIState = BossAIState.Reposition;
        currentTacticalMove = BossMoveState.Idle;

        SetMoveState(BossMoveState.Idle);
        SetAllActionBoolsFalse();

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterBossAction("RollBackward");

        if (!EnsureAnimator()) return;
        animator.SetBool(rollBackParam, true);
    }

    private void HandleRoll()
    {
        actionDurationTimer -= Time.deltaTime;
        MoveWithCollision(-transform.forward, rollBackSpeed);

        if (actionDurationTimer <= 0f)
            EndRoll();
    }

    public void EndRoll()
    {
        if (!isActiveAndEnabled || !EnsureAnimator()) return;

        isRolling = false;
        recoveryTimer = postRollRecovery;
        decisionTimer = 0f;

        animator.SetBool(rollBackParam, false);
        SetMoveState(BossMoveState.Idle);
    }

    public void PlayHitReaction()
    {
        if (!EnsureAnimator()) return;

        if (isDead)
            return;

        isHit = true;
        isAttacking = false;
        isRolling = false;
        currentAIState = BossAIState.Hit;
        currentTacticalMove = BossMoveState.Idle;

        DisableHitbox();
        SetMoveState(BossMoveState.Idle);
        SetAllActionBoolsFalse();
        animator.SetBool(hitParam, true);
    }

    public bool IsBusy()
    {
        return isAttacking || isRolling;
    }

    public void EndHit()
    {
        if (!isActiveAndEnabled || !EnsureAnimator()) return;

        isHit = false;
        decisionTimer = 0f;

        animator.SetBool(hitParam, false);
        SetMoveState(BossMoveState.Idle);
    }

    public void Die()
    {
        if (!EnsureAnimator()) return;

        if (isDead)
            return;

        isDead = true;
        isAttacking = false;
        isRolling = false;
        isHit = false;
        currentAIState = BossAIState.Dead;
        currentTacticalMove = BossMoveState.Idle;

        DisableHitbox();
        SetMoveState(BossMoveState.Idle);
        SetAllActionBoolsFalse();
        animator.SetBool(deathParam, true);
    }

    public void ProcessBossHitboxHit(Collider other, int damage, bool acceptTrainingPlayerHealth, bool acceptInvectorPlayer)
    {
        if (other == null || currentAttackType == BossAttackType.None)
            return;

        Transform root = other.transform.root;
        if (root == null || hitRootsThisAttack.Contains(root))
            return;

        if (acceptTrainingPlayerHealth)
        {
            TrainingPlayerHealth trainingHealth = other.GetComponentInParent<TrainingPlayerHealth>();
            if (trainingHealth != null && !trainingHealth.IsDead)
            {
                hitRootsThisAttack.Add(root);
                trainingHealth.TakeDamage(damage);
                currentAttackConnected = true;

                if (metricsCollector == null)
                    metricsCollector = CombatMetricsCollector.Instance;

                if (metricsCollector != null)
                    metricsCollector.RegisterBossAttackHit(GetMetricAttackName(currentAttackType), damage);
                return;
            }
        }

        if (acceptInvectorPlayer)
        {
            vThirdPersonController invectorPlayer = other.GetComponentInParent<vThirdPersonController>();
            if (invectorPlayer != null && !invectorPlayer.isDead)
            {
                hitRootsThisAttack.Add(root);
                invectorPlayer.TakeDamage(new vDamage(damage));
                currentAttackConnected = true;

                if (metricsCollector == null)
                    metricsCollector = CombatMetricsCollector.Instance;

                if (metricsCollector != null)
                {
                    string attackName = GetMetricAttackName(currentAttackType);
                    metricsCollector.RegisterBossAttackHit(attackName, damage);
                    metricsCollector.RegisterPlayerTookDamage(damage, "BossAttack:" + attackName);
                }
            }
        }
    }

    private int GetAttackDamage(BossAttackType attackType)
    {
        return attackType switch
        {
            BossAttackType.AttackOneHand => attackOneHandDamage,
            BossAttackType.AttackTwoHand => attackTwoHandDamage,
            BossAttackType.AttackSpin => attackSpinDamage,
            BossAttackType.PunchLeft => punchLeftDamage,
            _ => 0
        };
    }

    private void ConfigureAttackHitboxes(int damage)
    {
        if (attackHitboxes == null)
            return;

        int appliedDamage = Mathf.Max(0, damage);
        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] != null)
                attackHitboxes[i].SetDamage(appliedDamage);
        }
    }

    private string GetMetricAttackName(BossAttackType attackType)
    {
        return attackType switch
        {
            BossAttackType.AttackOneHand => "AttackOneHand",
            BossAttackType.AttackTwoHand => "AttackTwoHand",
            BossAttackType.AttackSpin => "AttackSpin",
            BossAttackType.PunchLeft => "PunchLeft",
            _ => "Unknown"
        };
    }

    private string GetMetricMoveName(BossMoveState move)
    {
        return move switch
        {
            BossMoveState.WalkForward => "WalkForward",
            BossMoveState.RunForward => "RunForward",
            BossMoveState.Sprint => "Sprint",
            BossMoveState.WalkBackward => "WalkBackward",
            BossMoveState.StrafeLeft => "StrafeLeft",
            BossMoveState.StrafeRight => "StrafeRight",
            BossMoveState.WalkForwardLeft => "WalkForwardLeft",
            BossMoveState.WalkForwardRight => "WalkForwardRight",
            _ => move.ToString()
        };
    }

    private CombatBand GetCombatBand(float distance)
    {
        if (distance < tooCloseDistance)
            return CombatBand.TooClose;

        if (distance < preferredCombatDistance - preferredCombatBuffer)
            return CombatBand.Tight;

        if (distance <= preferredCombatDistance + preferredCombatBuffer)
            return CombatBand.Ideal;

        if (distance <= walkDistance + preferredCombatBuffer)
            return CombatBand.Outer;

        return CombatBand.Far;
    }

    private BossMoveState GetRandomDiagonalMove()
    {
        return Random.value < 0.5f ? BossMoveState.WalkForwardLeft : BossMoveState.WalkForwardRight;
    }

    private BossMoveState GetRandomLateralMove()
    {
        return Random.value < 0.5f ? BossMoveState.StrafeLeft : BossMoveState.StrafeRight;
    }

    private bool IsFacingPlayer()
    {
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return true;

        float angle = Vector3.Angle(transform.forward, direction.normalized);
        return angle <= attackFacingAngle;
    }

    private bool IsLateralOrIdle(BossMoveState move)
    {
        return move == BossMoveState.Idle ||
               move == BossMoveState.StrafeLeft ||
               move == BossMoveState.StrafeRight;
    }

    private bool IsForwardMove(BossMoveState move)
    {
        return move == BossMoveState.WalkForward ||
               move == BossMoveState.RunForward ||
               move == BossMoveState.Sprint ||
               move == BossMoveState.WalkForwardLeft ||
               move == BossMoveState.WalkForwardRight;
    }

    private void SetMoveState(BossMoveState newState)
    {
        if (currentMoveState == newState)
            return;

        if (!EnsureAnimator()) return;

        currentMoveState = newState;
        animator.SetInteger(moveStateParam, (int)newState);
    }

    private void SetAllActionBoolsFalse()
    {
        if (!EnsureAnimator()) return;

        animator.SetBool(attackOneHandParam, false);
        animator.SetBool(attackTwoHandParam, false);
        animator.SetBool(attackSpinParam, false);
        animator.SetBool(punchLeftParam, false);
        animator.SetBool(hitParam, false);
        animator.SetBool(rollBackParam, false);
    }

    private void RotateTowardsPlayer()
    {
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
    }

    private Vector3 FlatPosition(Vector3 pos)
    {
        pos.y = 0f;
        return pos;
    }

    private float FlatDistanceToPlayer()
    {
        Vector3 a = transform.position;
        Vector3 b = player.position;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void MoveForward(float speed)
    {
        MoveWithCollision(transform.forward, speed);
    }

    private void MoveBackward(float speed)
    {
        MoveWithCollision(-transform.forward, speed);
    }

    private void StrafeLeft(float speed)
    {
        MoveWithCollision(-transform.right, speed);
    }

    private void StrafeRight(float speed)
    {
        MoveWithCollision(transform.right, speed);
    }

    private void MoveForwardLeft(float speed)
    {
        Vector3 dir = (transform.forward - transform.right).normalized;
        MoveWithCollision(dir, speed);
    }

    private void MoveForwardRight(float speed)
    {
        Vector3 dir = (transform.forward + transform.right).normalized;
        MoveWithCollision(dir, speed);
    }

    private void MoveWithCollision(Vector3 worldDirection, float speed)
    {
        if (worldDirection.sqrMagnitude < 0.0001f)
            return;

        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 moveDir = worldDirection.normalized;
        float moveDistance = speed * Time.deltaTime;
        Vector3 start = transform.position;
        Vector3 target = start + moveDir * moveDistance;

        if (TryGetBlockingHit(start, moveDir, moveDistance, out RaycastHit hit))
        {
            float allowedDistance = Mathf.Max(0f, hit.distance - collisionSkin);
            target = start + moveDir * allowedDistance;

            if (debugCollision)
                Debug.DrawLine(start + Vector3.up, hit.point, Color.red, 0.2f);
        }

        if (bossRigidbody != null && !bossRigidbody.isKinematic)
            bossRigidbody.MovePosition(target);
        else
            transform.position = target;
    }

    private bool TryGetBlockingHit(Vector3 start, Vector3 direction, float distance, out RaycastHit bestHit)
    {
        bestHit = default;

        if (distance <= 0f)
            return false;

        Vector3 castUp = Vector3.up;
        float castRadius = collisionProbeRadius;
        Vector3 point1;
        Vector3 point2;

        if (capsuleCollider != null)
        {
            Vector3 center = transform.TransformPoint(capsuleCollider.center);

            float scaleX = Mathf.Abs(transform.lossyScale.x);
            float scaleY = Mathf.Abs(transform.lossyScale.y);
            float scaleZ = Mathf.Abs(transform.lossyScale.z);

            float radiusScale = Mathf.Max(scaleX, scaleZ);
            castRadius = capsuleCollider.radius * radiusScale;

            float scaledHeight = capsuleCollider.height * scaleY;
            float half = Mathf.Max(castRadius, (scaledHeight * 0.5f) - castRadius);

            point1 = center + castUp * half;
            point2 = center - castUp * half;
        }
        else
        {
            point1 = start + Vector3.up * collisionProbeRadius;
            point2 = start + Vector3.up * collisionProbeHeight;
        }

        int hitCount = Physics.CapsuleCastNonAlloc(
            point1,
            point2,
            castRadius,
            direction,
            movementHits,
            distance + collisionSkin,
            movementBlockMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        bool found = false;
        float bestDistance = float.MaxValue;
        Transform myRoot = transform.root;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = movementHits[i].collider;
            if (col == null)
                continue;

            if (col.transform.root == myRoot)
                continue;

            if (movementHits[i].distance <= minBlockingDistance)
                continue;

            if (movementHits[i].normal.y > floorNormalIgnoreY)
                continue;

            float wallFacing = Vector3.Dot(-movementHits[i].normal.normalized, direction.normalized);
            if (wallFacing < wallFacingThreshold)
                continue;

            if (movementHits[i].distance < bestDistance)
            {
                bestDistance = movementHits[i].distance;
                bestHit = movementHits[i];
                found = true;
            }
        }

        return found;
    }

    public void EnableHitbox()
    {
        if (attackHitboxes == null)
            return;

        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] != null)
                attackHitboxes[i].EnableHitbox();
        }
    }

    public void DisableHitbox()
    {
        if (attackHitboxes == null)
            return;

        for (int i = 0; i < attackHitboxes.Length; i++)
        {
            if (attackHitboxes[i] != null)
                attackHitboxes[i].DisableHitbox();
        }
    }
}