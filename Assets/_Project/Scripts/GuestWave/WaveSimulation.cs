using System;
using System.Collections.Generic;
using UnityEngine;

public enum WaveNeedStatus { Ok, Bottleneck, Recovering, Satisfied }

/// <summary>Processes wave demand independently for each service building.</summary>
public sealed class WaveSimulation
{
    private sealed class ServicePlace
    {
        public bool Occupied;
        // Remaining base-efficiency service work. Keeping work, rather than a
        // percentage tied to the current speed, preserves progress on reassignment.
        public float RemainingWork;
    }

    private sealed class NeedState
    {
        public ResortNeed Need;
        public int Total;
        public int Arrived;
        public int Served;
        public int Capacity;
        public bool Staffed;
        public float BoostRemaining;
        public bool BoostUsed;
        public int Trend;
        public readonly Queue<float> Queue = new Queue<float>();
        public readonly List<ServicePlace> ServicePlaces = new List<ServicePlace>();
        public readonly List<float> WaitingTimeHistory = new List<float>();
        public float SatisfactionTotal;
        public float SatisfactionWeight;
        public int ScoredUnits;
        public int LastQueueCount;
        public float QueueGrowthSeconds;
        public WaveNeedStatus Status;

        public int Waiting => Queue.Count;
        public int InService
        {
            get
            {
                int occupied = 0;
                foreach (ServicePlace place in ServicePlaces)
                    if (place.Occupied) occupied++;
                return occupied;
            }
        }
        public int Outstanding => Waiting + InService;
    }

    private readonly WaveBalanceConfig config;
    private readonly List<NeedState> needs = new List<NeedState>();
    private float time;
    private bool arrivalClosed;
    private bool started;

    public event Action<ResortNeed, int, int> NeedProgressChanged;
    public event Action<ResortNeed, WaveNeedStatus, int> NeedStatusChanged;
    public event Action<float> ArrivalProgressChanged;
    public event Action<float, int, int> Completed;
    public bool IsActive { get; private set; }
    public bool ArrivalsEnded => arrivalClosed;
    public float Elapsed => time;

    /// <summary>Diagnostic state; queue counts exclude guests in service places.</summary>
    public string DrainStatus
    {
        get
        {
            var parts = new List<string>(needs.Count);
            foreach (NeedState state in needs)
            {
                var work = new List<string>(state.ServicePlaces.Count);
                foreach (ServicePlace place in state.ServicePlaces)
                    work.Add(place.Occupied ? place.RemainingWork.ToString("F2") : "empty");
                parts.Add($"{state.Need}: {state.Served}/{state.Total} filled, waiting {state.Waiting}, in_service {state.InService}, outstanding {state.Outstanding}, places [{string.Join(",", work)}], rate {ServiceRate(state):F3}/s, staffed {state.Staffed}");
            }
            return string.Join("; ", parts);
        }
    }

    public WaveSimulation(WaveBalanceConfig balanceConfig)
    {
        config = balanceConfig ?? throw new ArgumentNullException(nameof(balanceConfig));
    }

    public int WaitingCount(ResortNeed need) => FindNeed(need)?.Waiting ?? 0;
    public int InServiceCount(ResortNeed need) => FindNeed(need)?.InService ?? 0;
    public int OutstandingCount(ResortNeed need) => FindNeed(need)?.Outstanding ?? 0;
    public int FilledCount(ResortNeed need) => FindNeed(need)?.Served ?? 0;

    /// <summary>Returns an immutable snapshot of completed waiting durations for final scoring.</summary>
    public IReadOnlyList<float> GetWaitingTimeHistory(ResortNeed need)
    {
        NeedState state = FindNeed(need);
        return state == null ? Array.Empty<float>() : state.WaitingTimeHistory.ToArray();
    }

    public void Start(WaveDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (definition.Demands == null || definition.Demands.Length == 0)
            throw new ArgumentException("WaveDefinition must include at least one demand.", nameof(definition));

        needs.Clear();
        foreach (NeedDemand demand in definition.Demands)
        {
            if (demand.units <= 0) continue;
            var state = new NeedState { Need = demand.need, Total = demand.units, Capacity = config.CapacityForLevel(1) };
            for (int i = 0; i < state.Capacity; i++) state.ServicePlaces.Add(new ServicePlace());
            needs.Add(state);
        }

        time = 0f;
        arrivalClosed = false;
        started = true;
        IsActive = needs.Count > 0;
        AddArrivals();
        DispatchAll();
        if (!IsActive) Complete();
    }

