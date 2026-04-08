using System.Collections.Generic;
using UnityEngine;
using Invector;
using Invector.vCharacterController;

public class BossCombatExecutor : MonoBehaviour, IBossHitboxOwner
{
    public enum BossActionId
    {
        Idle = 0,
        WalkForward = 1,
        RunForward = 2,
        Sprint = 3,
        WalkBackward = 4,
        StrafeLeft = 5,
        StrafeRight = 6,
        WalkForwardLeft = 7,
        WalkForwardRight = 8,
        RollBackward = 9,
        AttackOneHand = 10,
        AttackTwoHand = 11,
        AttackSpin = 12,
        PunchLeft = 13
    }

    private enum BusyMode
    {
        None,
        Attack,
        Roll
    }

    [System.Serializable]
    public class AttackSpec
    {
        public string name = "Attack";
        public float damage = 3f;
        public float range = 3.0f;
        public float cooldown = 0.9f;
        public float windup = 0.20f;
        public float activeDuration = 0.10f;
        public float recovery = 0.25f;
        public float hitMoment = 0.20f;
        public float lungeSpeed = 1.5f;
        public float facingSpeed = 10f;
        public float minimumFacingDot = 0.45f;
    }

    [System.Serializable]
    public class RollSpec
    {
        public float cooldown = 2.8f;
        public float duration = 0.72f;
        public float speed = 5.8f;
    }

    public Transform player;
    public TrainingPlayerHealth playerHealth;
    public BossRLAgent agent;
    public BossRLAnimationDriver animationDriver;
    public BossHealth bossHealth;

    [Header("Defaults")]
    public bool forceCodeDefaults = true;

    [Header("Movement Speeds")]
    public float walkForwardSpeed = 2.0f;
    public float runForwardSpeed = 3.5f;
    public float sprintSpeed = 5.0f;
    public float walkBackwardSpeed = 1.8f;
    public float strafeSpeed = 2.0f;
    public float diagonalSpeed = 2.2f;
    public float rotateSpeed = 6f;

    [Header("Arena Bounds")]
    public Transform arenaCenter;
    public float arenaRadius = 12f;
    public bool clampToArena = true;

    [Header("Attack Rhythm")]
    public float globalAttackCooldownBonus = 0.10f;
    public float sameAttackRepeatLockout = 0.12f;
    public float postAttackMovementGrace = 0.22f;

    [Header("Attack Start Windows")]
    public float oneHandStartRangeMultiplier = 0.82f;
    public float twoHandStartRangeMultiplier = 0.86f;
    public float spinStartRangeMultiplier = 0.88f;
    public float punchStartRangeMultiplier = 0.92f;

    public float oneHandStartFacingDot = 0.62f;
    public float twoHandStartFacingDot = 0.58f;
    public float spinStartFacingDot = 0.42f;
    public float punchStartFacingDot = 0.78f;

    [Header("Attacks")]
    public AttackSpec attackOneHand = new AttackSpec
    {
        name = "AttackOneHand",
        damage = 3f,
        range = 3.1f,
        cooldown = 0.85f,
        windup = 0.18f,
        activeDuration = 0.25f,
        recovery = 0.24f,
        hitMoment = 0.32f,
        lungeSpeed = 1.7f,
        facingSpeed = 10f,
        minimumFacingDot = 0.45f
    };

    public AttackSpec attackTwoHand = new AttackSpec
    {
        name = "AttackTwoHand",
        damage = 5f,
        range = 3.85f,
        cooldown = 1.20f,
        windup = 0.30f,
        activeDuration = 0.56f,
        recovery = 0.34f,
        hitMoment = 0.44f,
        lungeSpeed = 3.1f,
        facingSpeed = 11f,
        minimumFacingDot = 0.35f
    };

    public AttackSpec attackSpin = new AttackSpec
    {
        name = "AttackSpin",
        damage = 4f,
        range = 3.9f,
        cooldown = 1.35f,
        windup = 0.26f,
        activeDuration = 0.34f,
        recovery = 0.38f,
        hitMoment = 0.26f,
        lungeSpeed = 1.7f,
        facingSpeed = 10f,
        minimumFacingDot = 0.20f
    };

