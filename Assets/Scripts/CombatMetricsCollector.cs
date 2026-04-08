using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Invector.vCharacterController;

public class CombatMetricsCollector : MonoBehaviour
{
    public static CombatMetricsCollector Instance { get; private set; }

    public enum FighterId
    {
        None,
        Boss,
        Player
    }

    [Serializable]
    private class EventRow
    {
        public int runId;
        public float time;
        public string actor;
        public string target;
        public string eventType;
        public string actionType;
        public float damage;
        public float distance;
        public int bossHP;
        public float playerHP;
        public string extra;
    }

    [Serializable]
    private class PositionSampleRow
    {
        public int runId;
        public float time;
        public float bossX;
        public float bossZ;
        public float playerX;
        public float playerZ;
        public float distance;
        public int bossHP;
        public float playerHP;
    }

    private class BossRuntimeStats
    {
        public int attackAttempts;
        public int attackHits;
        public int attackMisses;
        public float totalDamageDealt;
        public float totalDamageTaken;
        public float maxSingleHitDamage;

        public string lastAttemptType = string.Empty;
        public int currentSameAttackStreak;
        public int maxSameAttackStreak;

        public int currentHitStreak;
        public int maxHitStreak;

        public readonly Dictionary<string, int> attemptsByType = new Dictionary<string, int>();
        public readonly Dictionary<string, int> hitsByType = new Dictionary<string, int>();
        public readonly Dictionary<string, int> actionsByType = new Dictionary<string, int>();

        public void RegisterAction(string actionType)
        {
            Increment(actionsByType, actionType);
        }

        public void RegisterAttempt(string attackType)
        {
            attackAttempts++;
            Increment(attemptsByType, attackType);

            if (lastAttemptType == attackType)
                currentSameAttackStreak++;
            else
                currentSameAttackStreak = 1;

            maxSameAttackStreak = Mathf.Max(maxSameAttackStreak, currentSameAttackStreak);
            lastAttemptType = attackType;
        }

        public void RegisterHit(string attackType, float damage)
        {
            attackHits++;
            totalDamageDealt += damage;
            maxSingleHitDamage = Mathf.Max(maxSingleHitDamage, damage);
            Increment(hitsByType, attackType);
            currentHitStreak++;
            maxHitStreak = Mathf.Max(maxHitStreak, currentHitStreak);
        }

        public void RegisterMiss()
        {
            attackMisses++;
            currentHitStreak = 0;
        }

        public void RegisterDamageTaken(float damage)
        {
            totalDamageTaken += damage;
        }

        public int UniqueAttackTypesUsed()
        {
            return attemptsByType.Values.Count(v => v > 0);
        }

        public float AttackEntropyNormalized()
        {
            int total = attemptsByType.Values.Sum();
            if (total <= 0)
                return 0f;

            int k = attemptsByType.Count(v => v.Value > 0);
            if (k <= 1)
                return 0f;

            double entropy = 0.0;
            foreach (KeyValuePair<string, int> kvp in attemptsByType)
            {
                if (kvp.Value <= 0)
                    continue;

                double p = (double)kvp.Value / total;
                entropy -= p * Math.Log(p, 2.0);
            }

            double maxEntropy = Math.Log(k, 2.0);
            if (maxEntropy <= 0.0)
                return 0f;

            return Mathf.Clamp01((float)(entropy / maxEntropy));
        }

        public int GetAttempts(string attackType) => attemptsByType.TryGetValue(attackType, out int value) ? value : 0;
        public int GetHits(string attackType) => hitsByType.TryGetValue(attackType, out int value) ? value : 0;
        public int GetActions(string actionType) => actionsByType.TryGetValue(actionType, out int value) ? value : 0;

        private static void Increment(Dictionary<string, int> dict, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                key = "Unknown";

            if (dict.ContainsKey(key))
                dict[key]++;
            else
                dict[key] = 1;
        }
    }

