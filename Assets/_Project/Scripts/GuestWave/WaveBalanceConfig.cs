using UnityEngine;

[CreateAssetMenu(menuName = "ResortEmpire/Wave Balance Config", fileName = "WaveBalanceConfig")]
public sealed class WaveBalanceConfig : ScriptableObject
{
    [Header("Wave timing")]
    [SerializeField, Min(1f)] private float waveTimer = 90f;
    [Header("Building service")]
    [SerializeField, Min(0.1f)] private float serviceTime = 12f;
    [SerializeField, Range(0.05f, 0.6f)] private float utilizationEmpty = 0.33f;
    [SerializeField] private int[] capacityPerLevel = { 3, 5, 7 };
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

    public float WaveTimer => waveTimer;
    public float NeedArrivalWindow => waveTimer;
    public float ServiceTime => serviceTime;
    public float UtilizationEmpty => utilizationEmpty;
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
    public int CapacityForLevel(int level) => capacityPerLevel[Mathf.Clamp(level - 1, 0, capacityPerLevel.Length - 1)];
    public float RatingThreshold(int index) => ratingThresholds[Mathf.Clamp(index, 0, ratingThresholds.Length - 1)];
    public float CoinsRatingMultiplier(int index) => coinsRatingMultipliers[Mathf.Clamp(index, 0, coinsRatingMultipliers.Length - 1)];

    private void OnValidate()
    {
        if (capacityPerLevel == null || capacityPerLevel.Length != 3 || capacityPerLevel[0] <= 0 || capacityPerLevel[1] <= capacityPerLevel[0] || capacityPerLevel[2] <= capacityPerLevel[1])
            Debug.LogError("Prototype capacity must contain three increasing positive levels (3, 5, 7).", this);
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
        if (utilizationEmpty <= 0f)
            Debug.LogError("WR2 violated: utilizationEmpty must be greater than zero.", this);
    }
}
