using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Published after a building upgrade is committed to the player save.</summary>
public readonly struct BuildingUpgradeInfo
{
    public string BuildingKey { get; }
    public int PreviousLevel { get; }
    public int NewLevel { get; }
    public int ServicePlaces { get; }
    public int CoinCost { get; }

    public BuildingUpgradeInfo(string buildingKey, int previousLevel, int newLevel,
        int servicePlaces, int coinCost)
    {
        BuildingKey = buildingKey;
        PreviousLevel = previousLevel;
        NewLevel = newLevel;
        ServicePlaces = servicePlaces;
        CoinCost = coinCost;
    }
}

public class GuestWaveController : MonoBehaviour
{
    [Header("Prototype data assets")]
    [SerializeField] private WaveBalanceConfig balanceConfig;
    [SerializeField] private WaveDefinition waveDefinition;
    [SerializeField, Min(0f)] private float guestReturnTimeout = 10f;
    [SerializeField] private string playerId = "test-player-001";

    public GuestWaveState CurrentState { get; private set; } = GuestWaveState.Ready;
    public event Action<int> OnWaveStarted;
    public event Action OnWaveCompleted;
    public event Action<float, int, int> OnWaveResult;
    public event Action<GuestWaveState> OnWaveStateChanged;
    public event Action<int, int> OnGuestCountChanged;
    public event Action<float> OnArrivalProgressChanged;
    public event Action<ResortNeed, int, int> OnNeedProgressChanged;
    public event Action<WaveNeedSnapshot> OnNeedStateChanged;
    public event Action<ResortNeed, int> OnNeedArrived;
    public event Action<ResortNeed, WaveNeedStatus, int> OnNeedStatusChanged;
    public event Action<ServicePerformance> OnServicePerformanceChanged;
    public event Action<BuildingUpgradeInfo> OnBuildingUpgraded;
    public int TotalGuests { get; private set; }
    public int GuestsArrived { get; private set; }
    public int GuestsLeft { get; private set; }
    public float Elapsed => simulation != null ? simulation.Elapsed : 0f;
    public string ServiceDiagnostics => simulation != null ? simulation.DrainStatus : "Wave not running.";
    public WaveDefinition CurrentWaveDefinition => activeWaveDefinition ?? ResolveNextWaveDefinition();
    public float WaveDuration => balanceConfig != null ? balanceConfig.WaveTimer : 0f;
    public float NeedArrivalWindow => balanceConfig != null ? balanceConfig.NeedArrivalWindow : 0f;
    public bool AreNeedArrivalsClosed => simulation != null && simulation.ArrivalsEnded;

    /// <summary>Copies live demand for UI initialization and refresh after reopening a menu.</summary>
    public void GetNeedSnapshots(List<WaveNeedSnapshot> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (simulation == null) destination.Clear();
        else simulation.GetNeedSnapshots(destination);
    }

    /// <summary>Uses saved building availability; demand generation remains owned by SCRUM-90.</summary>
    public bool IsNeedUnlocked(ResortNeed need)
    {
        if (database == null) return false;
        BuildingStateRecord state = database.GetBuildingState(playerId, BuildingKeyFor(need));
        return state != null && state.is_unlocked == 1;
    }

    private float returnWaitTimer;
    private WaveSimulation simulation;
    private ResortDatabase database;
    private WaveDefinition activeWaveDefinition;
    private readonly HashSet<ResortNeed> activeWaveNeeds = new HashSet<ResortNeed>();
    private bool suddenRushScheduled;
    private bool simulationFinished;
    private string activeWaveCompletionToken;
    private long activeWaveStartedAtUnixSeconds;
    private float finalSatisfaction;
    private int finalRating;
    private int finalCoins;
    private float nextDrainDiagnosticTime;

    private void Awake()
    {
        database = new ResortDatabase();
        database.CreateNewPlayer(playerId);
        WaveDefinition next = ResolveNextWaveDefinition();
        TotalGuests = next != null ? next.VisualGuestCount : 0;
    }