    public bool SetBuilding(ResortNeed need, int level, bool staffed)
    {
        NeedState state = FindNeed(need);
        if (!IsActive || state == null) return false;

        int newCapacity = config.CapacityForLevel(level);
        // Do not remove occupied places when downgrading; callers may retry once
        // those guests have completed service.
        for (int i = newCapacity; i < state.ServicePlaces.Count; i++)
            if (state.ServicePlaces[i].Occupied) return false;

        state.Capacity = newCapacity;
        state.Staffed = staffed;
        while (state.ServicePlaces.Count < state.Capacity) state.ServicePlaces.Add(new ServicePlace());
        while (state.ServicePlaces.Count > state.Capacity) state.ServicePlaces.RemoveAt(state.ServicePlaces.Count - 1);
        Dispatch(state);
        Notify(state);
        return true;
    }

    public bool TryBoost(ResortNeed need, int energyAvailable)
    {
        NeedState state = FindNeed(need);
        if (!CanBoost(need, energyAvailable)) return false;
        state.BoostUsed = true;
        state.BoostRemaining = config.EnergyBoostDuration;
        return true;
    }

    public bool CanBoost(ResortNeed need, int energyAvailable)
    {
        NeedState state = FindNeed(need);
        return IsActive && state != null && !state.BoostUsed && state.BoostRemaining <= 0f
            && energyAvailable >= config.EnergyCostPerBoost;
    }

    public void Tick(float deltaTime)
    {
        if (!IsActive || deltaTime <= 0f) return;

        float previousTime = time;
        time += deltaTime;

        foreach (NeedState state in needs)
        {
            ProcessServices(state, deltaTime);
            state.BoostRemaining = Mathf.Max(0f, state.BoostRemaining - deltaTime);
        }

        // Release every scheduled arrival crossed by this frame before draining
        // newly freed places. The queue retains each unit's scheduled arrival time.
        AddArrivals();
        foreach (NeedState state in needs)
        {
            CompleteServices(state);
            Dispatch(state);
            UpdateStatus(state, time - previousTime);
        }

        if (!arrivalClosed && time >= config.NeedArrivalWindow)
        {
            arrivalClosed = true;
            ArrivalProgressChanged?.Invoke(1f);
        }
        else if (!arrivalClosed)
            ArrivalProgressChanged?.Invoke(Mathf.Clamp01(time / config.NeedArrivalWindow));

        if (arrivalClosed && AllNeedsDrained()) Complete();
        foreach (NeedState state in needs) Notify(state);
    }

    private void ProcessServices(NeedState state, float deltaTime)
    {
        float boostedSeconds = Mathf.Min(deltaTime, state.BoostRemaining);
        float normalSeconds = deltaTime - boostedSeconds;
        float utilization = state.Staffed ? 1f : config.UtilizationEmpty;
        float boostedRate = utilization * BoostMultiplier();
        float normalRate = utilization;

        foreach (ServicePlace place in state.ServicePlaces)
        {
            if (!place.Occupied) continue;
            place.RemainingWork -= boostedRate * boostedSeconds + normalRate * normalSeconds;
        }
    }

    private float BoostMultiplier() => 1f / (1f - config.EnergySpeedReduction);
    private float ServiceRate(NeedState state) => (state.Staffed ? 1f : config.UtilizationEmpty)
        * (state.BoostRemaining > 0f ? BoostMultiplier() : 1f) / config.ServiceTime;

    private void AddArrivals()
    {
        foreach (NeedState state in needs)
        {
            while (state.Arrived < state.Total)
            {
                float arrivalTime = config.NeedArrivalWindow * state.Arrived / state.Total;
                if (arrivalTime > time + 0.0001f) break;
                state.Queue.Enqueue(arrivalTime);
                state.Arrived++;
            }
        }
    }