    public AttackSpec punchLeft = new AttackSpec
    {
        name = "PunchLeft",
        damage = 2f,
        range = 1.9f,
        cooldown = 0.70f,
        windup = 0.10f,
        activeDuration = 0.30f,
        recovery = 0.18f,
        hitMoment = 0.94f,
        lungeSpeed = 0.55f,
        facingSpeed = 14f,
        minimumFacingDot = 0.65f
    };

    [Header("Roll")]
    public RollSpec rollBackward = new RollSpec();

    [Header("Debug")]
    public bool debugBossHitLogs = false;
    public bool debugBossAttackFlow = false;

    private BusyMode busyMode = BusyMode.None;
    private AttackSpec currentAttack;
    private BossActionId currentBusyAction = BossActionId.Idle;
    private BossActionId lastStartedAttackAction = BossActionId.Idle;
    private float busyTimer = 0f;
    private float currentBusyDuration = 0f;
    private float nextAttackTime = 0f;
    private float nextRollTime = 0f;
    private float movementLockUntil = 0f;
    private bool externallyLocked = false;
    private bool hitboxesOpen = false;
    private bool currentAttackConnected = false;
    private readonly HashSet<Transform> hitRootsThisAttack = new HashSet<Transform>();

    private float lastAttackStartTime = -999f;
    private float lastAttackEndTime = -999f;
    private float lastSuccessfulHitTime = -999f;
    private CombatMetricsCollector metricsCollector;
    private BossActionId lastLoggedDiscreteAction = BossActionId.Idle;

    private void Awake()
    {
        if (animationDriver == null)
            animationDriver = GetComponent<BossRLAnimationDriver>();

        if (bossHealth == null)
            bossHealth = GetComponent<BossHealth>();

        metricsCollector = CombatMetricsCollector.Instance;

        if (forceCodeDefaults)
            ApplyForceCodeDefaults();

        WireBossHitboxes();
    }

    private void ApplyForceCodeDefaults()
    {
        attackOneHand.name = "AttackOneHand";
        attackOneHand.damage = 3f;
        attackOneHand.range = 3.1f;
        attackOneHand.cooldown = 0.85f;
        attackOneHand.windup = 0.18f;
        attackOneHand.activeDuration = 0.25f;
        attackOneHand.recovery = 0.24f;
        attackOneHand.hitMoment = 0.32f;
        attackOneHand.lungeSpeed = 1.7f;
        attackOneHand.facingSpeed = 10f;
        attackOneHand.minimumFacingDot = 0.45f;

        attackTwoHand.name = "AttackTwoHand";
        attackTwoHand.damage = 5f;
        attackTwoHand.range = 3.85f;
        attackTwoHand.cooldown = 1.20f;
        attackTwoHand.windup = 0.30f;
        attackTwoHand.activeDuration = 0.56f;
        attackTwoHand.recovery = 0.34f;
        attackTwoHand.hitMoment = 0.44f;
        attackTwoHand.lungeSpeed = 3.1f;
        attackTwoHand.facingSpeed = 11f;
        attackTwoHand.minimumFacingDot = 0.35f;

        attackSpin.name = "AttackSpin";
        attackSpin.damage = 4f;
        attackSpin.range = 3.9f;
        attackSpin.cooldown = 1.35f;
        attackSpin.windup = 0.26f;
        attackSpin.activeDuration = 0.34f;
        attackSpin.recovery = 0.38f;
        attackSpin.hitMoment = 0.26f;
        attackSpin.lungeSpeed = 1.7f;
        attackSpin.facingSpeed = 10f;
        attackSpin.minimumFacingDot = 0.20f;

        punchLeft.name = "PunchLeft";
        punchLeft.damage = 2f;
        punchLeft.range = 1.9f;
        punchLeft.cooldown = 0.70f;
        punchLeft.windup = 0.10f;
        punchLeft.activeDuration = 0.30f;
        punchLeft.recovery = 0.18f;
        punchLeft.hitMoment = 0.94f;
        punchLeft.lungeSpeed = 0.55f;
        punchLeft.facingSpeed = 14f;
        punchLeft.minimumFacingDot = 0.65f;

        rollBackward.cooldown = 2.8f;
        rollBackward.duration = 0.72f;
        rollBackward.speed = 5.8f;
    }