    private void Update()
    {
        if ((CurrentState == GuestWaveState.Active || CurrentState == GuestWaveState.Resolving) && simulation != null)
        {
            ScheduleSuddenRushIfDue(Time.deltaTime);
            simulation.Tick(Time.deltaTime);
            if (simulation.IsActive && simulation.Elapsed >= WaveDuration && simulation.Elapsed >= nextDrainDiagnosticTime)
            {
                nextDrainDiagnosticTime = simulation.Elapsed + 10f;
                Debug.Log($"Wave drain at {simulation.Elapsed:F0}s: {simulation.DrainStatus}; guests returned {GuestsLeft}/{TotalGuests}.", this);
            }
        }
        if ((CurrentState == GuestWaveState.Active || CurrentState == GuestWaveState.Resolving) && simulationFinished)
        {
        returnWaitTimer += Time.deltaTime;
        if (returnWaitTimer >= guestReturnTimeout) FinalizeWave();
        }
    }

    public void StartWaveFromButton()
    {
        WaveDefinition next = ResolveNextWaveDefinition();
        if (next != null) StartWave(next.VisualGuestCount);
        else Debug.LogError("All scripted waves are complete or no WaveDefinition is configured.", this);
    }

    public void StartWave(int guestCount)
    {
        if (CurrentState != GuestWaveState.Ready || guestCount <= 0) return;
        activeWaveDefinition = ResolveNextWaveDefinition();
        if (balanceConfig == null || activeWaveDefinition == null)
        {
            Debug.LogError("Assign both WaveBalanceConfig and WaveDefinition assets before starting a wave.", this);
            return;
        }

        activeWaveCompletionToken = Guid.NewGuid().ToString("N");
        activeWaveStartedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        simulation = new WaveSimulation(balanceConfig);
        simulation.NeedProgressChanged += (need, served, total) => OnNeedProgressChanged?.Invoke(need, served, total);
        simulation.NeedStateChanged += snapshot => OnNeedStateChanged?.Invoke(snapshot);
        simulation.NeedArrived += (need, unitIndex) => OnNeedArrived?.Invoke(need, unitIndex);
        simulation.NeedStatusChanged += (need, status, queue) => OnNeedStatusChanged?.Invoke(need, status, queue);
        simulation.ArrivalProgressChanged += progress => OnArrivalProgressChanged?.Invoke(progress);
        simulation.PerformanceChanged += performance => OnServicePerformanceChanged?.Invoke(performance);
        simulation.Completed += HandleSimulationCompleted;
        activeWaveNeeds.Clear();
        suddenRushScheduled = false;
        if (activeWaveDefinition.Demands != null)
        {
            foreach (NeedDemand demand in activeWaveDefinition.Demands)
                if (demand.units > 0 && IsNeedUnlocked(demand.need)) activeWaveNeeds.Add(demand.need);
        }
        simulation.Start(activeWaveDefinition, activeWaveNeeds);

        foreach (ResortNeed need in activeWaveNeeds) RefreshBuildingPerformance(need);

        TotalGuests = guestCount;
        GuestsArrived = 0;
        GuestsLeft = 0;
        simulationFinished = false;
        nextDrainDiagnosticTime = balanceConfig.WaveTimer;
        ChangeState(GuestWaveState.Active);
        OnWaveStarted?.Invoke(TotalGuests);
        OnGuestCountChanged?.Invoke(GuestsArrived, TotalGuests);
    }

    public void SetBuilding(ResortNeed need, int level, bool staffed)
    {
        simulation?.SetBuilding(need, level, staffed);
    }

    /// <summary>Re-reads the saved assignment and updates this building's live simulation performance.</summary>
    public bool RefreshBuildingPerformance(string buildingKey)
    {
        if (!TryNeedForBuildingKey(buildingKey, out ResortNeed need)) return false;
        return RefreshBuildingPerformance(need);
    }