    private void CompleteServices(NeedState state)
    {
        foreach (ServicePlace place in state.ServicePlaces)
        {
            if (!place.Occupied || place.RemainingWork > 0f) continue;
            place.Occupied = false;
            place.RemainingWork = 0f;
            state.Served++;
        }
    }

    private bool AllNeedsDrained()
    {
        foreach (NeedState state in needs)
            if (state.Arrived < state.Total || state.Waiting != 0 || state.InService != 0 || state.Served != state.Total)
                return false;
        return true;
    }

    private void DispatchAll() { foreach (NeedState state in needs) Dispatch(state); }

    private void Dispatch(NeedState state)
    {
        foreach (ServicePlace place in state.ServicePlaces)
        {
            if (state.Queue.Count == 0) break;
            if (place.Occupied) continue;

            float arrivedAt = state.Queue.Dequeue();
            float wait = Mathf.Max(0f, time - arrivedAt);
            state.WaitingTimeHistory.Add(wait);
            float contribution = wait <= config.PatienceGrace ? 1f
                : wait >= config.PatienceFloorAfter ? config.UnitFloor
                : Mathf.Lerp(1f, config.UnitFloor,
                    (wait - config.PatienceGrace) / (config.PatienceFloorAfter - config.PatienceGrace));
            float weight = state.Need == ResortNeed.OceanView ? config.OceanViewSatisfactionWeight : 1f;
            state.SatisfactionTotal += Mathf.Max(config.UnitFloor, contribution) * weight;
            state.SatisfactionWeight += weight;
            state.ScoredUnits++;
            place.Occupied = true;
            place.RemainingWork = config.ServiceTime;
        }
    }

    private void UpdateStatus(NeedState state, float deltaTime)
    {
        int queueCount = state.Waiting;
        if (queueCount > state.LastQueueCount) state.Trend = 1;
        else if (queueCount < state.LastQueueCount) state.Trend = -1;
        state.LastQueueCount = queueCount;

        WaveNeedStatus next = state.Status;
        if (arrivalClosed && queueCount == 0 && state.InService == 0 && state.Served == state.Total)
        {
            next = WaveNeedStatus.Satisfied;
            state.QueueGrowthSeconds = 0f;
        }
        else if (queueCount <= config.BottleneckThreshold)
        {
            next = WaveNeedStatus.Ok;
            state.QueueGrowthSeconds = 0f;
            state.Trend = 0;
        }
        else if (state.Trend > 0)
        {
            state.QueueGrowthSeconds += deltaTime;
            if (state.QueueGrowthSeconds >= config.BottleneckHysteresisSeconds)
                next = WaveNeedStatus.Bottleneck;
        }
        else if (state.Trend < 0)
        {
            state.QueueGrowthSeconds = 0f;
            if (state.Status == WaveNeedStatus.Bottleneck || state.Status == WaveNeedStatus.Recovering)
                next = WaveNeedStatus.Recovering;
        }

        if (next == state.Status) return;
        state.Status = next;
        NeedStatusChanged?.Invoke(state.Need, next, queueCount);
    }

    private NeedState FindNeed(ResortNeed need) => needs.Find(n => n.Need == need);
    private void Notify(NeedState state) => NeedProgressChanged?.Invoke(state.Need, state.Served, state.Total);

    private void Complete()
    {
        if (!started || !IsActive) return;
        IsActive = false;
        float weight = 0f, score = 0f;
        int total = 0;
        foreach (NeedState state in needs)
        {
            float needWeight = state.Need == ResortNeed.OceanView ? config.OceanViewSatisfactionWeight : 1f;
            int unscoredUnits = Mathf.Max(0, state.Total - state.ScoredUnits);
            score += state.SatisfactionTotal + unscoredUnits * config.UnitFloor * needWeight;
            weight += state.SatisfactionWeight + unscoredUnits * needWeight;
            total += state.Total;
        }
        float satisfaction = Mathf.Max(config.WaveFloor, weight > 0f ? score / weight : config.WaveFloor);
        int rating = 1;
        for (int i = 0; i < 6; i++) if (satisfaction >= config.RatingThreshold(i)) rating = i + 1;
        int coins = Mathf.RoundToInt(total * config.CoinsPerUnit * config.CoinsRatingMultiplier(rating - 1));
        Completed?.Invoke(satisfaction, rating, coins);
    }
}