    private void Update()
    {
        TickBusyAction();
    }

    private void WireBossHitboxes()
    {
        if (animationDriver == null || animationDriver.attackHitboxes == null)
            return;

        for (int i = 0; i < animationDriver.attackHitboxes.Length; i++)
        {
            BossDamageHitbox hitbox = animationDriver.attackHitboxes[i];
            if (hitbox == null)
                continue;

            hitbox.SetOwner(this);
            hitbox.ForceDisable();
        }
    }

    public void ExecuteAction(int action)
    {
        if ((BossActionId)action < BossActionId.AttackOneHand)
            DecayAttackPressure();

        switch ((BossActionId)action)
        {
            case BossActionId.Idle: DoIdle(); break;
            case BossActionId.WalkForward: DoWalkForward(); break;
            case BossActionId.RunForward: DoRunForward(); break;
            case BossActionId.Sprint: DoSprint(); break;
            case BossActionId.WalkBackward: DoWalkBackward(); break;
            case BossActionId.StrafeLeft: DoStrafeLeft(); break;
            case BossActionId.StrafeRight: DoStrafeRight(); break;
            case BossActionId.WalkForwardLeft: DoWalkForwardLeft(); break;
            case BossActionId.WalkForwardRight: DoWalkForwardRight(); break;
            case BossActionId.RollBackward: DoRollBackward(); break;
            case BossActionId.AttackOneHand: DoAttackOneHand(); break;
            case BossActionId.AttackTwoHand: DoAttackTwoHand(); break;
            case BossActionId.AttackSpin: DoAttackSpin(); break;
            case BossActionId.PunchLeft: DoPunchLeft(); break;
        }
    }

    public void DoIdle()
    {
        if (!CanMove())
            return;

        lastLoggedDiscreteAction = BossActionId.Idle;
        SetMoveState((int)BossActionId.Idle);
    }

    public void DoWalkForward()
    {
        if (!CanMove()) return;
        RegisterBossMovementAction(BossActionId.WalkForward);
        FacePlayerSmooth(rotateSpeed);
        SetMoveState((int)BossActionId.WalkForward);
        Move(transform.forward, walkForwardSpeed);
    }

    public void DoRunForward()
    {
        if (!CanMove()) return;
        RegisterBossMovementAction(BossActionId.RunForward);
        FacePlayerSmooth(rotateSpeed);
        SetMoveState((int)BossActionId.RunForward);
        Move(transform.forward, runForwardSpeed);
    }

    public void DoSprint()
    {
        if (!CanMove()) return;
        RegisterBossMovementAction(BossActionId.Sprint);
        FacePlayerSmooth(rotateSpeed);
        SetMoveState((int)BossActionId.Sprint);
        Move(transform.forward, sprintSpeed);
    }

    public void DoWalkBackward()
    {
        if (!CanMove()) return;
        RegisterBossMovementAction(BossActionId.WalkBackward);
        FacePlayerSmooth(rotateSpeed);
        SetMoveState((int)BossActionId.WalkBackward);
        Move(-transform.forward, walkBackwardSpeed);
    }

    public void DoStrafeLeft()
    {
        if (!CanMove()) return;
        RegisterBossMovementAction(BossActionId.StrafeLeft);
        FacePlayerSmooth(rotateSpeed);
        SetMoveState((int)BossActionId.StrafeLeft);
        Move(-transform.right, strafeSpeed);
    }

    public void DoStrafeRight()
    {
        if (!CanMove()) return;
        RegisterBossMovementAction(BossActionId.StrafeRight);
        FacePlayerSmooth(rotateSpeed);
        SetMoveState((int)BossActionId.StrafeRight);
        Move(transform.right, strafeSpeed);
    }

