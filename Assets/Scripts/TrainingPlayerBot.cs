using UnityEngine;

public class TrainingPlayerBot : MonoBehaviour
{
    public enum CombatProfile
    {
        Balanced,
        Aggressive,
        Evasive,
        Passive
    }

    public enum State
    {
        Neutral,
        Approach,
        CircleLeft,
        CircleRight,
        Backstep,
        WeakAttack,
        StrongAttack,
        Roll
    }

    [System.Serializable]
    public class AttackSpec
    {
        public int damage = 10;
        public float range = 2.0f;
        public float cooldown = 0.9f;
        public float windup = 0.16f;
        public float activeDuration = 0.12f;
        public float recovery = 0.22f;
        public float hitMoment = 0.17f;
        public float lungeSpeed = 1.0f;
        public float minFacingDot = 0.60f;
    }

    [System.Serializable]
    public class RollSpec
    {
        public float cooldown = 2.0f;
        public float duration = 0.55f;
        public float speed = 4.4f;
    }

    public Transform boss;
    public BossHealth bossHealth;
    public BossRLAgent bossAgent;
    public TrainingPlayerHealth playerHealth;
    public TrainingPlayerAnimationDriver animationDriver;
    public Transform attackOrigin;
    public TrainingPlayerWeaponHitbox[] weaponHitboxes;

    [Header("Disable On Surrogate Copy")]
    public Behaviour[] componentsToDisable;

    [Header("Motion")]
    public CharacterController characterController;
    public Rigidbody playerRigidbody;

    [Header("Movement Speeds")]
    public float walkSpeed = 1.8f;
    public float runSpeed = 2.7f;
    public float sprintSpeed = 3.7f;
    public float backstepSpeed = 2.1f;
    public float strafeSpeed = 2.8f;
    public float diagonalSpeed = 2.35f;
    public float rotateSpeed = 8f;
    public float combatRotateSpeed = 10f;

    [Header("Spacing")]
    public float preferredDistanceMin = 1.45f;
    public float preferredDistanceMax = 2.05f;
    public float tooCloseDistance = 1.10f;
    public float pressureDistance = 2.65f;

    [Header("Decision Timing")]
    public float decisionDurationMin = 0.18f;
    public float decisionDurationMax = 0.42f;
    public float idleDecisionChance = 0.04f;
    public float passiveProfileChance = 0.18f;

    [Header("Attack Chances")]
    public float weakAttackChance = 0.82f;
    public float strongAttackChance = 0.14f;
    public float rollChanceWhenCrowded = 0.18f;

    [Header("Attack Control")]
    public float weakAttackCommitRange = 1.55f;
    public float strongAttackCommitRange = 1.75f;
    public float comboDistance = 1.85f;
    public float comboGraceTime = 0.55f;
    [Range(0f, 1f)] public float comboWeakChance = 0.62f;
    [Range(0f, 1f)] public float comboStrongChance = 0.18f;
    [Range(0f, 1f)] public float chainFromStrongToWeakChance = 0.35f;

    [Header("Arena")]
    public Transform arenaCenter;
    public float arenaRadius = 12f;
    public bool clampToArena = true;

    [Header("Weak Attack")]
    public AttackSpec weakAttack = new AttackSpec
    {
        damage = 3,
        range = 1.55f,
        cooldown = 0.58f,
        windup = 0.11f,
        activeDuration = 0.14f,
        recovery = 0.15f,
        hitMoment = 0.12f,
        lungeSpeed = 0.95f,
        minFacingDot = 0.58f
    };

    [Header("Strong Attack")]
    public AttackSpec strongAttack = new AttackSpec
    {
        damage = 5,
        range = 1.78f,
        cooldown = 0.95f,
        windup = 0.18f,
        activeDuration = 0.16f,
        recovery = 0.24f,
        hitMoment = 0.19f,
        lungeSpeed = 1.12f,
        minFacingDot = 0.62f
    };

    [Header("Roll")]
    public RollSpec roll = new RollSpec();

    [Header("Debug")]
    public CombatProfile currentProfile = CombatProfile.Balanced;
    public State currentState = State.Neutral;
    public bool forceCodeDefaults = true;
    public bool debugAttackDecisions = false;
    public bool debugHitConfirm = false;