    /// <summary>Recalculates from the authoritative persisted worker assignment.</summary>
    public bool RefreshBuildingPerformance(ResortNeed need)
    {
        if (database == null || balanceConfig == null) return false;
        BuildingStateRecord savedState = database.GetBuildingState(playerId, BuildingKeyFor(need));
        if (savedState == null) return false;

        if (simulation != null && simulation.IsActive
            && simulation.SetBuilding(need, Mathf.Max(1, savedState.level), savedState.workers_assigned > 0))
            return true; // WaveSimulation raises the performance-changed event immediately.

        float utilization = balanceConfig.UtilizationFor(savedState.workers_assigned > 0);
        var performance = new ServicePerformance(need, savedState.workers_assigned > 0, utilization,
            utilization / balanceConfig.ServiceTime, false, 0f);
        OnServicePerformanceChanged?.Invoke(performance);
        return true;
    }

    /// <summary>Returns current simulation performance, or the configured preparation value.</summary>
    public bool TryGetServicePerformance(ResortNeed need, out ServicePerformance performance)
    {
        if (simulation != null && simulation.TryGetServicePerformance(need, out performance)) return true;
        if (database != null && balanceConfig != null)
        {
            BuildingStateRecord savedState = database.GetBuildingState(playerId, BuildingKeyFor(need));
            if (savedState != null)
            {
                bool staffed = savedState.workers_assigned > 0;
                float utilization = balanceConfig.UtilizationFor(staffed);
                performance = new ServicePerformance(need, staffed, utilization,
                    utilization / balanceConfig.ServiceTime, false, 0f);
                return true;
            }
        }

        performance = default;
        return false;
    }

    /// <summary>Reads the saved building level using the existing balance table.</summary>
    public bool TryGetServicePlaces(ResortNeed need, out int places)
    {
        places = 0;
        if (database == null || balanceConfig == null) return false;
        BuildingStateRecord state = database.GetBuildingState(playerId, BuildingKeyFor(need));
        if (state == null || state.is_unlocked != 1 || state.level < 1) return false;
        places = balanceConfig.CapacityForLevel(state.level);
        return true;
    }

    public int GetConfiguredServicePlacesForLevel(int level) =>
        balanceConfig != null ? balanceConfig.CapacityForLevel(level) : 0;

    public bool CanBoost(ResortNeed need)
    {
        if ((CurrentState != GuestWaveState.Active && CurrentState != GuestWaveState.Resolving)
            || database == null || simulation == null || balanceConfig == null)
            return false;

        return simulation.CanBoost(need, database.GetCurrencyAmount(playerId, "ENERGY"));
    }

    public bool TryBoost(ResortNeed need)
    {
        if ((CurrentState != GuestWaveState.Active && CurrentState != GuestWaveState.Resolving)
            || database == null || simulation == null || balanceConfig == null) return false;
        int energy = database.GetCurrencyAmount(playerId, "ENERGY");
        if (!simulation.CanBoost(need, energy)) return false;
        if (!database.TrySpendCurrency(playerId, "ENERGY", balanceConfig.EnergyCostPerBoost, "WAVE_ENERGY_BOOST")) return false;
        return simulation.TryBoost(need, energy);
    }


    public void GuestArrived()
    {
        if (CurrentState == GuestWaveState.Active && GuestsArrived < TotalGuests)
        {
            GuestsArrived++;
            OnGuestCountChanged?.Invoke(GuestsArrived, TotalGuests);
        }
    }

    public void GuestLeftResort()
    {
        if ((CurrentState != GuestWaveState.Active && CurrentState != GuestWaveState.Resolving) || GuestsLeft >= TotalGuests) return;
        GuestsLeft++;
        if (simulationFinished && GuestsLeft >= TotalGuests) FinalizeWave();
    }

    public void FinishArrivals() { }

    private void HandleSimulationCompleted(float satisfaction, int rating, int coins)
    {
        simulationFinished = true;
        if (CurrentState == GuestWaveState.Active)
            ChangeState(GuestWaveState.Resolving);
        finalSatisfaction = satisfaction;
        finalRating = rating;
        finalCoins = coins;
        OnWaveCompleted?.Invoke();
        if (GuestsLeft >= TotalGuests) FinalizeWave();
    }