    [Header("Scene References")]
    public Transform boss;
    public Transform player;
    public Transform arenaCenter;
    public BossHealth bossHealth;
    public TrainingPlayerHealth playerHealth;
    public vThirdPersonController invectorPlayerController;
    public BossBrainClassic bossClassicBrain;
    public BossCombatExecutor bossCombatExecutor;

    [Header("Run Labels")]
    public string modeLabel = "RLTraining";
    public string sceneLabel = "ArenaRL";

    [Header("Arena / Sampling")]
    public float arenaRadius = 28f;
    public float positionSampleInterval = 0.10f;
    public float nearEdgeThresholdNormalized = 0.85f;

    [Header("Spacing Bands")]
    public float tooCloseDistance = 1.25f;
    public float idealDistanceMin = 1.90f;
    public float idealDistanceMax = 3.10f;
    public float tooFarDistance = 6.50f;

    [Header("Export")]
    public bool exportOnRunEnd = true;
    public string exportFolderName = "CombatMetrics";
    public string runSummaryFileName = "run_summary.csv";
    public string eventLogFileName = "combat_events.csv";
    public string positionSamplesFileName = "position_samples.csv";

    [Header("Debug")]
    public bool debugLogs = false;
    public bool autoBeginRunOnStart = false;

    private readonly List<EventRow> currentEvents = new List<EventRow>();
    private readonly List<PositionSampleRow> currentSamples = new List<PositionSampleRow>();

    private BossRuntimeStats bossStats;

    private int runCounter;
    private int runIdBase;
    private int currentRunId;
    private bool runActive;
    private float runStartTime;
    private float lastSampleTime;
    private float lastAnyDamageTime;
    private float longestNoDamageWindow;

    private FighterId firstSuccessfulHitBy = FighterId.None;
    private float firstSuccessfulHitTime = -1f;
    private float firstSuccessfulHitDamage = 0f;

    private float distanceSum;
    private int distanceSampleCount;
    private float minDistance;
    private float maxDistance;
    private float timeTooClose;
    private float timeIdealRange;
    private float timeTooFar;
    private float bossTimeNearEdge;
    private float playerTimeNearEdge;
    private float timeAnyNearEdge;

    private bool runEndedByHook;

    private bool triedResolveReflectedPlayerHealth;
    private Component reflectedPlayerHealthComponent;
    private MemberInfo reflectedCurrentHealthMember;
    private MemberInfo reflectedMaxHealthMember;