    private float stateTimer;
    private float nextWeakAttackTime;
    private float nextStrongAttackTime;
    private float nextRollTime;
    private Vector3 desiredLocalMove;
    private float desiredSpeed;
    private bool hitboxWindowOpen;
    private bool currentAttackConnected;
    private float lastConfirmedHitTime = -999f;
    private State lastAttackState = State.Neutral;
    private int confirmedHitCount;
    private CombatMetricsCollector metricsCollector;

    private void ApplyForceCodeDefaults()
    {
        weakAttack.damage = 3;
        weakAttack.range = 1.55f;
        weakAttack.cooldown = 0.58f;
        weakAttack.windup = 0.11f;
        weakAttack.activeDuration = 0.14f;
        weakAttack.recovery = 0.15f;
        weakAttack.hitMoment = 0.12f;
        weakAttack.lungeSpeed = 0.95f;
        weakAttack.minFacingDot = 0.58f;

        strongAttack.damage = 5;
        strongAttack.range = 1.78f;
        strongAttack.cooldown = 0.95f;
        strongAttack.windup = 0.18f;
        strongAttack.activeDuration = 0.16f;
        strongAttack.recovery = 0.24f;
        strongAttack.hitMoment = 0.19f;
        strongAttack.lungeSpeed = 1.12f;
        strongAttack.minFacingDot = 0.62f;

        roll.cooldown = 1.95f;
        roll.duration = 0.52f;
        roll.speed = 4.4f;
    }

    private void Awake()
    {
        if (animationDriver == null)
            animationDriver = GetComponent<TrainingPlayerAnimationDriver>();

        if (playerHealth == null)
            playerHealth = GetComponent<TrainingPlayerHealth>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (playerRigidbody == null)
            playerRigidbody = GetComponent<Rigidbody>();

        if (forceCodeDefaults)
            ApplyForceCodeDefaults();

        metricsCollector = CombatMetricsCollector.Instance;

        DisableOriginalComponents();
        WireWeaponHitboxes();
    }

    private void Start()
    {
        if (attackOrigin == null)
            attackOrigin = transform;

        ResetBot();
    }