    public void DoWalkForwardLeft()
    {
        if (!CanMove()) return;
        RegisterBossMovementAction(BossActionId.WalkForwardLeft);
        FacePlayerSmooth(rotateSpeed);
        SetMoveState((int)BossActionId.WalkForwardLeft);
        Move((transform.forward - transform.right).normalized, diagonalSpeed);
    }

    public void DoWalkForwardRight()
    {
        if (!CanMove()) return;
        RegisterBossMovementAction(BossActionId.WalkForwardRight);
        FacePlayerSmooth(rotateSpeed);
        SetMoveState((int)BossActionId.WalkForwardRight);
        Move((transform.forward + transform.right).normalized, diagonalSpeed);
    }

    public void DoRollBackward()
    {
        if (!CanStartRoll())
            return;

        RegisterBossMovementAction(BossActionId.RollBackward);
        busyMode = BusyMode.Roll;
        currentBusyAction = BossActionId.RollBackward;
        busyTimer = 0f;
        currentBusyDuration = Mathf.Max(0.01f, rollBackward.duration);
        nextRollTime = Time.time + Mathf.Max(0.01f, rollBackward.cooldown);
        SetMoveState((int)BossActionId.Idle);
        CloseAttackHitboxes();
        DecayAttackPressure();

        if (animationDriver != null)
            animationDriver.SetRollBackward(true);
    }

    public void DoAttackOneHand() => TryStartAttack(attackOneHand, BossActionId.AttackOneHand);
    public void DoAttackTwoHand() => TryStartAttack(attackTwoHand, BossActionId.AttackTwoHand);
    public void DoAttackSpin() => TryStartAttack(attackSpin, BossActionId.AttackSpin);
    public void DoPunchLeft() => TryStartAttack(punchLeft, BossActionId.PunchLeft);

    public bool IsBusy() => busyMode != BusyMode.None;
    public bool IsRolling() => busyMode == BusyMode.Roll;

    public void FlushPendingAttackMetrics(bool registerMissIfNoHit = true)
    {
        if (currentBusyAction < BossActionId.AttackOneHand)
            return;

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (!currentAttackConnected && registerMissIfNoHit && metricsCollector != null)
            metricsCollector.RegisterBossAttackMiss(GetMetricAttackName(currentBusyAction));

        currentAttackConnected = false;
        currentAttack = null;
        currentBusyAction = BossActionId.Idle;
        hitRootsThisAttack.Clear();
    }

    public bool IsLocked()
    {
        return externallyLocked || (bossHealth != null && bossHealth.IsDead);
    }

    public void StopCombatForDeath()
    {
        externallyLocked = true;
        ClearBusyState();
        SetMoveState((int)BossActionId.Idle);
    }

    public void ResetCombatState()
    {
        externallyLocked = false;
        nextAttackTime = 0f;
        nextRollTime = 0f;
        movementLockUntil = 0f;
        lastStartedAttackAction = BossActionId.Idle;
        lastAttackStartTime = -999f;
        lastAttackEndTime = -999f;
        lastSuccessfulHitTime = -999f;
        lastLoggedDiscreteAction = BossActionId.Idle;
        ClearBusyState();
        SetMoveState((int)BossActionId.Idle);
    }

    public void NotifyBossHitConnected(int damage)
    {
        if (currentAttackConnected)
            return;

        currentAttackConnected = true;
        lastSuccessfulHitTime = Time.time;

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null && currentBusyAction >= BossActionId.AttackOneHand)
            metricsCollector.RegisterBossAttackHit(GetMetricAttackName(currentBusyAction), damage);

