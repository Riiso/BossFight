using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class BossRLAgent : Agent
{
    private enum TerminalState
    {
        None,
        BossWon,
        BossLost,
        Timeout
    }

    public Transform player;
    public BossCombatExecutor executor;
    public BossEpisodeManager episodeManager;
    public BossHealth bossHealth;
    public TrainingPlayerHealth trainingPlayerHealth;

    [Header("Episode")]
    public float maxDistance = 15f;
    public float episodeTimeLimit = 60f;
    public float terminalDelay = 3f;

    [Header("Manual Debug")]
    public KeyCode manualEpisodeResetKey = KeyCode.F6;

    [Header("Distance Bands")]
    public float tooCloseDistance = 1.30f;
    public float idealDistanceMin = 2.00f;
    public float idealDistanceMax = 3.00f;
    public float tooFarDistance = 6.20f;
    public float pressureDistance = 3.35f;

    [Header("Reward Weights")]
    public float dealtDamageRewardScale = 0.10f;
    public float tookDamagePenaltyScale = 0.045f;
    public float missPenalty = -0.010f;
    public float repeatedMissPenalty = -0.006f;
    public float stepPenalty = -0.00045f;
    public float timeoutPenalty = -2.5f;
    public float winReward = 5.0f;
    public float lossPenalty = -4.0f;

    [Header("Aggression Shaping")]
    public float attackStartGoodReward = 0.060f;
    public float attackStartBadReward = -0.015f;
    public float canAttackButDidNotPenalty = -0.0025f;
    public float idleNearPenalty = -0.0050f;
    public float backwardNearPenalty = -0.0060f;
    public float rollNearPenalty = -0.0090f;
    public float prolongedNoAttackPenalty = -0.0030f;
    public float prolongedNoAttackDelay = 0.85f;
    public float approachRewardScale = 0.011f;
    public float facePlayerReward = 0.0030f;

    [Header("Attack Rhythm Shaping")]
    public float rapidRepeatWindow = 0.95f;
    public float rapidRepeatAttackPenalty = -0.010f;
    public float sameAttackRepeatPenalty = -0.005f;
    public float stalePressureAttackPenalty = -0.006f;
    public float movementAfterAttackReward = 0.0035f;
    public float repositionNearReward = 0.0035f;
    public float attackTypeSwitchReward = 0.007f;

    [Header("Passive Target Shaping")]
    public float passiveTargetSpeedThreshold = 0.35f;
    public float passiveTargetWindow = 0.55f;
    public float stationaryTargetApproachReward = 0.0065f;
    public float stallVsPassivePenalty = -0.0035f;
    public float holdPressureVsPassiveReward = 0.0025f;
    public float spinWindowBonus = 0.0040f;
    public float punchWindowBonus = 0.0050f;

    [Range(0f, 1f)] public float bossHpNormalized = 1f;
    [Range(0f, 1f)] public float playerHpNormalized = 1f;

    private float episodeTimer = 0f;
    private float terminalTimer = 0f;
    private TerminalState terminalState = TerminalState.None;
    private Vector3 previousPlayerFlatPos;
    private bool hasPreviousPlayerPos = false;
    private float currentPlayerFlatSpeed = 0f;
    private float passiveTargetTimer = 0f;
    private float lastDistanceToPlayer = -1f;
    private float timeNearPlayerWithoutAttack = 0f;

    private int lastActionReceived = -1;
    private int consecutiveAttackDecisions = 0;
    private int consecutiveMisses = 0;
    private float lastAttackDecisionTime = -999f;
    private float lastSuccessfulHitTime = -999f;

    public override void OnEpisodeBegin()
    {
        episodeTimer = 0f;
        terminalTimer = 0f;
        terminalState = TerminalState.None;
        hasPreviousPlayerPos = false;
        currentPlayerFlatSpeed = 0f;
        passiveTargetTimer = 0f;
        lastDistanceToPlayer = -1f;
        timeNearPlayerWithoutAttack = 0f;
        lastActionReceived = -1;
        consecutiveAttackDecisions = 0;
        consecutiveMisses = 0;
        lastAttackDecisionTime = -999f;
        lastSuccessfulHitTime = -999f;

        if (episodeManager != null)
            episodeManager.ResetEpisode();

        SyncHealthFromScene();
        CachePlayerPosition();

        if (executor != null)
            lastDistanceToPlayer = executor.GetFlatDistanceToPlayer();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (player == null || executor == null)
        {
            for (int i = 0; i < 27; i++)
                sensor.AddObservation(0f);
            return;
        }

        Vector3 toPlayer = player.position - transform.position;
        Vector3 localToPlayer = transform.InverseTransformDirection(toPlayer);

        float x = Mathf.Clamp(localToPlayer.x / maxDistance, -1f, 1f);
        float z = Mathf.Clamp(localToPlayer.z / maxDistance, -1f, 1f);
        float dist = Mathf.Clamp(toPlayer.magnitude / maxDistance, 0f, 1f);
        float signedAngle = executor.GetSignedAngleToPlayerNormalized();
        float forwardDot = executor.GetForwardDotToPlayer();

        Vector3 currentPlayerFlat = Flat(player.position);
        Vector3 localPlayerVelocity = Vector3.zero;
        currentPlayerFlatSpeed = 0f;

        if (hasPreviousPlayerPos)
        {
            float dt = Mathf.Max(Time.fixedDeltaTime, 0.001f);
            Vector3 flatDelta = (currentPlayerFlat - previousPlayerFlatPos) / dt;
            currentPlayerFlatSpeed = flatDelta.magnitude;
            localPlayerVelocity = transform.InverseTransformDirection(flatDelta);
        }

        previousPlayerFlatPos = currentPlayerFlat;
        hasPreviousPlayerPos = true;

        float currentDistance = executor.GetFlatDistanceToPlayer();
        float localVelX = Mathf.Clamp(localPlayerVelocity.x / 6f, -1f, 1f);
        float localVelZ = Mathf.Clamp(localPlayerVelocity.z / 6f, -1f, 1f);
        float tooClose = currentDistance < tooCloseDistance ? 1f : 0f;
        float ideal = currentDistance >= idealDistanceMin && currentDistance <= idealDistanceMax ? 1f : 0f;
        float tooFar = currentDistance > tooFarDistance ? 1f : 0f;

        sensor.AddObservation(x);
        sensor.AddObservation(z);
        sensor.AddObservation(dist);
        sensor.AddObservation(signedAngle);
        sensor.AddObservation(forwardDot);
        sensor.AddObservation(localVelX);
        sensor.AddObservation(localVelZ);
        sensor.AddObservation(bossHpNormalized);
        sensor.AddObservation(playerHpNormalized);
        sensor.AddObservation(executor.CanAnyAttackNow() ? 1f : 0f);
        sensor.AddObservation(executor.CanRollNow() ? 1f : 0f);
        sensor.AddObservation(executor.GetBusyNormalized());
        sensor.AddObservation(executor.GetRollingNormalized());
        sensor.AddObservation(executor.GetCurrentActionTypeNormalized());
        sensor.AddObservation(executor.GetActionPhaseNormalized());
        sensor.AddObservation(executor.IsPlayerInOneHandRange() ? 1f : 0f);
        sensor.AddObservation(executor.IsPlayerInTwoHandRange() ? 1f : 0f);
        sensor.AddObservation(executor.IsPlayerInSpinRange() ? 1f : 0f);
        sensor.AddObservation(executor.IsPlayerInPunchRange() ? 1f : 0f);
        sensor.AddObservation(executor.GetAttackCooldownNormalized());
        sensor.AddObservation(executor.GetRollCooldownNormalized());
        sensor.AddObservation(tooClose);
        sensor.AddObservation(ideal);
        sensor.AddObservation(tooFar);
        sensor.AddObservation(executor.GetTimeSinceLastAttackNormalized());
        sensor.AddObservation(executor.GetTimeSinceLastSuccessfulHitNormalized());
        sensor.AddObservation(executor.GetPostAttackMovementLockNormalized());
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (executor == null || player == null)
            return;

        if (terminalState != TerminalState.None)
        {
            executor.DoIdle();
            return;
        }

        int action = actions.DiscreteActions[0];
        bool attackAction = action >= (int)BossCombatExecutor.BossActionId.AttackOneHand;
        bool movementAction = action > (int)BossCombatExecutor.BossActionId.Idle && action < (int)BossCombatExecutor.BossActionId.AttackOneHand;

        float distanceBefore = executor.GetFlatDistanceToPlayer();
        float forwardDotBefore = executor.GetForwardDotToPlayer();
        bool canAttackNow = executor.CanAnyAttackNow();
        bool canRollNow = executor.CanRollNow();
        bool tooClose = distanceBefore < tooCloseDistance;
        bool ideal = distanceBefore >= idealDistanceMin && distanceBefore <= idealDistanceMax;
        bool tooFar = distanceBefore > tooFarDistance;
        bool inPressureRange = distanceBefore <= pressureDistance;
        bool passiveTarget = UpdatePassiveTargetState(distanceBefore);

        executor.ExecuteAction(action);

        float distanceAfter = executor.GetFlatDistanceToPlayer();
        float forwardDotAfter = executor.GetForwardDotToPlayer();

        ApplyActionRewards(action, canAttackNow, canRollNow, tooClose, ideal, tooFar, inPressureRange, passiveTarget, distanceBefore, distanceAfter, forwardDotBefore, forwardDotAfter);
        ApplyPassiveShaping(action, canAttackNow, passiveTarget, distanceAfter, forwardDotAfter);
        ApplyRhythmShaping(action, attackAction, movementAction, canAttackNow, passiveTarget, distanceBefore, distanceAfter, inPressureRange, tooClose);
        UpdateAttackRhythmMemory(action, attackAction);

        lastDistanceToPlayer = distanceAfter;
        AddReward(stepPenalty);
    }

    private void ApplyActionRewards(
        int action,
        bool canAttackNow,
        bool canRollNow,
        bool tooClose,
        bool ideal,
        bool tooFar,
        bool inPressureRange,
        bool passiveTarget,
        float distanceBefore,
        float distanceAfter,
        float forwardDotBefore,
        float forwardDotAfter)
    {
        switch ((BossCombatExecutor.BossActionId)action)
        {
            case BossCombatExecutor.BossActionId.Idle:
                if (inPressureRange)
                    AddReward(idleNearPenalty);

                if (passiveTarget && distanceBefore > idealDistanceMax + 0.20f)
                    AddReward(stallVsPassivePenalty * 0.75f);
                break;

            case BossCombatExecutor.BossActionId.WalkForward:
            case BossCombatExecutor.BossActionId.RunForward:
            case BossCombatExecutor.BossActionId.Sprint:
            case BossCombatExecutor.BossActionId.WalkForwardLeft:
            case BossCombatExecutor.BossActionId.WalkForwardRight:
                if (distanceAfter < distanceBefore)
                {
                    float delta = Mathf.Clamp(distanceBefore - distanceAfter, 0f, 1.2f);
                    AddReward(delta * approachRewardScale);

                    if (passiveTarget)
                        AddReward(Mathf.Clamp(delta * stationaryTargetApproachReward, 0f, stationaryTargetApproachReward));
                }

                if (tooFar && distanceAfter < distanceBefore)
                    AddReward(0.009f);
                else if (tooClose)
                    AddReward(-0.003f);

                if (forwardDotAfter > forwardDotBefore)
                    AddReward(facePlayerReward);
                break;

            case BossCombatExecutor.BossActionId.WalkBackward:
                if (tooClose && distanceAfter > distanceBefore)
                {
                    AddReward(0.004f);
                }
                else if (inPressureRange)
                {
                    AddReward(backwardNearPenalty);
                }
                else if (tooFar)
                {
                    AddReward(-0.005f);
                }

                if (passiveTarget && inPressureRange)
                    AddReward(stallVsPassivePenalty * 0.55f);
                break;

            case BossCombatExecutor.BossActionId.StrafeLeft:
            case BossCombatExecutor.BossActionId.StrafeRight:
                if (ideal)
                    AddReward(0.0012f);
                if (forwardDotAfter > forwardDotBefore)
                    AddReward(facePlayerReward);
                if (canAttackNow && inPressureRange)
                    AddReward(-0.0015f);

                if (passiveTarget && distanceBefore > idealDistanceMax + 0.25f && distanceAfter >= distanceBefore - 0.03f)
                    AddReward(stallVsPassivePenalty * 0.45f);
                break;

            case BossCombatExecutor.BossActionId.RollBackward:
                if (!canRollNow)
                {
                    AddReward(-0.015f);
                }
                else if (tooClose)
                {
                    AddReward(0.003f);
                }
                else if (inPressureRange)
                {
                    AddReward(rollNearPenalty);
                }
                else
                {
                    AddReward(-0.004f);
                }

                if (passiveTarget && !tooClose)
                    AddReward(-0.003f);
                break;

            case BossCombatExecutor.BossActionId.AttackOneHand:
                EvaluateAttackIntent(canAttackNow, executor.IsPlayerInOneHandRange(), ideal || tooClose, -0.012f, 0.030f);
                if (ideal) AddReward(0.018f);
                if (tooFar) AddReward(-0.010f);
                break;

            case BossCombatExecutor.BossActionId.AttackTwoHand:
                EvaluateAttackIntent(canAttackNow, executor.IsPlayerInTwoHandRange(), ideal || !tooFar, -0.012f, 0.032f);
                if (distanceBefore >= 2.3f && distanceBefore <= 3.8f) AddReward(0.020f);
                if (tooClose) AddReward(-0.010f);
                break;

            case BossCombatExecutor.BossActionId.AttackSpin:
                EvaluateAttackIntent(canAttackNow, executor.IsPlayerInSpinRange(), !tooClose, -0.014f, 0.022f);
                if (distanceBefore >= 2.5f && distanceBefore <= 4.0f) AddReward(0.012f);
                if (tooClose) AddReward(-0.012f);
                if (canAttackNow && executor.IsPlayerInSpinRange() && distanceBefore >= 2.35f && distanceBefore <= 3.85f && !tooClose)
                    AddReward(spinWindowBonus);
                break;

            case BossCombatExecutor.BossActionId.PunchLeft:
                if (!canAttackNow)
                {
                    AddReward(-0.012f);
                }
                else if (executor.IsPlayerInPunchRange() && tooClose)
                {
                    AddReward(0.040f);
                }
                else if (executor.IsPlayerInPunchRange())
                {
                    AddReward(0.014f);
                }
                else
                {
                    AddReward(-0.015f);
                }

                if (canAttackNow && executor.IsPlayerInPunchRange() && distanceBefore <= 2.05f && forwardDotBefore > 0.72f)
                    AddReward(punchWindowBonus);
                break;
        }
    }

    private void EvaluateAttackIntent(bool canAttackNow, bool inRange, bool preferredSituation, float outOfPlacePenalty, float goodIntentReward)
    {
        if (!canAttackNow)
        {
            AddReward(-0.012f);
        }
        else if (inRange && preferredSituation)
        {
            AddReward(goodIntentReward);
        }
        else if (inRange)
        {
            AddReward(goodIntentReward * 0.55f);
        }
        else
        {
            AddReward(outOfPlacePenalty);
        }
    }

    private void ApplyPassiveShaping(int action, bool canAttackNow, bool passiveTarget, float distance, float forwardDot)
    {
        bool inPressureRange = distance <= pressureDistance;
        bool attackAction = action >= (int)BossCombatExecutor.BossActionId.AttackOneHand;

        if (distance >= idealDistanceMin && distance <= idealDistanceMax)
            AddReward(0.0018f);
        else if (distance < tooCloseDistance)
            AddReward(-0.0024f);
        else if (distance > tooFarDistance)
            AddReward(-0.0052f);

        if (!executor.IsBusy() && distance <= idealDistanceMax + 0.30f && forwardDot > 0.72f)
            AddReward(0.0013f);

        if (canAttackNow && inPressureRange && !attackAction)
            AddReward(canAttackButDidNotPenalty);

        if (inPressureRange && !attackAction)
        {
            timeNearPlayerWithoutAttack += Time.fixedDeltaTime;
            if (timeNearPlayerWithoutAttack >= prolongedNoAttackDelay)
                AddReward(prolongedNoAttackPenalty);
        }
        else if (attackAction)
        {
            timeNearPlayerWithoutAttack = 0f;
        }
        else if (distance > pressureDistance)
        {
            timeNearPlayerWithoutAttack = 0f;
        }

        if (lastDistanceToPlayer > 0f && distance > idealDistanceMax)
        {
            float delta = lastDistanceToPlayer - distance;
            if (delta > 0f)
                AddReward(Mathf.Clamp(delta * approachRewardScale, 0f, 0.018f));
        }

        if (passiveTarget)
        {
            if (!attackAction && distance > idealDistanceMax + 0.35f && !IsForwardPressureAction(action))
                AddReward(stallVsPassivePenalty);

            if (!executor.IsBusy() &&
                distance >= idealDistanceMin - 0.10f &&
                distance <= pressureDistance &&
                forwardDot > 0.72f)
            {
                AddReward(holdPressureVsPassiveReward);
            }
        }
    }

    private void ApplyRhythmShaping(
        int action,
        bool attackAction,
        bool movementAction,
        bool canAttackNow,
        bool passiveTarget,
        float distanceBefore,
        float distanceAfter,
        bool inPressureRange,
        bool tooClose)
    {
        if (attackAction)
        {
            bool rapidRepeat = Time.time < lastAttackDecisionTime + rapidRepeatWindow;
            if (rapidRepeat)
                AddReward(rapidRepeatAttackPenalty);

            if (lastActionReceived == action)
                AddReward(sameAttackRepeatPenalty);

            if (lastActionReceived >= (int)BossCombatExecutor.BossActionId.AttackOneHand &&
                lastActionReceived != action)
            {
                AddReward(attackTypeSwitchReward);
            }

            if (consecutiveAttackDecisions >= 3 && Time.time > lastSuccessfulHitTime + 1.0f)
                AddReward(stalePressureAttackPenalty * Mathf.Clamp(consecutiveAttackDecisions - 2, 1, 3));

            if (!canAttackNow)
                AddReward(-0.004f);

            if (distanceBefore > idealDistanceMax + 0.80f)
                AddReward(-0.008f);

            if (tooClose && action == (int)BossCombatExecutor.BossActionId.AttackSpin)
                AddReward(-0.004f);

            return;
        }

        if (movementAction && lastActionReceived >= (int)BossCombatExecutor.BossActionId.AttackOneHand)
        {
            if (inPressureRange)
                AddReward(movementAfterAttackReward);

            if (tooClose && distanceAfter > distanceBefore)
                AddReward(repositionNearReward);
        }

        if (passiveTarget && movementAction && IsForwardPressureAction(action) && distanceAfter < distanceBefore)
            AddReward(stationaryTargetApproachReward * 0.45f);
    }

    private void UpdateAttackRhythmMemory(int action, bool attackAction)
    {
        if (attackAction)
        {
            consecutiveAttackDecisions = lastActionReceived >= (int)BossCombatExecutor.BossActionId.AttackOneHand
                ? consecutiveAttackDecisions + 1
                : 1;

            lastAttackDecisionTime = Time.time;
        }
        else
        {
            consecutiveAttackDecisions = 0;
        }

        lastActionReceived = action;
    }

    private bool UpdatePassiveTargetState(float distanceToPlayer)
    {
        if (currentPlayerFlatSpeed <= passiveTargetSpeedThreshold && distanceToPlayer <= tooFarDistance + 1.2f)
            passiveTargetTimer += Time.fixedDeltaTime;
        else
            passiveTargetTimer = Mathf.Max(0f, passiveTargetTimer - Time.fixedDeltaTime * 1.5f);

        return passiveTargetTimer >= passiveTargetWindow;
    }

    private bool IsForwardPressureAction(int action)
    {
        return action == (int)BossCombatExecutor.BossActionId.WalkForward ||
               action == (int)BossCombatExecutor.BossActionId.RunForward ||
               action == (int)BossCombatExecutor.BossActionId.Sprint ||
               action == (int)BossCombatExecutor.BossActionId.WalkForwardLeft ||
               action == (int)BossCombatExecutor.BossActionId.WalkForwardRight;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        d[0] = (int)BossCombatExecutor.BossActionId.Idle;

        if (Input.GetKey(KeyCode.W)) d[0] = (int)BossCombatExecutor.BossActionId.WalkForward;
        if (Input.GetKey(KeyCode.R)) d[0] = (int)BossCombatExecutor.BossActionId.RunForward;
        if (Input.GetKey(KeyCode.T)) d[0] = (int)BossCombatExecutor.BossActionId.Sprint;
        if (Input.GetKey(KeyCode.S)) d[0] = (int)BossCombatExecutor.BossActionId.WalkBackward;
        if (Input.GetKey(KeyCode.A)) d[0] = (int)BossCombatExecutor.BossActionId.StrafeLeft;
        if (Input.GetKey(KeyCode.D)) d[0] = (int)BossCombatExecutor.BossActionId.StrafeRight;
        if (Input.GetKey(KeyCode.Q)) d[0] = (int)BossCombatExecutor.BossActionId.WalkForwardLeft;
        if (Input.GetKey(KeyCode.E)) d[0] = (int)BossCombatExecutor.BossActionId.WalkForwardRight;
        if (Input.GetKey(KeyCode.Space)) d[0] = (int)BossCombatExecutor.BossActionId.RollBackward;
        if (Input.GetKey(KeyCode.Alpha1)) d[0] = (int)BossCombatExecutor.BossActionId.AttackOneHand;
        if (Input.GetKey(KeyCode.Alpha2)) d[0] = (int)BossCombatExecutor.BossActionId.AttackTwoHand;
        if (Input.GetKey(KeyCode.Alpha3)) d[0] = (int)BossCombatExecutor.BossActionId.AttackSpin;
        if (Input.GetKey(KeyCode.Alpha4)) d[0] = (int)BossCombatExecutor.BossActionId.PunchLeft;
    }

    private void Update()
    {
        episodeTimer += Time.deltaTime;
        SyncHealthFromScene();

        if (terminalState != TerminalState.None)
        {
            terminalTimer += Time.deltaTime;
            if (terminalTimer >= terminalDelay)
            {
                FinalizeTerminalState();
                EndEpisode();
            }
            return;
        }

        if (episodeTimer >= episodeTimeLimit)
        {
            terminalState = TerminalState.Timeout;
            terminalTimer = 0f;
            executor.DoIdle();
            return;
        }

        if (playerHpNormalized <= 0f)
        {
            terminalState = TerminalState.BossWon;
            terminalTimer = 0f;
            executor.DoIdle();
            return;
        }

        if (bossHpNormalized <= 0f)
        {
            terminalState = TerminalState.BossLost;
            terminalTimer = 0f;
            executor.DoIdle();
            return;
        }

        if (Input.GetKeyDown(manualEpisodeResetKey))
            EndEpisode();
    }

    private void FinalizeTerminalState()
    {
        switch (terminalState)
        {
            case TerminalState.BossWon:
                AddReward(winReward);
                break;
            case TerminalState.BossLost:
                AddReward(lossPenalty);
                break;
            case TerminalState.Timeout:
                AddReward(timeoutPenalty);
                break;
        }
    }

    private void SyncHealthFromScene()
    {
        if (bossHealth != null)
            bossHpNormalized = bossHealth.GetNormalizedHP();

        if (trainingPlayerHealth != null)
            playerHpNormalized = trainingPlayerHealth.GetNormalizedHP();
    }

    private void CachePlayerPosition()
    {
        if (player == null)
            return;

        previousPlayerFlatPos = Flat(player.position);
        hasPreviousPlayerPos = true;
    }

    private Vector3 Flat(Vector3 pos)
    {
        pos.y = 0f;
        return pos;
    }

    public void NotifyAttackStarted(bool inGoodWindow)
    {
        AddReward(inGoodWindow ? attackStartGoodReward : attackStartBadReward);
        timeNearPlayerWithoutAttack = 0f;
    }

    public void NotifyDealtDamage(float damage)
    {
        AddReward(damage * dealtDamageRewardScale);
        timeNearPlayerWithoutAttack = 0f;
        consecutiveMisses = 0;
        lastSuccessfulHitTime = Time.time;
    }

    public void NotifyMissedAttack()
    {
        AddReward(missPenalty);
        consecutiveMisses++;
        if (consecutiveMisses >= 2)
            AddReward(repeatedMissPenalty * Mathf.Clamp(consecutiveMisses - 1, 1, 3));
    }

    public void NotifyTookDamage(float damage)
    {
        AddReward(-damage * tookDamagePenaltyScale);
    }
}