    private void FinalizeWave()
    {
        if (CurrentState != GuestWaveState.Active && CurrentState != GuestWaveState.Resolving) return;
        if (database != null && activeWaveDefinition != null)
        {
            int energyReward = balanceConfig != null ? balanceConfig.EnergyRewardForRating(finalRating) : 0;
            bool rewardRecorded = database.RecordWaveCompletionOnce(playerId, activeWaveDefinition.WaveNumber,
                finalRating, finalCoins, energyReward, activeWaveCompletionToken, activeWaveStartedAtUnixSeconds);
            if (!rewardRecorded)
                Debug.LogWarning($"Wave {activeWaveDefinition.WaveNumber} completion reward was already recorded for this run.", this);
        }
        ChangeState(GuestWaveState.Completed);
        OnWaveResult?.Invoke(finalSatisfaction, finalRating, finalCoins);
    }

    private void ChangeState(GuestWaveState newState)
    {
        CurrentState = newState;
        OnWaveStateChanged?.Invoke(newState);
        Debug.Log("Guest Wave State: " + CurrentState, this);
    }

    private static string BuildingKeyFor(ResortNeed need)
    {
        switch (need)
        {
            case ResortNeed.Rooms: return "RECEPTION";
            case ResortNeed.Drinks: return "BEACH_BAR";
            case ResortNeed.Food: return "RESTAURANT";
            case ResortNeed.Relax: return "POOL";
            case ResortNeed.OceanView: return "BUNGALOWS";
            default: throw new ArgumentOutOfRangeException(nameof(need), need, null);
        }
    }

    private static bool TryNeedForBuildingKey(string buildingKey, out ResortNeed need)
    {
        switch (buildingKey)
        {
            case "RECEPTION": need = ResortNeed.Rooms; return true;
            case "BEACH_BAR": need = ResortNeed.Drinks; return true;
            case "RESTAURANT": need = ResortNeed.Food; return true;
            case "POOL": need = ResortNeed.Relax; return true;
            case "BUNGALOWS": need = ResortNeed.OceanView; return true;
            default: need = default; return false;
        }
    }

    public void PrepareNextWave()
    {
        if (CurrentState == GuestWaveState.Completed)
        {
            simulation = null;
            activeWaveDefinition = null;
            WaveDefinition next = ResolveNextWaveDefinition();
            TotalGuests = next != null ? next.VisualGuestCount : 0;
            ChangeState(GuestWaveState.Ready);
        }
    }

    /// <summary>Unlocks a purchased service and assigns its one new Worker in the same save transaction.</summary>
    public bool TryUnlockServiceBuilding(string buildingKey)
    {
        if (database == null || CurrentState == GuestWaveState.Active || CurrentState == GuestWaveState.Resolving
            || CurrentState == GuestWaveState.Paused) return false;
        int coinCost = GetBuildingUnlockCost(buildingKey);
        if (coinCost < 0 || !database.TryUnlockBuilding(playerId, buildingKey, coinCost)) return false;
        RefreshBuildingPerformance(buildingKey);
        return true;
    }

    /// <summary>Whether the selected unlocked building can be upgraded in a preparation or post-wave phase.</summary>
    public bool CanUpgradeServiceBuilding(string buildingKey)
    {
        if (!IsBuildingUpgradePhase || database == null || balanceConfig == null
            || string.IsNullOrWhiteSpace(buildingKey)) return false;

        BuildingStateRecord state = database.GetBuildingState(playerId, buildingKey);
        if (state == null || state.is_unlocked != 1 || state.level < 1 || state.level >= 3) return false;

        int cost = balanceConfig.BuildingUpgradeCost(state.level + 1);
        return cost >= 0 && database.GetCurrencyAmount(playerId, "COINS") >= cost;
    }

    /// <summary>Returns the configured cost of the next legal level, or -1 when no upgrade is available.</summary>
    public int GetBuildingUpgradeCost(string buildingKey)
    {
        if (!IsBuildingUpgradePhase || database == null || balanceConfig == null
            || string.IsNullOrWhiteSpace(buildingKey)) return -1;
        BuildingStateRecord state = database.GetBuildingState(playerId, buildingKey);
        if (state == null || state.is_unlocked != 1 || state.level < 1 || state.level >= 3) return -1;
        return balanceConfig.BuildingUpgradeCost(state.level + 1);
    }

