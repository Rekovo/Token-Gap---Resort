using System;
using System.Collections.Generic;
using UnityEngine;

public enum WaveNeedStatus { Ok, Bottleneck, Recovering, Satisfied }

public sealed class WaveSimulation
{
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
        public readonly List<float> ServiceProgress = new List<float>();
        public float SatisfactionTotal;
        public float SatisfactionWeight;
        public int ScoredUnits;
        public int LastQueueCount;
        public float QueueGrowthSeconds;
        public WaveNeedStatus Status;
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
    public float Elapsed => time;
    public float ArrivalTimeRemaining => Mathf.Max(0f, config.NeedArrivalWindow - time);
    public string DrainStatus
    {
        get
        {
            var parts = new List<string>(needs.Count);
            foreach (NeedState state in needs)
            {
                int inService = 0;
                var progressValues = new List<string>(state.ServiceProgress.Count);
                foreach (float progress in state.ServiceProgress)
                {
                    if (progress != 0f) inService++;
                    progressValues.Add(progress.ToString("F2"));
                }
                parts.Add($"{state.Need}: {state.Served}/{state.Total} served, {state.Queue.Count} queued, service [{string.Join(",", progressValues)}], rate {ServiceRate(state):F3}/s, staffed {state.Staffed}");
            }
            return string.Join("; ", parts);
        }
    }

    public WaveSimulation(WaveBalanceConfig balanceConfig)
    {
        config = balanceConfig ?? throw new ArgumentNullException(nameof(balanceConfig));
    }

    public void Start(WaveDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (definition.Demands == null || definition.Demands.Length == 0)
            throw new ArgumentException("WaveDefinition must include at least one demand.", nameof(definition));
        needs.Clear();
        foreach (var demand in definition.Demands)
        {
            if (demand.units <= 0) continue;
            var state = new NeedState { Need = demand.need, Total = demand.units, Capacity = config.CapacityForLevel(1) };
            for (int i = 0; i < state.Capacity; i++) state.ServiceProgress.Add(0f);
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
        var state = needs.Find(n => n.Need == need);
        if (!IsActive || state == null) return false;
        int newCapacity = config.CapacityForLevel(level);
        for (int i = newCapacity; i < state.ServiceProgress.Count; i++)
            if (state.ServiceProgress[i] != 0f) return false;
        state.Capacity = newCapacity;
        state.Staffed = staffed;
        while (state.ServiceProgress.Count < state.Capacity) state.ServiceProgress.Add(0f);
        while (state.ServiceProgress.Count > state.Capacity)
        {
            int last = state.ServiceProgress.Count - 1;
            state.ServiceProgress.RemoveAt(last);
        }
        Dispatch(state);
        Notify(state);
        return true;
    }

    public bool TryBoost(ResortNeed need, int energyAvailable)
    {
    var state = needs.Find(n => n.Need == need);
    if (!IsActive || state == null || state.BoostUsed || state.BoostRemaining > 0f
        || energyAvailable < config.EnergyCostPerBoost) return false;
    state.BoostUsed = true;
    state.BoostRemaining = config.EnergyBoostDuration;
    return true;
    }

    public bool CanBoost(ResortNeed need, int energyAvailable)
    {
    var state = needs.Find(n => n.Need == need);
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
            float rate = ServiceRate(state);
            for (int i = 0; i < state.ServiceProgress.Count; i++)
            {
                if (state.ServiceProgress[i] > 0f)
                {
                    state.ServiceProgress[i] -= rate * deltaTime;
                    if (state.ServiceProgress[i] <= 0f)
                        state.ServiceProgress[i] = -1f;
                }
            }
            state.BoostRemaining = Mathf.Max(0f, state.BoostRemaining - deltaTime);
        }

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
        foreach (var state in needs) Notify(state);
    }

    private float ServiceRate(NeedState state)
    {
        float utilization = state.Staffed ? 1f : config.UtilizationEmpty;
        float boost = state.BoostRemaining > 0f ? 1f / (1f - config.EnergySpeedReduction) : 1f;
        return utilization * boost / config.ServiceTime;
    }

    private void AddArrivals()
    {
        foreach (var state in needs)
        {
            while (state.Arrived < state.Total && config.NeedArrivalWindow * state.Arrived / state.Total <= time + 0.0001f)
            {
                state.Queue.Enqueue(config.NeedArrivalWindow * state.Arrived / state.Total);
                state.Arrived++;
            }
        }
    }

    private void CompleteServices(NeedState state)
    {
        for (int i = 0; i < state.ServiceProgress.Count; i++)
        {
            if (state.ServiceProgress[i] < 0f)
            {
                state.ServiceProgress[i] = 0f;
                state.Served++;
            }
        }
    }

    private bool AllNeedsDrained()
    {
        foreach (NeedState state in needs)
        {
            if (state.Arrived < state.Total || state.Queue.Count > 0 || state.Served < state.Total)
                return false;
            foreach (float progress in state.ServiceProgress)
                if (progress != 0f) return false;
        }
        return true;
    }

    private void DispatchAll() { foreach (var state in needs) Dispatch(state); }
    private void Dispatch(NeedState state)
    {
        for (int i = 0; i < state.ServiceProgress.Count && state.Queue.Count > 0; i++)
        {
            if (state.ServiceProgress[i] != 0f) continue;
            float arrivedAt = state.Queue.Dequeue();
            float wait = Mathf.Max(0f, time - arrivedAt);
            float contribution = wait <= config.PatienceGrace ? 1f : wait >= config.PatienceFloorAfter ? config.UnitFloor : Mathf.Lerp(1f, config.UnitFloor, (wait - config.PatienceGrace) / (config.PatienceFloorAfter - config.PatienceGrace));
            float weight = state.Need == ResortNeed.OceanView ? config.OceanViewSatisfactionWeight : 1f;
            state.SatisfactionTotal += Mathf.Max(config.UnitFloor, contribution) * weight;
            state.SatisfactionWeight += weight;
            state.ScoredUnits++;
            state.ServiceProgress[i] = 1f;
        }
    }

    private void UpdateStatus(NeedState state, float deltaTime)
{
    int queueCount = state.Queue.Count;
    if (queueCount > state.LastQueueCount) state.Trend = 1;
    else if (queueCount < state.LastQueueCount) state.Trend = -1;
    state.LastQueueCount = queueCount;

    WaveNeedStatus next = state.Status;
    if (arrivalClosed && queueCount == 0 && state.Served == state.Total)
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

    private void Notify(NeedState state) => NeedProgressChanged?.Invoke(state.Need, state.Served, state.Total);
    private void Complete()
    {
        if (!started || !IsActive) return;
        IsActive = false;
        float weight = 0f, score = 0f;
        int total = 0;
        foreach (var state in needs)
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