    private void Update()
    {
        if (boss == null || bossHealth == null)
        {
            SetNeutralMove();
            CloseWeaponWindow();
            return;
        }

        if ((playerHealth != null && playerHealth.IsDead) || bossHealth.IsDead)
        {
            SetNeutralMove();
            CloseWeaponWindow();
            return;
        }

        FaceBossSmooth(IsBusyState(currentState) ? combatRotateSpeed : rotateSpeed);

        if (IsBusyState(currentState))
        {
            TickBusyState();
            return;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            ChooseNewState();

        TickLocomotionState();
    }

    public void ResetBot()
    {
        PickProfile();
        currentState = State.Neutral;
        stateTimer = 0f;
        nextWeakAttackTime = 0f;
        nextStrongAttackTime = 0f;
        nextRollTime = 0f;
        hitboxWindowOpen = false;
        currentAttackConnected = false;
        lastConfirmedHitTime = -999f;
        lastAttackState = State.Neutral;
        confirmedHitCount = 0;
        SetNeutralMove();
        CloseWeaponWindow();

        if (playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }

        if (animationDriver != null)
            animationDriver.ResetVisualState();
    }

    public void NotifyWeaponConnected(int damage)
    {
        NotifyWeaponConnected(damage, bossHealth != null ? bossHealth.currentHP + damage : 0, bossHealth != null ? bossHealth.currentHP : 0, "UnknownHitbox");
    }

    public void NotifyWeaponConnected(int damage, int hpBefore, int hpAfter, string hitboxName)
    {
        bool confirmed = hpAfter < hpBefore;

        if (confirmed)
        {
            currentAttackConnected = true;
            lastConfirmedHitTime = Time.time;
            confirmedHitCount++;
        }

        if (debugHitConfirm)
        {
            Debug.Log(
                $"[TrainingPlayerBot] HIT_CHECK attackState={currentState} hitbox={hitboxName} dmg={damage} bossHP={hpBefore}->{hpAfter} confirmed={confirmed} confirmedHits={confirmedHitCount} frame={Time.frameCount}",
                this);
        }

        if (confirmed && bossAgent != null)
            bossAgent.NotifyTookDamage(damage);

        if (confirmed)
        {
            if (metricsCollector == null)
                metricsCollector = CombatMetricsCollector.Instance;

            if (metricsCollector != null)
                metricsCollector.RegisterPlayerAttackHit(currentState == State.StrongAttack ? "StrongAttack" : "WeakAttack", damage);
        }
    }

    private void WireWeaponHitboxes()
    {
        if (weaponHitboxes == null)
            return;

        for (int i = 0; i < weaponHitboxes.Length; i++)
        {
            if (weaponHitboxes[i] == null)
                continue;

            weaponHitboxes[i].owner = this;
            weaponHitboxes[i].fallbackBossHealth = bossHealth;
            weaponHitboxes[i].ForceDisable();
        }
    }

    private void ChooseNewState()
    {
        float distance = GetFlatDistanceToBoss();
        float facingDot = GetFacingDotToBoss();
        bool tooClose = distance < GetTooCloseDistance();
        bool tooFar = distance > GetPreferredMax();

        if (tooClose && CanRollNow() && Random.value < GetRollChance())
        {
            BeginRoll();
            return;
        }

        if (TryStartAttackDecision(distance, facingDot))
            return;

        float duration = Random.Range(decisionDurationMin, decisionDurationMax);
        float idleChance = GetIdleDecisionChance();

        if (tooFar)
        {
            float r = Random.value;

            if (currentProfile == CombatProfile.Passive)
                currentState = r < 0.44f ? State.Approach : (r < 0.74f ? State.CircleLeft : State.CircleRight);
            else
                currentState = r < 0.62f ? State.Approach : (r < 0.81f ? State.CircleLeft : State.CircleRight);

            stateTimer = duration;
            RegisterPlayerMovementState(currentState);
            return;
        }

        if (tooClose)
        {
            float r = Random.value;

            if (currentProfile == CombatProfile.Passive)
                currentState = r < 0.28f ? State.Backstep : (r < 0.64f ? State.CircleLeft : State.CircleRight);
            else
                currentState = r < 0.40f ? State.Backstep : (r < 0.70f ? State.CircleLeft : State.CircleRight);

            stateTimer = duration;
            RegisterPlayerMovementState(currentState);
            return;
        }

        float choice = Random.value;
        if (choice < idleChance)
        {
            currentState = State.Neutral;
        }
        else if (currentProfile == CombatProfile.Passive)
        {
            if (choice < idleChance + 0.32f)
                currentState = State.CircleLeft;
            else if (choice < idleChance + 0.64f)
                currentState = State.CircleRight;
            else if (choice < idleChance + 0.80f)
                currentState = State.Backstep;
            else
                currentState = State.Approach;
        }
        else
        {
            if (choice < 0.34f)
                currentState = State.CircleLeft;
            else if (choice < 0.68f)
                currentState = State.CircleRight;
            else if (choice < 0.82f)
                currentState = State.Backstep;
            else
                currentState = State.Approach;
        }

        stateTimer = duration;
        RegisterPlayerMovementState(currentState);
    }

    private bool TryStartAttackDecision(float distance, float facingDot)
    {
        bool recentHit = Time.time <= lastConfirmedHitTime + comboGraceTime;
        bool weakReady = CanWeakAttackNow() && CanStartAttack(weakAttack, weakAttackCommitRange, distance, facingDot);
        bool strongReady = CanStrongAttackNow() && CanStartAttack(strongAttack, strongAttackCommitRange, distance, facingDot);

        if (recentHit)
        {
            if (lastAttackState == State.WeakAttack)
            {
                if (weakReady && Random.value < comboWeakChance)
                {
                    BeginWeakAttack();
                    return true;
                }

                if (strongReady && distance <= strongAttackCommitRange && Random.value < comboStrongChance)
                {
                    BeginStrongAttack();
                    return true;
                }
            }
            else if (lastAttackState == State.StrongAttack)
            {
                if (weakReady && Random.value < chainFromStrongToWeakChance)
                {
                    BeginWeakAttack();
                    return true;
                }
            }
        }

        if (weakReady && distance <= weakAttackCommitRange)
        {
            float weakChance = GetWeakAttackChance(distance);
            if (Random.value < weakChance)
            {
                BeginWeakAttack();
                return true;
            }
        }

        if (strongReady && distance <= strongAttackCommitRange)
        {
            float strongChance = GetStrongAttackChance(distance);
            bool idealStrongWindow = distance >= weakAttackCommitRange * 0.85f || recentHit;

            if (idealStrongWindow && Random.value < strongChance)
            {
                BeginStrongAttack();
                return true;
            }
        }

        return false;
    }

    private void TickLocomotionState()
    {
        switch (currentState)
        {
            case State.Neutral:
                SetNeutralMove();
                break;

            case State.Approach:
                SetLocalMove(GetApproachMove(), GetApproachSpeed());
                break;

            case State.CircleLeft:
                SetLocalMove(GetCircleMove(-1f), strafeSpeed);
                break;

            case State.CircleRight:
                SetLocalMove(GetCircleMove(1f), strafeSpeed);
                break;

            case State.Backstep:
                SetLocalMove(new Vector3(Random.Range(-0.18f, 0.18f), 0f, -1f), backstepSpeed);
                break;
        }

        ApplyMove();
    }

    private void BeginWeakAttack()
    {
        if (animationDriver == null || !animationDriver.PlayWeakAttack())
            return;

        currentState = State.WeakAttack;
        stateTimer = CurrentAttackTotalDurationFor(weakAttack);
        SetNeutralMove();
        nextWeakAttackTime = Time.time + weakAttack.cooldown;
        currentAttackConnected = false;
        CloseWeaponWindow();

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterPlayerAttackAttempt("WeakAttack");

        if (debugAttackDecisions)
        {
            Debug.Log($"[TrainingPlayerBot] BEGIN WeakAttack dist={GetFlatDistanceToBoss():F2} dot={GetFacingDotToBoss():F2} frame={Time.frameCount}", this);
        }
    }

    private void BeginStrongAttack()
    {
        if (animationDriver == null || !animationDriver.PlayStrongAttack())
            return;

        currentState = State.StrongAttack;
        stateTimer = CurrentAttackTotalDurationFor(strongAttack);
        SetNeutralMove();
        nextStrongAttackTime = Time.time + strongAttack.cooldown;
        currentAttackConnected = false;
        CloseWeaponWindow();

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterPlayerAttackAttempt("StrongAttack");

        if (debugAttackDecisions)
        {
            Debug.Log($"[TrainingPlayerBot] BEGIN StrongAttack dist={GetFlatDistanceToBoss():F2} dot={GetFacingDotToBoss():F2} frame={Time.frameCount}", this);
        }
    }

    private void BeginRoll()
    {
        if (animationDriver != null)
            animationDriver.PlayRoll();

        currentState = State.Roll;
        stateTimer = roll.duration;
        nextRollTime = Time.time + roll.cooldown;
        CloseWeaponWindow();
        SetNeutralMove();

        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector != null)
            metricsCollector.RegisterPlayerAction("Roll");
    }