        if (agent != null)
            agent.NotifyDealtDamage(damage);
    }

    public void ProcessBossHitboxHit(Collider other, int damage, bool acceptTrainingPlayerHealth, bool acceptInvectorPlayer)
    {
        TryProcessBossHit(other, damage, acceptTrainingPlayerHealth, acceptInvectorPlayer);
    }

    public void TryProcessBossHit(Collider other, int damage, bool acceptTrainingPlayerHealth, bool acceptInvectorPlayer)
    {
        if (!hitboxesOpen || currentAttack == null || other == null)
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

                if (debugBossHitLogs)
                {
                    Debug.Log(
                        $"[BossCombatExecutor] ACCEPT attack={currentAttack.name} target={other.name} dmg={damage} frame={Time.frameCount}",
                        this);
                }

                trainingHealth.TakeDamage(damage);
                if (metricsCollector == null)
                    metricsCollector = CombatMetricsCollector.Instance;
                if (metricsCollector != null && currentBusyAction >= BossActionId.AttackOneHand)
                    metricsCollector.RegisterPlayerTookDamage(damage, "BossAttack:" + GetMetricAttackName(currentBusyAction));
                NotifyBossHitConnected(damage);
                return;
            }
        }

        if (acceptInvectorPlayer)
        {
            vThirdPersonController invectorPlayer = other.GetComponentInParent<vThirdPersonController>();
            if (invectorPlayer != null && !invectorPlayer.isDead)
            {
                hitRootsThisAttack.Add(root);

                if (debugBossHitLogs)
                {
                    Debug.Log(
                        $"[BossCombatExecutor] ACCEPT attack={currentAttack.name} target={other.name} dmg={damage} frame={Time.frameCount}",
                        this);
                }

                invectorPlayer.TakeDamage(new vDamage(damage));
                if (metricsCollector == null)
                    metricsCollector = CombatMetricsCollector.Instance;
                if (metricsCollector != null && currentBusyAction >= BossActionId.AttackOneHand)
                    metricsCollector.RegisterPlayerTookDamage(damage, "BossAttack:" + GetMetricAttackName(currentBusyAction));
                NotifyBossHitConnected(damage);
            }
        }
    }

    private bool IsAttackStartValid(AttackSpec spec, BossActionId actionId)
    {
        if (player == null || spec == null)
            return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        if (distance <= 0.001f)
            return true;

        float forwardDot = Vector3.Dot(transform.forward, toPlayer.normalized);

        float rangeMultiplier = GetStartRangeMultiplier(actionId);
        float requiredDot = GetStartFacingDot(actionId);
        float allowedStartRange = spec.range * rangeMultiplier;

        return distance <= allowedStartRange && forwardDot >= requiredDot;
    }

    private float GetStartRangeMultiplier(BossActionId actionId)
    {
        return actionId switch
        {
            BossActionId.AttackOneHand => oneHandStartRangeMultiplier,
            BossActionId.AttackTwoHand => twoHandStartRangeMultiplier,
            BossActionId.AttackSpin => spinStartRangeMultiplier,
            BossActionId.PunchLeft => punchStartRangeMultiplier,
            _ => 0.85f
        };
    }

    private float GetStartFacingDot(BossActionId actionId)
    {
        return actionId switch
        {
            BossActionId.AttackOneHand => oneHandStartFacingDot,
            BossActionId.AttackTwoHand => twoHandStartFacingDot,
            BossActionId.AttackSpin => spinStartFacingDot,
            BossActionId.PunchLeft => punchStartFacingDot,
            _ => 0.55f
        };
    }

    private void ClearBusyState()
    {
        busyMode = BusyMode.None;
        currentAttack = null;
        currentBusyAction = BossActionId.Idle;
        busyTimer = 0f;
        currentBusyDuration = 0f;
        currentAttackConnected = false;
        hitRootsThisAttack.Clear();
        CloseAttackHitboxes();

        if (animationDriver != null)
            animationDriver.ClearActionBools();
    }

    private bool CanMove()
    {
        if (Time.time < movementLockUntil)
            return false;

        return !IsLocked() && !IsBusy();
    }

    private bool CanStartRoll()
    {
        if (Time.time < movementLockUntil)
            return false;

        return !IsLocked() && !IsBusy() && Time.time >= nextRollTime;
    }

    private void TryStartAttack(AttackSpec spec, BossActionId actionId)
    {
        if (spec == null)
            return;

        if (IsLocked() || IsBusy())
            return;

        if (Time.time < nextAttackTime)
            return;

        if (actionId == lastStartedAttackAction && Time.time < lastAttackStartTime + sameAttackRepeatLockout)
            return;

        FacePlayerInstant();

        bool inGoodWindow = IsAttackStartValid(spec, actionId);

        if (agent != null)
            agent.NotifyAttackStarted(inGoodWindow);

        if (!inGoodWindow)
            return;

        if (metricsCollector != null)
            metricsCollector.RegisterBossAttackAttempt(GetMetricAttackName(actionId));

        currentAttack = spec;
        currentBusyAction = actionId;
        lastStartedAttackAction = actionId;
        busyMode = BusyMode.Attack;
        busyTimer = 0f;
        currentBusyDuration = spec.windup + spec.activeDuration + spec.recovery;
        lastAttackStartTime = Time.time;
        nextAttackTime = Time.time + Mathf.Max(0.01f, spec.cooldown + globalAttackCooldownBonus);
        currentAttackConnected = false;
        hitRootsThisAttack.Clear();
        ConfigureAttackHitboxes(Mathf.RoundToInt(spec.damage));
        CloseAttackHitboxes();

        if (debugBossAttackFlow)
        {
            Debug.Log(
                $"[BossCombatExecutor] START attack={spec.name} dmg={spec.damage} hitMoment={spec.hitMoment:F2} active={spec.activeDuration:F2} frame={Time.frameCount}",
                this);
        }

        SetMoveState((int)BossActionId.Idle);
        SetAttackVisual(actionId, true);
    }

    private void TickBusyAction()
    {
        if (IsLocked())
            return;

        if (busyMode == BusyMode.None)
            return;

        busyTimer += Time.deltaTime;

        if (busyMode == BusyMode.Roll)
        {
            FacePlayerSmooth(rotateSpeed * 0.5f);
            Move(-transform.forward, rollBackward.speed, true);

            if (busyTimer >= currentBusyDuration)
            {
                if (animationDriver != null)
                    animationDriver.SetRollBackward(false);

                ClearBusyState();
            }

            return;
        }

        if (currentAttack == null)
        {
            ClearBusyState();
            return;
        }

        FacePlayerSmooth(currentAttack.facingSpeed);

        if ((busyTimer <= currentAttack.windup + currentAttack.activeDuration) && currentAttack.lungeSpeed > 0f)
            Move(transform.forward, currentAttack.lungeSpeed, true);

        bool shouldOpen =
            busyTimer >= currentAttack.hitMoment - 0.05f &&
            busyTimer <= currentAttack.hitMoment + currentAttack.activeDuration + 0.05f;

        if (shouldOpen && !hitboxesOpen)
            OpenAttackHitboxes();
        else if (!shouldOpen && hitboxesOpen)
            CloseAttackHitboxes();

        if (busyTimer >= currentBusyDuration)
        {
            if (!currentAttackConnected)
            {
                if (debugBossAttackFlow)
                {
                    Debug.Log(
                        $"[BossCombatExecutor] MISS attack={currentAttack.name} frame={Time.frameCount}",
                        this);
                }

                if (metricsCollector == null)
                    metricsCollector = CombatMetricsCollector.Instance;
                if (metricsCollector != null && currentBusyAction >= BossActionId.AttackOneHand)
                    metricsCollector.RegisterBossAttackMiss(GetMetricAttackName(currentBusyAction));

                if (agent != null)
                    agent.NotifyMissedAttack();
            }

            lastAttackEndTime = Time.time;
            movementLockUntil = Time.time + Mathf.Max(0f, postAttackMovementGrace);
            ClearBusyState();
        }
    }

    private void ConfigureAttackHitboxes(int damage)
    {
        if (animationDriver == null || animationDriver.attackHitboxes == null)
            return;

        for (int i = 0; i < animationDriver.attackHitboxes.Length; i++)
        {
            BossDamageHitbox hitbox = animationDriver.attackHitboxes[i];
            if (hitbox == null)
                continue;

            hitbox.SetOwner(this);
            hitbox.SetDamage(damage);
        }
    }

    private void OpenAttackHitboxes()
    {
        hitboxesOpen = true;
        if (animationDriver != null)
            animationDriver.EnableHitboxes();
    }

    private void CloseAttackHitboxes()
    {
        hitboxesOpen = false;
        if (animationDriver != null)
            animationDriver.DisableHitboxes();
    }

    private void SetAttackVisual(BossActionId actionId, bool value)
    {
        if (animationDriver == null)
            return;

        animationDriver.ClearActionBools();

        if (!value)
            return;

        switch (actionId)
        {
            case BossActionId.AttackOneHand:
                animationDriver.SetAttackOneHand(true);
                break;
            case BossActionId.AttackTwoHand:
                animationDriver.SetAttackTwoHand(true);
                break;
            case BossActionId.AttackSpin:
                animationDriver.SetAttackSpin(true);
                break;
            case BossActionId.PunchLeft:
                animationDriver.SetPunchLeft(true);
                break;
        }
    }

    private void Move(Vector3 worldDirection, float speed, bool forceWhileBusy = false)
    {
        if (IsLocked())
            return;

        if (!forceWhileBusy && IsBusy())
            return;

        if (worldDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 targetPosition = transform.position + worldDirection.normalized * speed * Time.deltaTime;

        if (clampToArena)
            targetPosition = ClampPositionToArena(targetPosition);

        transform.position = targetPosition;
    }

    private Vector3 ClampPositionToArena(Vector3 position)
    {
        if (arenaCenter == null || arenaRadius <= 0f)
            return position;

        Vector3 center = arenaCenter.position;
        Vector3 flatOffset = position - center;
        flatOffset.y = 0f;

        if (flatOffset.magnitude <= arenaRadius)
            return position;

        Vector3 clampedFlat = flatOffset.normalized * arenaRadius;
        return new Vector3(center.x + clampedFlat.x, position.y, center.z + clampedFlat.z);
    }

    private void FacePlayerSmooth(float speed)
    {
        if (player == null)
            return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, speed * Time.deltaTime);
    }

    private void FacePlayerInstant()
    {
        if (player == null)
            return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f)
            return;

        transform.rotation = Quaternion.LookRotation(dir.normalized);
    }

    private void SetMoveState(int state)
    {
        if (animationDriver != null)
            animationDriver.SetMoveState(state);
    }


    private void RegisterBossMovementAction(BossActionId actionId)
    {
        if (actionId == BossActionId.Idle)
        {
            lastLoggedDiscreteAction = BossActionId.Idle;
            return;
        }

        if (lastLoggedDiscreteAction == actionId)
            return;

        lastLoggedDiscreteAction = actionId;

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterBossAction(GetMetricMoveName(actionId));
    }

    private string GetMetricMoveName(BossActionId actionId)
    {
        return actionId switch
        {
            BossActionId.WalkForward => "WalkForward",
            BossActionId.RunForward => "RunForward",
            BossActionId.Sprint => "Sprint",
            BossActionId.WalkBackward => "WalkBackward",
            BossActionId.StrafeLeft => "StrafeLeft",
            BossActionId.StrafeRight => "StrafeRight",
            BossActionId.WalkForwardLeft => "WalkForwardLeft",
            BossActionId.WalkForwardRight => "WalkForwardRight",
            BossActionId.RollBackward => "RollBackward",
            _ => actionId.ToString()
        };
    }

    private void DecayAttackPressure()
    {
        if (Time.time >= lastAttackEndTime + 0.15f)
            lastStartedAttackAction = BossActionId.Idle;
    }

    private string GetMetricAttackName(BossActionId actionId)
    {
        return actionId switch
        {
            BossActionId.AttackOneHand => "AttackOneHand",
            BossActionId.AttackTwoHand => "AttackTwoHand",
            BossActionId.AttackSpin => "AttackSpin",
            BossActionId.PunchLeft => "PunchLeft",
            _ => "Unknown"
        };
    }

    public float GetFlatDistanceToPlayer()
    {
        if (player == null)
            return 999f;

        Vector3 a = transform.position;
        Vector3 b = player.position;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public float GetSignedAngleToPlayerNormalized()
    {
        if (player == null)
            return 0f;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude < 0.001f)
            return 0f;

        float angle = Vector3.SignedAngle(transform.forward, toPlayer.normalized, Vector3.up);
        return Mathf.Clamp(angle / 180f, -1f, 1f);
    }

    public float GetForwardDotToPlayer()
    {
        if (player == null)
            return 0f;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude < 0.001f)
            return 1f;

        return Vector3.Dot(transform.forward, toPlayer.normalized);
    }

    public float GetArenaDistanceNormalized()
    {
        if (arenaCenter == null || arenaRadius <= 0f)
            return 0f;

        Vector3 offset = transform.position - arenaCenter.position;
        offset.y = 0f;
        return Mathf.Clamp01(offset.magnitude / arenaRadius);
    }

    public bool CanAnyAttackNow()
    {
        return !IsLocked() && !IsBusy() && Time.time >= nextAttackTime && Time.time >= movementLockUntil;
    }

    public bool CanRollNow()
    {
        return !IsLocked() && !IsBusy() && Time.time >= nextRollTime && Time.time >= movementLockUntil;
    }

    public bool IsPlayerInAttackRange()
    {
        return IsPlayerInRange(attackTwoHand != null ? attackTwoHand.range : 3.85f);
    }

    public bool IsPlayerInRange(float range)
    {
        return GetFlatDistanceToPlayer() <= range;
    }

    public bool IsPlayerInOneHandRange() => IsPlayerInRange(attackOneHand != null ? attackOneHand.range : 3.1f);
    public bool IsPlayerInTwoHandRange() => IsPlayerInRange(attackTwoHand != null ? attackTwoHand.range : 3.85f);
    public bool IsPlayerInSpinRange() => IsPlayerInRange(attackSpin != null ? attackSpin.range : 3.9f);
    public bool IsPlayerInPunchRange() => IsPlayerInRange(punchLeft != null ? punchLeft.range : 1.9f);

    public float GetAttackCooldownNormalized()
    {
        float remaining = Mathf.Max(0f, nextAttackTime - Time.time);
        float reference = 1.35f;

        if (attackOneHand != null) reference = Mathf.Max(reference, attackOneHand.cooldown + globalAttackCooldownBonus);
        if (attackTwoHand != null) reference = Mathf.Max(reference, attackTwoHand.cooldown + globalAttackCooldownBonus);
        if (attackSpin != null) reference = Mathf.Max(reference, attackSpin.cooldown + globalAttackCooldownBonus);
        if (punchLeft != null) reference = Mathf.Max(reference, punchLeft.cooldown + globalAttackCooldownBonus);

        return Mathf.Clamp01(remaining / Mathf.Max(0.01f, reference));
    }

    public float GetRollCooldownNormalized()
    {
        float remaining = Mathf.Max(0f, nextRollTime - Time.time);
        return Mathf.Clamp01(remaining / Mathf.Max(0.01f, rollBackward.cooldown));
    }

    public float GetBusyNormalized() => IsBusy() ? 1f : 0f;
    public float GetRollingNormalized() => IsRolling() ? 1f : 0f;

    public float GetActionPhaseNormalized()
    {
        if (!IsBusy() || currentBusyDuration <= 0f)
            return 0f;

        return Mathf.Clamp01(busyTimer / currentBusyDuration);
    }

    public float GetCurrentActionTypeNormalized()
    {
        int value = currentBusyAction == BossActionId.Idle ? 0 : (int)currentBusyAction;
        return Mathf.Clamp01(value / 13f);
    }

    public float GetTimeSinceLastAttackNormalized()
    {
        float elapsed = Time.time - lastAttackStartTime;
        return Mathf.Clamp01(elapsed / 2.2f);
    }

    public float GetTimeSinceLastSuccessfulHitNormalized()
    {
        float elapsed = Time.time - lastSuccessfulHitTime;
        return Mathf.Clamp01(elapsed / 2.4f);
    }

    public float GetPostAttackMovementLockNormalized()
    {
        float remaining = Mathf.Max(0f, movementLockUntil - Time.time);
        return Mathf.Clamp01(remaining / Mathf.Max(0.01f, postAttackMovementGrace));
    }
}
