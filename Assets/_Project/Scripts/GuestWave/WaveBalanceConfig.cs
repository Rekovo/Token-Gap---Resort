using UnityEngine;
using System;

[CreateAssetMenu(menuName = "ResortEmpire/Wave Balance Config", fileName = "WaveBalanceConfig")]
public sealed class WaveBalanceConfig : ScriptableObject
{
    [Header("Wave timing")]
    [SerializeField, Min(1f)] private float waveTimer = 90f;
    [Header("Scripted waves")]
    [SerializeField] private WaveDefinition[] scriptedWaves = Array.Empty<WaveDefinition>();
    [Header("Building unlocks")]
    [SerializeField, Min(0)] private int poolUnlockCost = 1200;
    [SerializeField, Min(0)] private int bungalowsUnlockCost = 2500;
    [Header("Building service")]
    [SerializeField, Min(0.1f)] private float serviceTime = 12f;
    [SerializeField, Range(0.05f, 1f)] private float utilizationStaffed = 1f;
    [SerializeField, Range(0.05f, 0.6f)] private float utilizationEmpty = 0.33f;
    [SerializeField] private int[] capacityPerLevel = { 3, 5, 7 };
    [Header("Building upgrades")]
    [Tooltip("Coin cost indexed by target building level: Level 2, then Level 3.")]
    [SerializeField, Min(0)] private int[] upgradeCostsByTargetLevel = { 500, 1200 };
    [Header("Bottleneck")]
    [SerializeField, Min(1)] private int bottleneckThreshold = 4;
    [SerializeField, Min(0f)] private float bottleneckHysteresisSeconds = 3f;
    [Header("Satisfaction")]
    [SerializeField, Min(0f)] private float patienceGrace = 10f;
    [SerializeField, Min(0f)] private float patienceFloorAfter = 30f;
    [SerializeField, Range(0f, 1f)] private float unitFloor = 0.25f;
    [SerializeField, Range(0f, 1f)] private float waveFloor = 0.40f;
    [SerializeField] private float[] ratingThresholds = { 0.40f, 0.55f, 0.70f, 0.80f, 0.90f, 1f };
    [SerializeField] private float oceanViewSatisfactionWeight = 1.5f;
    [Header("Energy boost (Mode A)")]
    [SerializeField, Min(0)] private int energyCostPerBoost = 3;
    [SerializeField, Range(0f, 0.95f)] private float energySpeedReduction = 0.5f;
    [SerializeField, Min(0.1f)] private float energyBoostDuration = 15f;
    [Header("Rewards")]
    [SerializeField, Min(0)] private int coinsPerUnit = 16;
    [SerializeField] private float[] coinsRatingMultipliers = { 0.4f, 0.6f, 0.8f, 1f, 1.2f, 1.3f };
    [Header("Energy wave rewards")]
    [Tooltip("TEAM ASSUMPTION: the Build Document lists five Energy reward values for six rating outcomes. This provisional six-rating mapping follows the current team-approved ticket values.")]
    [SerializeField] private int[] energyRewardsByRating = { 0, 1, 2, 3, 5, 5 };