    private void TickBusyState()
    {
        stateTimer -= Time.deltaTime;

        switch (currentState)
        {
            case State.WeakAttack:
                TickAttackState(weakAttack);
                break;
            case State.StrongAttack:
                TickAttackState(strongAttack);
                break;
            case State.Roll:
                SetLocalMove(new Vector3(0f, 0f, -1f), roll.speed);
                ApplyMove();
                break;
        }

        if (stateTimer <= 0f)
        {
            State finishedState = currentState;

            if ((finishedState == State.WeakAttack || finishedState == State.StrongAttack) && !currentAttackConnected)
            {
                if (metricsCollector == null)
                    metricsCollector = CombatMetricsCollector.Instance;

                if (metricsCollector != null)
                    metricsCollector.RegisterPlayerAttackMiss(finishedState == State.StrongAttack ? "StrongAttack" : "WeakAttack");
            }

            if ((finishedState == State.WeakAttack || finishedState == State.StrongAttack) && TryContinueCombo(finishedState))
                return;

            lastAttackState = finishedState;
            currentState = State.Neutral;
            CloseWeaponWindow();
            SetNeutralMove();
        }
    }

    private bool TryContinueCombo(State finishedState)
    {
        float distance = GetFlatDistanceToBoss();
        float facingDot = GetFacingDotToBoss();
        bool recentHit = currentAttackConnected || Time.time <= lastConfirmedHitTime + comboGraceTime;

        if (!recentHit || distance > comboDistance)
            return false;

        if (finishedState == State.WeakAttack)
        {
            if (CanWeakAttackNow() && CanStartAttack(weakAttack, weakAttackCommitRange, distance, facingDot) && Random.value < comboWeakChance)
            {
                BeginWeakAttack();
                return true;
            }

            if (CanStrongAttackNow() && CanStartAttack(strongAttack, strongAttackCommitRange, distance, facingDot) && Random.value < comboStrongChance)
            {
                BeginStrongAttack();
                return true;
            }
        }
        else if (finishedState == State.StrongAttack)
        {
            if (CanWeakAttackNow() && CanStartAttack(weakAttack, weakAttackCommitRange, distance, facingDot) && Random.value < chainFromStrongToWeakChance)
            {
                BeginWeakAttack();
                return true;
            }
        }

        return false;
    }