    private static readonly string[] currentHealthNames = { "currentHP", "currentHealth", "health", "_currentHealth" };
    private static readonly string[] maxHealthNames = { "maxHP", "maxHealth", "_maxHealth", "maxLife" };

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        runIdBase = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 2000000000L);

        if (bossHealth == null && boss != null)
            bossHealth = boss.GetComponent<BossHealth>();

        if (playerHealth == null && player != null)
            playerHealth = player.GetComponent<TrainingPlayerHealth>();

        if (invectorPlayerController == null && player != null)
            invectorPlayerController = player.GetComponent<vThirdPersonController>();

        if (bossClassicBrain == null && boss != null)
            bossClassicBrain = boss.GetComponent<BossBrainClassic>();

        if (bossCombatExecutor == null && boss != null)
            bossCombatExecutor = boss.GetComponent<BossCombatExecutor>();
    }

    private void Start()
    {
        if (autoBeginRunOnStart)
            BeginRun();
    }

    private void Update()
    {
        if (!runActive)
            return;

        float elapsed = Time.time - runStartTime;
        if (elapsed >= lastSampleTime + positionSampleInterval)
        {
            SamplePositions(elapsed);
            lastSampleTime = elapsed;
        }

        if (!runEndedByHook)
        {
            if (bossHealth != null && bossHealth.IsDead)
                EndRun("BossDeath", FighterId.Player);
            else if (playerHealth != null && playerHealth.IsDead)
                EndRun("PlayerDeath", FighterId.Boss);
            else if (invectorPlayerController != null && invectorPlayerController.isDead)
                EndRun("PlayerDeath", FighterId.Boss);
        }
    }

    public void BeginRun()
    {
        if (runActive)
            EndRun("InterruptedByNewRun", FighterId.None);

        runCounter++;
        currentRunId = runIdBase + runCounter;
        runActive = true;
        runEndedByHook = false;
        runStartTime = Time.time;
        lastSampleTime = -positionSampleInterval;
        lastAnyDamageTime = 0f;
        longestNoDamageWindow = 0f;

        firstSuccessfulHitBy = FighterId.None;
        firstSuccessfulHitTime = -1f;
        firstSuccessfulHitDamage = 0f;

        distanceSum = 0f;
        distanceSampleCount = 0;
        minDistance = float.MaxValue;
        maxDistance = 0f;
        timeTooClose = 0f;
        timeIdealRange = 0f;
        timeTooFar = 0f;
        bossTimeNearEdge = 0f;
        playerTimeNearEdge = 0f;
        timeAnyNearEdge = 0f;

        bossStats = new BossRuntimeStats();
        currentEvents.Clear();
        currentSamples.Clear();

        LogEvent(FighterId.None, FighterId.None, "RunStarted", string.Empty, 0f, CurrentDistance(), string.Empty, 0f);
        SamplePositions(0f);
        lastSampleTime = 0f;

        if (debugLogs)
            Debug.Log($"[CombatMetricsCollector] BeginRun runId={currentRunId}", this);
    }

    public void EndRun(string endReason, FighterId winner)
    {
        if (!runActive)
            return;

        runEndedByHook = true;

        float elapsed = Mathf.Max(0f, Time.time - runStartTime);
        FlushPendingBossAttackMetrics();
        longestNoDamageWindow = Mathf.Max(longestNoDamageWindow, elapsed - lastAnyDamageTime);

        LogEvent(
            winner,
            winner == FighterId.Boss ? FighterId.Player : winner == FighterId.Player ? FighterId.Boss : FighterId.None,
            "RunEnded",
            endReason,
            0f,
            CurrentDistance(),
            $"winner={winner}",
            elapsed);

        runActive = false;

        if (exportOnRunEnd)
            ExportCurrentRun(endReason, winner, elapsed);

        if (debugLogs)
            Debug.Log($"[CombatMetricsCollector] EndRun runId={currentRunId} winner={winner} reason={endReason} duration={elapsed:F2}", this);
    }

    public void RegisterBossAttackAttempt(string attackType)
    {
        if (!runActive)
            return;

        float time = Time.time - runStartTime;
        bossStats.RegisterAttempt(attackType);
        LogEvent(FighterId.Boss, FighterId.Player, "AttackAttempt", attackType, 0f, CurrentDistance(), string.Empty, time);
    }

    public void RegisterBossAttackHit(string attackType, int damage)
    {
        if (!runActive)
            return;

        float time = Time.time - runStartTime;
        bossStats.RegisterHit(attackType, damage);
        LogEvent(FighterId.Boss, FighterId.Player, "AttackHit", attackType, damage, CurrentDistance(), string.Empty, time);
    }

    public void RegisterBossAttackMiss(string attackType)
    {
        if (!runActive)
            return;

        float time = Time.time - runStartTime;
        bossStats.RegisterMiss();
        LogEvent(FighterId.Boss, FighterId.Player, "AttackMiss", attackType, 0f, CurrentDistance(), string.Empty, time);
    }

    // Final version: player-side combat metrics are intentionally not tracked.
    public void RegisterPlayerAttackAttempt(string attackType) { }
    public void RegisterPlayerAttackHit(string attackType, int damage) { }
    public void RegisterPlayerAttackMiss(string attackType) { }
    public void RegisterPlayerAction(string actionType) { }

    public void RegisterBossAction(string actionType)
    {
        if (!runActive)
            return;

        bossStats.RegisterAction(actionType);
        LogEvent(FighterId.Boss, FighterId.None, "Action", actionType, 0f, CurrentDistance(), string.Empty);
    }

    public void RegisterBossTookDamage(int damage, string source)
    {
        if (!runActive)
            return;

        float appliedDamage = Mathf.Max(0f, damage);
        bossStats.RegisterDamageTaken(appliedDamage);
        TrackSuccessfulDamage(FighterId.Player, appliedDamage);
        LogEvent(FighterId.Player, FighterId.Boss, "DamageTaken", source, appliedDamage, CurrentDistance(), string.Empty);
    }

    public void RegisterPlayerTookDamage(float damage, string source)
    {
        if (!runActive)
            return;

        float appliedDamage = Mathf.Max(0f, damage);
        TrackSuccessfulDamage(FighterId.Boss, appliedDamage);
        LogEvent(FighterId.Boss, FighterId.Player, "DamageTaken", source, appliedDamage, CurrentDistance(), string.Empty);
    }

    public void RegisterBossDeath()
    {
        if (!runActive)
            return;

        LogEvent(FighterId.Player, FighterId.Boss, "Death", "BossDeath", 0f, CurrentDistance(), string.Empty);
        EndRun("BossDeath", FighterId.Player);
    }

    public void RegisterPlayerDeath()
    {
        if (!runActive)
            return;

        LogEvent(FighterId.Boss, FighterId.Player, "Death", "PlayerDeath", 0f, CurrentDistance(), string.Empty);
        EndRun("PlayerDeath", FighterId.Boss);
    }

    private void FlushPendingBossAttackMetrics()
    {
        if (bossClassicBrain == null && boss != null)
            bossClassicBrain = boss.GetComponent<BossBrainClassic>();

        if (bossCombatExecutor == null && boss != null)
            bossCombatExecutor = boss.GetComponent<BossCombatExecutor>();

        if (bossClassicBrain != null)
            bossClassicBrain.FlushPendingAttackMetrics(true);

        if (bossCombatExecutor != null)
            bossCombatExecutor.FlushPendingAttackMetrics(true);
    }

    private void TrackSuccessfulDamage(FighterId actor, float damage)
    {
        float time = Time.time - runStartTime;

        if (firstSuccessfulHitBy == FighterId.None)
        {
            firstSuccessfulHitBy = actor;
            firstSuccessfulHitTime = time;
            firstSuccessfulHitDamage = damage;
        }

        longestNoDamageWindow = Mathf.Max(longestNoDamageWindow, time - lastAnyDamageTime);
        lastAnyDamageTime = time;
    }

    private void SamplePositions(float elapsed)
    {
        if (boss == null || player == null)
            return;

        float distance = CurrentDistance();
        minDistance = Mathf.Min(minDistance, distance);
        maxDistance = Mathf.Max(maxDistance, distance);
        distanceSum += distance;
        distanceSampleCount++;

        float dt = Mathf.Max(0f, positionSampleInterval);
        if (distance < tooCloseDistance) timeTooClose += dt;
        if (distance >= idealDistanceMin && distance <= idealDistanceMax) timeIdealRange += dt;
        if (distance > tooFarDistance) timeTooFar += dt;

        bool bossNear = IsNearArenaEdge(boss.position);
        bool playerNear = IsNearArenaEdge(player.position);
        if (bossNear) bossTimeNearEdge += dt;
        if (playerNear) playerTimeNearEdge += dt;
        if (bossNear || playerNear) timeAnyNearEdge += dt;

        currentSamples.Add(new PositionSampleRow
        {
            runId = currentRunId,
            time = elapsed,
            bossX = boss.position.x,
            bossZ = boss.position.z,
            playerX = player.position.x,
            playerZ = player.position.z,
            distance = distance,
            bossHP = bossHealth != null ? bossHealth.currentHP : 0,
            playerHP = GetPlayerHpForLogs()
        });
    }

    private bool IsNearArenaEdge(Vector3 position)
    {
        if (arenaCenter == null || arenaRadius <= 0f)
            return false;

        Vector3 offset = position - arenaCenter.position;
        offset.y = 0f;
        float normalized = offset.magnitude / arenaRadius;
        return normalized >= nearEdgeThresholdNormalized;
    }

    private float CurrentDistance()
    {
        if (boss == null || player == null)
            return 0f;

        Vector3 a = boss.position;
        Vector3 b = player.position;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void LogEvent(FighterId actor, FighterId target, string eventType, string actionType, float damage, float distance, string extra, float? elapsedOverride = null)
    {
        float elapsed = elapsedOverride ?? (runActive ? Time.time - runStartTime : 0f);

        currentEvents.Add(new EventRow
        {
            runId = currentRunId,
            time = elapsed,
            actor = actor.ToString(),
            target = target.ToString(),
            eventType = eventType,
            actionType = actionType,
            damage = damage,
            distance = distance,
            bossHP = bossHealth != null ? bossHealth.currentHP : 0,
            playerHP = GetPlayerHpForLogs(),
            extra = extra
        });
    }

    private float GetPlayerHpForLogs()
    {
        if (playerHealth != null)
            return playerHealth.currentHP;

        if (TryGetReflectedPlayerHealth(out float currentHp, out _))
            return currentHp;

        if (invectorPlayerController != null)
            return invectorPlayerController.isDead ? 0f : -1f;

        return 0f;
    }

    private float GetPlayerMaxHpForSummary()
    {
        if (playerHealth != null)
            return playerHealth.maxHP;

        if (TryGetReflectedPlayerHealth(out _, out float maxHp))
            return maxHp;

        return invectorPlayerController != null ? -1f : 0f;
    }

    private float GetPlayerCurrentHpForSummary()
    {
        if (playerHealth != null)
            return playerHealth.currentHP;

        if (TryGetReflectedPlayerHealth(out float currentHp, out _))
            return currentHp;

        return invectorPlayerController != null ? (invectorPlayerController.isDead ? 0f : -1f) : 0f;
    }

    private bool TryGetReflectedPlayerHealth(out float currentHp, out float maxHp)
    {
        currentHp = 0f;
        maxHp = 0f;

        ResolveReflectedPlayerHealthMembers();
        if (reflectedPlayerHealthComponent == null || reflectedCurrentHealthMember == null)
            return false;

        currentHp = ReadNumericMember(reflectedPlayerHealthComponent, reflectedCurrentHealthMember);
        maxHp = reflectedMaxHealthMember != null
            ? ReadNumericMember(reflectedPlayerHealthComponent, reflectedMaxHealthMember)
            : 0f;

        return currentHp >= 0f || maxHp > 0f;
    }

    private void ResolveReflectedPlayerHealthMembers()
    {
        if (triedResolveReflectedPlayerHealth)
            return;

        triedResolveReflectedPlayerHealth = true;

        GameObject targetObject = null;
        if (invectorPlayerController != null)
            targetObject = invectorPlayerController.gameObject;
        else if (player != null)
            targetObject = player.gameObject;

        if (targetObject == null)
            return;

        Component[] components = targetObject.GetComponents<Component>()
            .Where(c => c != null)
            .OrderByDescending(GetHealthComponentPriority)
            .ToArray();

        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            Type type = component.GetType();

            MemberInfo currentMember = FindNumericMember(type, currentHealthNames);
            if (currentMember == null)
                continue;

            MemberInfo maxMember = FindNumericMember(type, maxHealthNames);

            reflectedPlayerHealthComponent = component;
            reflectedCurrentHealthMember = currentMember;
            reflectedMaxHealthMember = maxMember;
            break;
        }
    }

    private static int GetHealthComponentPriority(Component component)
    {
        if (component == null)
            return -1;

        string typeName = component.GetType().Name.ToLowerInvariant();
        int score = 0;

        if (typeName.Contains("health"))
            score += 10;

        if (typeName.Contains("damage"))
            score += 2;

        if (typeName.Contains("controller"))
            score += 1;

        return score;
    }

    private static MemberInfo FindNumericMember(Type type, string[] candidateNames)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < candidateNames.Length; i++)
        {
            string candidate = candidateNames[i];

            PropertyInfo property = type.GetProperty(candidate, flags);
            if (property != null && property.CanRead && IsNumericType(property.PropertyType))
                return property;

            FieldInfo field = type.GetField(candidate, flags);
            if (field != null && IsNumericType(field.FieldType))
                return field;
        }

        return null;
    }

    private static bool IsNumericType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(int) ||
               underlying == typeof(float) ||
               underlying == typeof(double) ||
               underlying == typeof(long) ||
               underlying == typeof(short);
    }

    private static float ReadNumericMember(Component component, MemberInfo member)
    {
        try
        {
            object value = null;

            if (member is PropertyInfo property)
                value = property.GetValue(component, null);
            else if (member is FieldInfo field)
                value = field.GetValue(component);

            if (value == null)
                return 0f;

            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0f;
        }
    }

    private void ExportCurrentRun(string endReason, FighterId winner, float duration)
    {
        string directory = Path.Combine(Application.persistentDataPath, exportFolderName);
        Directory.CreateDirectory(directory);
        Debug.Log(Path.Combine(Application.persistentDataPath, exportFolderName));

        string summaryPath = Path.Combine(directory, runSummaryFileName);
        string eventsPath = Path.Combine(directory, eventLogFileName);
        string samplesPath = Path.Combine(directory, positionSamplesFileName);

        AppendSummaryRow(summaryPath, endReason, winner, duration);
        AppendEventRows(eventsPath);
        AppendSampleRows(samplesPath);
    }

    private void AppendSummaryRow(string path, string endReason, FighterId winner, float duration)
    {
        bool writeHeader = !File.Exists(path);
        using (StreamWriter writer = new StreamWriter(path, true, Encoding.UTF8))
        {
            if (writeHeader)
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    "runId","mode","scene","winner","endReason","fightDurationSeconds",
                    "bossStartHP","bossFinalHP","playerStartHP","playerFinalHP",
                    "firstSuccessfulHitBy","firstSuccessfulHitTime","firstSuccessfulHitDamage",
                    "bossAttackAttempts","bossAttackHits","bossAttackMisses","bossAttackAccuracy","bossTotalDamageDealt","bossTotalDamageTaken","bossDamagePerSecond","bossAverageDamagePerHit","bossMaxSingleHitDamage",
                    "bossAttackOneHandAttempts","bossAttackOneHandHits","bossAttackTwoHandAttempts","bossAttackTwoHandHits","bossAttackSpinAttempts","bossAttackSpinHits","bossPunchLeftAttempts","bossPunchLeftHits",
                    "bossUniqueAttackTypesUsed","bossAttackEntropy","bossMaxSameAttackStreak","bossMaxHitStreak",
                    "bossRollCount","bossRetreatCount","bossStrafeLeftCount","bossStrafeRightCount","bossForwardMoveCount",
                    "averageDistance","minDistance","maxDistance","timeTooClose","timeIdealRange","timeTooFar","bossTimeNearEdge","playerTimeNearEdge","timeAnyNearEdge",
                    "longestNoDamageWindow"
                }));
            }

            float bossAccuracy = SafeRatio(bossStats.attackHits, bossStats.attackAttempts);
            float bossDps = duration > 0f ? bossStats.totalDamageDealt / duration : 0f;
            float bossAvgDamagePerHit = SafeRatio(bossStats.totalDamageDealt, bossStats.attackHits);
            float averageDistance = distanceSampleCount > 0 ? distanceSum / distanceSampleCount : 0f;

            writer.WriteLine(string.Join(",", new[]
            {
                Csv(currentRunId), Csv(modeLabel), Csv(sceneLabel), Csv(winner.ToString()), Csv(endReason), Csv(duration),
                Csv(bossHealth != null ? bossHealth.maxHP : 0), Csv(bossHealth != null ? bossHealth.currentHP : 0), Csv(GetPlayerMaxHpForSummary()), Csv(GetPlayerCurrentHpForSummary()),
                Csv(firstSuccessfulHitBy.ToString()), Csv(firstSuccessfulHitTime), Csv(firstSuccessfulHitDamage),
                Csv(bossStats.attackAttempts), Csv(bossStats.attackHits), Csv(bossStats.attackMisses), Csv(bossAccuracy), Csv(bossStats.totalDamageDealt), Csv(bossStats.totalDamageTaken), Csv(bossDps), Csv(bossAvgDamagePerHit), Csv(bossStats.maxSingleHitDamage),
                Csv(bossStats.GetAttempts("AttackOneHand")), Csv(bossStats.GetHits("AttackOneHand")), Csv(bossStats.GetAttempts("AttackTwoHand")), Csv(bossStats.GetHits("AttackTwoHand")), Csv(bossStats.GetAttempts("AttackSpin")), Csv(bossStats.GetHits("AttackSpin")), Csv(bossStats.GetAttempts("PunchLeft")), Csv(bossStats.GetHits("PunchLeft")),
                Csv(bossStats.UniqueAttackTypesUsed()), Csv(bossStats.AttackEntropyNormalized()), Csv(bossStats.maxSameAttackStreak), Csv(bossStats.maxHitStreak),
                Csv(bossStats.GetActions("RollBackward")), Csv(bossStats.GetActions("WalkBackward")), Csv(bossStats.GetActions("StrafeLeft")), Csv(bossStats.GetActions("StrafeRight")), Csv(bossStats.GetActions("WalkForward") + bossStats.GetActions("RunForward") + bossStats.GetActions("Sprint") + bossStats.GetActions("WalkForwardLeft") + bossStats.GetActions("WalkForwardRight")),
                Csv(averageDistance), Csv(minDistance == float.MaxValue ? 0f : minDistance), Csv(maxDistance), Csv(timeTooClose), Csv(timeIdealRange), Csv(timeTooFar), Csv(bossTimeNearEdge), Csv(playerTimeNearEdge), Csv(timeAnyNearEdge),
                Csv(longestNoDamageWindow)
            }));
        }
    }

    private void AppendEventRows(string path)
    {
        bool writeHeader = !File.Exists(path);
        using (StreamWriter writer = new StreamWriter(path, true, Encoding.UTF8))
        {
            if (writeHeader)
                writer.WriteLine("runId,time,actor,target,eventType,actionType,damage,distance,bossHP,playerHP,extra");

            for (int i = 0; i < currentEvents.Count; i++)
            {
                EventRow row = currentEvents[i];
                writer.WriteLine(string.Join(",", new[]
                {
                    Csv(row.runId), Csv(row.time), Csv(row.actor), Csv(row.target), Csv(row.eventType), Csv(row.actionType), Csv(row.damage), Csv(row.distance),
                    Csv(row.bossHP), Csv(row.playerHP), Csv(row.extra)
                }));
            }
        }
    }

    private void AppendSampleRows(string path)
    {
        bool writeHeader = !File.Exists(path);
        using (StreamWriter writer = new StreamWriter(path, true, Encoding.UTF8))
        {
            if (writeHeader)
                writer.WriteLine("runId,time,bossX,bossZ,playerX,playerZ,distance,bossHP,playerHP");

            for (int i = 0; i < currentSamples.Count; i++)
            {
                PositionSampleRow row = currentSamples[i];
                writer.WriteLine(string.Join(",", new[]
                {
                    Csv(row.runId), Csv(row.time), Csv(row.bossX), Csv(row.bossZ), Csv(row.playerX), Csv(row.playerZ), Csv(row.distance), Csv(row.bossHP), Csv(row.playerHP)
                }));
            }
        }
    }

    private static float SafeRatio(float numerator, float denominator)
    {
        if (Mathf.Abs(denominator) <= 0.0001f)
            return 0f;

        return numerator / denominator;
    }

    private static string Csv(string value)
    {
        if (value == null)
            return string.Empty;

        string escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static string Csv(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Csv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
