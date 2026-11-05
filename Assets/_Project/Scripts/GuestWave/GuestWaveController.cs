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
    public int TotalGuests { get; private set; }
    public int GuestsArrived { get; private set; }
    public int GuestsLeft { get; private set; }
    public float Elapsed => simulation != null ? simulation.Elapsed : 0f;
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
        if (CurrentState == GuestWaveState.Active && simulation != null)
        {
            simulation.Tick(Time.deltaTime);
            if (simulation.IsActive && simulation.Elapsed >= WaveDuration && simulation.Elapsed >= nextDrainDiagnosticTime)
            {
                nextDrainDiagnosticTime = simulation.Elapsed + 10f;
                Debug.Log($"Wave drain at {simulation.Elapsed:F0}s: {simulation.DrainStatus}; guests returned {GuestsLeft}/{TotalGuests}.", this);
            }
        }
        if (CurrentState == GuestWaveState.Active && simulationFinished)
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
        simulation.Completed += HandleSimulationCompleted;
        simulation.Start(waveDefinition);
        if (database != null && waveDefinition.Demands != null)
        {
            foreach (var demand in waveDefinition.Demands)
            {
                string key = BuildingKeyFor(demand.need);
                BuildingStateRecord state = database.GetBuildingState(playerId, key);
                if (state != null) simulation.SetBuilding(demand.need, Mathf.Max(1, state.level), state.workers_assigned > 0);
            }
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

    public bool TryBoost(ResortNeed need)
    {
        if (database == null || simulation == null || balanceConfig == null) return false;
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
        if (CurrentState != GuestWaveState.Active || GuestsLeft >= TotalGuests) return;
        GuestsLeft++;
        if (simulationFinished && GuestsLeft >= TotalGuests) FinalizeWave();
    }

    public void FinishArrivals() { }

    private void HandleSimulationCompleted(float satisfaction, int rating, int coins)
    {
        simulationFinished = true;
        finalSatisfaction = satisfaction;
        finalRating = rating;
        finalCoins = coins;
        OnWaveCompleted?.Invoke();
        if (GuestsLeft >= TotalGuests) FinalizeWave();
    }

    private void FinalizeWave()
    {
        if (CurrentState != GuestWaveState.Active) return;
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
        if (CurrentState == GuestWaveState.Active) ChangeState(GuestWaveState.Paused);
    }

    public void ResumeWave()
    {
        if (CurrentState == GuestWaveState.Paused) ChangeState(GuestWaveState.Active);
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