    private void TickAttackState(AttackSpec spec)
    {
        float elapsed = CurrentAttackTotalDurationFor(spec) - stateTimer;

        if (elapsed < spec.hitMoment)
        {
            SetLocalMove(new Vector3(0f, 0f, 1f), spec.lungeSpeed);
            ApplyMove();
        }
        else
        {
            SetNeutralMove();
        }

        float activeStart = spec.hitMoment;
        float activeEnd = spec.hitMoment + spec.activeDuration;
        bool shouldBeOpen = elapsed >= activeStart && elapsed <= activeEnd;

        if (shouldBeOpen && !hitboxWindowOpen)
            OpenWeaponWindow(spec.damage);
        else if (!shouldBeOpen && hitboxWindowOpen)
            CloseWeaponWindow();
    }

    private void OpenWeaponWindow(int damage)
    {
        hitboxWindowOpen = true;

        if (weaponHitboxes == null)
            return;

        for (int i = 0; i < weaponHitboxes.Length; i++)
        {
            if (weaponHitboxes[i] != null)
                weaponHitboxes[i].OpenWindow(damage);
        }
    }

    private void CloseWeaponWindow()
    {
        hitboxWindowOpen = false;

        if (weaponHitboxes == null)
            return;

        for (int i = 0; i < weaponHitboxes.Length; i++)
        {
            if (weaponHitboxes[i] != null)
                weaponHitboxes[i].CloseWindow();
        }
    }

    private void ApplyMove()
    {
        Vector3 local = desiredLocalMove;
        local.y = 0f;

        if (desiredSpeed <= 0.001f || local.sqrMagnitude < 0.001f)
        {
            SetAnimatorMove(Vector3.zero, 0f, false);
            return;
        }

        Vector3 world = transform.TransformDirection(local.normalized);
        MoveWorld(world, desiredSpeed);

        bool sprinting = desiredSpeed >= runSpeed - 0.05f && local.z > 0.55f;
        SetAnimatorMove(local.normalized, Mathf.Clamp01(desiredSpeed / sprintSpeed), sprinting);
    }

    private void SetNeutralMove()
    {
        desiredLocalMove = Vector3.zero;
        desiredSpeed = 0f;
        SetAnimatorMove(Vector3.zero, 0f, false);
    }

    private void SetLocalMove(Vector3 localDirection, float speed)
    {
        desiredLocalMove = localDirection;
        desiredSpeed = speed;
    }

    private void SetAnimatorMove(Vector3 localNormalized, float magnitude, bool sprinting)
    {
        if (animationDriver == null)
            return;

        bool strafing = Mathf.Abs(localNormalized.x) > 0.22f;
        animationDriver.SetMovement(localNormalized.x, localNormalized.z, magnitude, true, strafing, sprinting);
    }