    /// <summary>Purchases exactly one configured level; it is intentionally unavailable during a wave.</summary>
    public bool TryUpgradeServiceBuilding(string buildingKey)
    {
        if (!IsBuildingUpgradePhase || database == null || balanceConfig == null
            || string.IsNullOrWhiteSpace(buildingKey)) return false;

        BuildingStateRecord before = database.GetBuildingState(playerId, buildingKey);
        if (before == null || before.is_unlocked != 1 || before.level < 1 || before.level >= 3) return false;

        int nextLevel = before.level + 1;
        int cost = balanceConfig.BuildingUpgradeCost(nextLevel);
        if (cost < 0 || database.GetCurrencyAmount(playerId, "COINS") < cost
            || !database.TryUpgradeBuilding(playerId, buildingKey, cost)) return false;

        BuildingStateRecord after = database.GetBuildingState(playerId, buildingKey);
        if (after == null || after.level != nextLevel)
            throw new System.InvalidOperationException("The purchased building upgrade was not persisted at its expected level.");

        RefreshBuildingPerformance(buildingKey);
        OnBuildingUpgraded?.Invoke(new BuildingUpgradeInfo(buildingKey, before.level,
            after.level, balanceConfig.CapacityForLevel(after.level), cost));
        return true;
    }

    private bool IsBuildingUpgradePhase => CurrentState == GuestWaveState.Ready
        || CurrentState == GuestWaveState.Completed;

    public int GetBuildingUnlockCost(string buildingKey) =>
        balanceConfig != null ? balanceConfig.BuildingUnlockCost(buildingKey) : -1;

    private WaveDefinition ResolveNextWaveDefinition()
    {
        if (balanceConfig == null) return waveDefinition;
        int nextWaveNumber = database != null ? database.GetHighestWaveCompleted(playerId) + 1 : 1;
        WaveDefinition configured = balanceConfig.GetWaveDefinition(nextWaveNumber);
        if (configured != null) return configured;
        return waveDefinition != null && waveDefinition.WaveNumber == nextWaveNumber ? waveDefinition : null;
    }

    private void ScheduleSuddenRushIfDue(float deltaTime)
    {
        if (suddenRushScheduled || activeWaveDefinition == null || activeWaveDefinition.SuddenRushUnits <= 0
            || simulation == null || simulation.Elapsed + deltaTime < activeWaveDefinition.SuddenRushStartSeconds)
            return;

        if (!TryFindUnstaffedNeed(out ResortNeed rushNeed))
        {
            Debug.LogWarning($"Wave {activeWaveDefinition.WaveNumber} has a Sudden Rush but no unlocked unstaffed service with demand.", this);
            suddenRushScheduled = true;
            return;
        }

        suddenRushScheduled = simulation.AddSuddenRush(rushNeed, activeWaveDefinition.SuddenRushUnits,
            activeWaveDefinition.SuddenRushStartSeconds, activeWaveDefinition.SuddenRushDurationSeconds);
    }

    private bool TryFindUnstaffedNeed(out ResortNeed need)
    {
        foreach (ResortNeed candidate in activeWaveNeeds)
        {
            BuildingStateRecord state = database.GetBuildingState(playerId, BuildingKeyFor(candidate));
            if (state != null && state.is_unlocked == 1 && state.workers_assigned == 0)
            {
                need = candidate;
                return true;
            }
        }
        need = default;
        return false;
    }

    public void PauseWave()
    {
        if (CurrentState == GuestWaveState.Active || CurrentState == GuestWaveState.Resolving) ChangeState(GuestWaveState.Paused);
    }

    public void ResumeWave()
    {
        if (CurrentState == GuestWaveState.Paused)
        {
            // Re-publish saved boost durations and effective rates for HUD listeners.
            simulation?.PublishPerformanceState();
            ChangeState(simulationFinished ? GuestWaveState.Resolving : GuestWaveState.Active);
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) PauseWave();
        else ResumeWave();
    }

    private void OnDestroy()
    {
        database?.Dispose();
    }
}
