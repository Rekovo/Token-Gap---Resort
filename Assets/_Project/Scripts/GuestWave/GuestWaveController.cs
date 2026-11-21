using System;
using UnityEngine;

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
    public event Action<ResortNeed, WaveNeedStatus, int> OnNeedStatusChanged;
    public event Action<ServicePerformance> OnServicePerformanceChanged;
    public int TotalGuests { get; private set; }
    public int GuestsArrived { get; private set; }
    public int GuestsLeft { get; private set; }
    public float Elapsed => simulation != null ? simulation.Elapsed : 0f;
    public string ServiceDiagnostics => simulation != null ? simulation.DrainStatus : "Wave not running.";
    public WaveDefinition CurrentWaveDefinition => waveDefinition;
    public float WaveDuration => balanceConfig != null ? balanceConfig.WaveTimer : 0f;
    public float NeedArrivalWindow => balanceConfig != null ? balanceConfig.NeedArrivalWindow : 0f;

    private float returnWaitTimer;
    private WaveSimulation simulation;
    private ResortDatabase database;
    private bool simulationFinished;
    private float finalSatisfaction;
    private int finalRating;
    private int finalCoins;
    private float nextDrainDiagnosticTime;

    private void Awake()
    {
        database = new ResortDatabase();
        database.CreateNewPlayer(playerId);
        TotalGuests = waveDefinition != null ? waveDefinition.VisualGuestCount : 0;
    }

    private void Update()
    {
        if ((CurrentState == GuestWaveState.Active || CurrentState == GuestWaveState.Resolving) && simulation != null)
        {
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
        if (waveDefinition != null) StartWave(waveDefinition.VisualGuestCount);
        else Debug.LogError("GuestWaveController requires a WaveDefinition asset.", this);
    }

    public void StartWave(int guestCount)
    {
        if (CurrentState != GuestWaveState.Ready || guestCount <= 0) return;
        if (balanceConfig == null || waveDefinition == null)
        {
            Debug.LogError("Assign both WaveBalanceConfig and WaveDefinition assets before starting a wave.", this);
            return;
        }

        simulation = new WaveSimulation(balanceConfig);
        simulation.NeedProgressChanged += (need, served, total) => OnNeedProgressChanged?.Invoke(need, served, total);
        simulation.NeedStatusChanged += (need, status, queue) => OnNeedStatusChanged?.Invoke(need, status, queue);
        simulation.ArrivalProgressChanged += progress => OnArrivalProgressChanged?.Invoke(progress);
        simulation.PerformanceChanged += performance => OnServicePerformanceChanged?.Invoke(performance);
        simulation.Completed += HandleSimulationCompleted;
        simulation.Start(waveDefinition);
        if (database != null && waveDefinition.Demands != null)
        {
            foreach (var demand in waveDefinition.Demands)
                RefreshBuildingPerformance(demand.need);
        }

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
        if (database != null && waveDefinition != null)
            database.RecordWaveCompletion(playerId, waveDefinition.WaveNumber, finalCoins);
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
            ChangeState(GuestWaveState.Ready);
        }
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