    private void MoveWorld(Vector3 worldDirection, float speed)
    {
        Vector3 delta = worldDirection * speed * Time.deltaTime;

        if (characterController != null && characterController.enabled)
            characterController.Move(delta);
        else if (playerRigidbody != null && !playerRigidbody.isKinematic)
            playerRigidbody.MovePosition(playerRigidbody.position + delta);
        else
            transform.position += delta;

        ClampArena();
    }

    private void FaceBossSmooth(float speed)
    {
        if (boss == null)
            return;

        Vector3 dir = boss.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            return;

        Quaternion target = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, speed * Time.deltaTime);
    }

    private bool CanWeakAttackNow() => Time.time >= nextWeakAttackTime;
    private bool CanStrongAttackNow() => Time.time >= nextStrongAttackTime;
    private bool CanRollNow() => Time.time >= nextRollTime;

    private bool CanStartAttack(AttackSpec spec, float commitRange, float distance, float facingDot)
    {
        if (spec == null)
            return false;

        float requiredRange = Mathf.Min(spec.range, commitRange);
        return distance <= requiredRange && facingDot >= spec.minFacingDot;
    }

    private float GetFacingDotToBoss()
    {
        if (boss == null)
            return 0f;

        Vector3 toBoss = boss.position - transform.position;
        toBoss.y = 0f;
        if (toBoss.sqrMagnitude < 0.001f)
            return 1f;

        return Vector3.Dot(transform.forward, toBoss.normalized);
    }

    private float GetStrongAttackChance(float distance)
    {
        float chance = strongAttackChance;
        if (currentProfile == CombatProfile.Aggressive) chance += 0.03f;
        if (currentProfile == CombatProfile.Evasive) chance -= 0.05f;
        if (currentProfile == CombatProfile.Passive) chance -= 0.10f;
        if (distance > weakAttackCommitRange) chance += 0.02f;
        if (Time.time <= lastConfirmedHitTime + comboGraceTime) chance += 0.06f;
        return Mathf.Clamp01(chance);
    }

    private float GetWeakAttackChance(float distance)
    {
        float chance = weakAttackChance;
        if (currentProfile == CombatProfile.Aggressive) chance += 0.08f;
        if (currentProfile == CombatProfile.Evasive) chance -= 0.05f;
        if (currentProfile == CombatProfile.Passive) chance -= 0.16f;
        if (distance <= weakAttackCommitRange) chance += 0.08f;
        return Mathf.Clamp01(chance);
    }

    private float GetRollChance()
    {
        float chance = rollChanceWhenCrowded;
        if (currentProfile == CombatProfile.Evasive) chance += 0.10f;
        if (currentProfile == CombatProfile.Aggressive) chance -= 0.08f;
        if (currentProfile == CombatProfile.Passive) chance += 0.04f;
        return Mathf.Clamp01(chance);
    }

    private float GetPreferredMax()
    {
        return currentProfile switch
        {
            CombatProfile.Aggressive => preferredDistanceMax - 0.10f,
            CombatProfile.Evasive => preferredDistanceMax + 0.18f,
            CombatProfile.Passive => preferredDistanceMax + 0.36f,
            _ => preferredDistanceMax,
        };
    }

    private float GetTooCloseDistance()
    {
        return currentProfile switch
        {
            CombatProfile.Aggressive => tooCloseDistance - 0.05f,
            CombatProfile.Evasive => tooCloseDistance + 0.10f,
            CombatProfile.Passive => tooCloseDistance + 0.14f,
            _ => tooCloseDistance,
        };
    }

    private float GetFlatDistanceToBoss()
    {
        if (boss == null)
            return 999f;

        Vector3 d = boss.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    private Vector3 GetApproachMove()
    {
        float distance = GetFlatDistanceToBoss();
        if (distance > pressureDistance)
        {
            float r = Random.value;

            if (currentProfile == CombatProfile.Passive)
            {
                if (r < 0.42f) return new Vector3(0f, 0f, 1f);
                if (r < 0.71f) return new Vector3(-0.28f, 0f, 0.92f);
                return new Vector3(0.28f, 0f, 0.92f);
            }

            if (r < 0.50f) return new Vector3(0f, 0f, 1f);
            if (r < 0.75f) return new Vector3(-0.35f, 0f, 1f);
            return new Vector3(0.35f, 0f, 1f);
        }

        float nearR = Random.value;
        if (currentProfile == CombatProfile.Passive)
        {
            if (nearR < 0.30f) return new Vector3(0f, 0f, 1f);
            if (nearR < 0.65f) return new Vector3(-0.52f, 0f, 0.78f);
            return new Vector3(0.52f, 0f, 0.78f);
        }

        if (nearR < 0.45f) return new Vector3(0f, 0f, 1f);
        if (nearR < 0.72f) return new Vector3(-0.45f, 0f, 0.92f);
        return new Vector3(0.45f, 0f, 0.92f);
    }

    private float GetApproachSpeed()
    {
        float distance = GetFlatDistanceToBoss();

        if (currentProfile == CombatProfile.Passive)
        {
            if (distance > pressureDistance)
                return Random.value < 0.62f ? runSpeed : walkSpeed;

            return Random.value < 0.72f ? walkSpeed : runSpeed;
        }

        if (distance > pressureDistance)
            return Random.value < 0.55f ? sprintSpeed : runSpeed;

        return Random.value < 0.72f ? runSpeed : walkSpeed;
    }

    private Vector3 GetCircleMove(float lateralSign)
    {
        float zBias = 0f;
        if (currentProfile == CombatProfile.Aggressive)
            zBias = 0.08f;
        else if (currentProfile == CombatProfile.Evasive)
            zBias = -0.10f;

        return new Vector3(lateralSign, 0f, zBias);
    }


    private void RegisterPlayerMovementState(State state)
    {
        if (metricsCollector == null)
            metricsCollector = CombatMetricsCollector.Instance;

        if (metricsCollector == null)
            return;

        switch (state)
        {
            case State.Approach:
                metricsCollector.RegisterPlayerAction("Approach");
                break;
            case State.CircleLeft:
                metricsCollector.RegisterPlayerAction("CircleLeft");
                break;
            case State.CircleRight:
                metricsCollector.RegisterPlayerAction("CircleRight");
                break;
            case State.Backstep:
                metricsCollector.RegisterPlayerAction("Backstep");
                break;
        }
    }

    private bool IsBusyState(State state)
    {
        return state == State.WeakAttack || state == State.StrongAttack || state == State.Roll;
    }

    private float CurrentAttackTotalDurationFor(AttackSpec spec)
    {
        return spec.windup + spec.activeDuration + spec.recovery;
    }

    private float GetIdleDecisionChance()
    {
        return currentProfile switch
        {
            CombatProfile.Aggressive => Mathf.Max(0.01f, idleDecisionChance - 0.02f),
            CombatProfile.Passive => idleDecisionChance + 0.10f,
            _ => idleDecisionChance,
        };
    }

    private void PickProfile()
    {
        float r = Random.value;
        float passiveCutoff = Mathf.Clamp01(passiveProfileChance);

        if (r < passiveCutoff)
            currentProfile = CombatProfile.Passive;
        else if (r < passiveCutoff + 0.34f)
            currentProfile = CombatProfile.Balanced;
        else if (r < passiveCutoff + 0.64f)
            currentProfile = CombatProfile.Aggressive;
        else
            currentProfile = CombatProfile.Evasive;
    }

    private void ClampArena()
    {
        if (!clampToArena || arenaCenter == null)
            return;

        Vector3 offset = transform.position - arenaCenter.position;
        offset.y = 0f;
        if (offset.magnitude <= arenaRadius)
            return;

        Vector3 clamped = arenaCenter.position + offset.normalized * arenaRadius;
        transform.position = new Vector3(clamped.x, transform.position.y, clamped.z);
    }

    private void DisableOriginalComponents()
    {
        if (componentsToDisable == null)
            return;

        for (int i = 0; i < componentsToDisable.Length; i++)
        {
            if (componentsToDisable[i] != null)
                componentsToDisable[i].enabled = false;
        }
    }
}