    public float WaveTimer => waveTimer;
    public WaveDefinition GetWaveDefinition(int waveNumber)
    {
        if (scriptedWaves == null) return null;
        foreach (WaveDefinition definition in scriptedWaves)
            if (definition != null && definition.WaveNumber == waveNumber) return definition;
        return null;
    }
    public int ScriptedWaveCount => scriptedWaves == null ? 0 : scriptedWaves.Length;
    public int BuildingUnlockCost(string buildingKey)
    {
        switch (buildingKey)
        {
            case "POOL": return poolUnlockCost;
            case "BUNGALOWS": return bungalowsUnlockCost;
            default: return -1;
        }
    }
    public float NeedArrivalWindow => waveTimer;
    public float ServiceTime => serviceTime;
    public float UtilizationStaffed => utilizationStaffed;
    public float UtilizationEmpty => utilizationEmpty;
    public float UtilizationFor(bool staffed) => staffed ? utilizationStaffed : utilizationEmpty;
    public float EnergyBoostMultiplier => 1f / Mathf.Max(0.05f, 1f - energySpeedReduction);
    public int BottleneckThreshold => bottleneckThreshold;
    public float BottleneckHysteresisSeconds => bottleneckHysteresisSeconds;
    public float PatienceGrace => patienceGrace;
    public float PatienceFloorAfter => patienceFloorAfter;
    public float UnitFloor => unitFloor;
    public float WaveFloor => waveFloor;
    public float OceanViewSatisfactionWeight => oceanViewSatisfactionWeight;
    public int EnergyCostPerBoost => energyCostPerBoost;
    public float EnergySpeedReduction => energySpeedReduction;
    public float EnergyBoostDuration => energyBoostDuration;
    public int CoinsPerUnit => coinsPerUnit;
    public int BuildingUpgradeCost(int targetLevel)
    {
        if (targetLevel < 2 || targetLevel > 3 || upgradeCostsByTargetLevel == null
            || upgradeCostsByTargetLevel.Length != 2) return -1;
        return upgradeCostsByTargetLevel[targetLevel - 2];
    }
    public int EnergyRewardForRating(int rating)
    {
        if (energyRewardsByRating == null || energyRewardsByRating.Length != 6) return 0;
        return energyRewardsByRating[Mathf.Clamp(rating - 1, 0, 5)];
    }
    public int CapacityForLevel(int level) => capacityPerLevel[Mathf.Clamp(level - 1, 0, capacityPerLevel.Length - 1)];
    public float RatingThreshold(int index) => ratingThresholds[Mathf.Clamp(index, 0, ratingThresholds.Length - 1)];
    public float CoinsRatingMultiplier(int index) => coinsRatingMultipliers[Mathf.Clamp(index, 0, coinsRatingMultipliers.Length - 1)];

    private void OnValidate()
    {
        if (scriptedWaves == null || scriptedWaves.Length != 8)
            Debug.LogError("The prototype balance config must reference all eight scripted waves.", this);
        else
            for (int i = 0; i < scriptedWaves.Length; i++)
                if (scriptedWaves[i] == null || scriptedWaves[i].WaveNumber != i + 1)
                    Debug.LogError($"Scripted wave slot {i + 1} must reference Wave {i + 1}.", this);

        if (capacityPerLevel == null || capacityPerLevel.Length != 3 || capacityPerLevel[0] != 3
            || capacityPerLevel[1] != 5 || capacityPerLevel[2] != 7)
            Debug.LogError("Prototype building capacities must be 3, 5, and 7 service places for levels 1, 2, and 3.", this);
        if (upgradeCostsByTargetLevel == null || upgradeCostsByTargetLevel.Length != 2
            || upgradeCostsByTargetLevel[0] < 0 || upgradeCostsByTargetLevel[1] < 0)
            Debug.LogError("Building upgrade costs must configure non-negative costs for target levels 2 and 3.", this);
        if (ratingThresholds == null || ratingThresholds.Length != 6 || coinsRatingMultipliers == null || coinsRatingMultipliers.Length != 6)
            Debug.LogError("Rating thresholds and reward multipliers must each have six entries.", this);
        else
        {
            if (unitFloor >= waveFloor || waveFloor >= ratingThresholds[2])
                Debug.LogError("WR1 violated: unitFloor < waveFloor < the 3-star threshold.", this);
            for (int i = 1; i < ratingThresholds.Length; i++)
                if (ratingThresholds[i] <= ratingThresholds[i - 1])
                    Debug.LogError("Rating thresholds must increase strictly from 1 star to Perfect.", this);
            foreach (float multiplier in coinsRatingMultipliers)
                if (multiplier < 0f) Debug.LogError("Coin rating multipliers cannot be negative.", this);
        }
        if (patienceFloorAfter <= patienceGrace)
            Debug.LogError("patienceFloorAfter must be greater than patienceGrace.", this);
        if (energyRewardsByRating == null || energyRewardsByRating.Length != 6)
            Debug.LogError("The provisional Energy reward mapping must contain one non-negative value for each of the six ratings.", this);
        else
            foreach (int reward in energyRewardsByRating)
                if (reward < 0) Debug.LogError("Energy wave rewards cannot be negative.", this);
        if (utilizationEmpty <= 0f)
            Debug.LogError("WR2 violated: utilizationEmpty must be greater than zero.", this);
        if (utilizationStaffed <= 0f)
            Debug.LogError("utilizationStaffed must be greater than zero.", this);
    }
}
