using System;
using UnityEngine;

public enum ResortNeed { Rooms, Drinks, Food, Relax, OceanView }

[Serializable]
public struct NeedDemand
{
    public ResortNeed need;
    [Min(0)] public int units;
}

[CreateAssetMenu(menuName = "ResortEmpire/Wave Definition", fileName = "WaveDefinition")]
public sealed class WaveDefinition : ScriptableObject
{
    [SerializeField, Min(1)] private int waveNumber = 1;
    [SerializeField, Min(1)] private int visualGuestCount = 12;
    [SerializeField] private NeedDemand[] demands =
    {
        new NeedDemand { need = ResortNeed.Rooms, units = 8 },
        new NeedDemand { need = ResortNeed.Drinks, units = 8 },
        new NeedDemand { need = ResortNeed.Food, units = 6 }
    };
    [Header("Optional wave event")]
    [SerializeField, Min(0)] private int suddenRushUnits;
    [SerializeField, Min(0f)] private float suddenRushStartSeconds = 45f;
    [SerializeField, Min(0.1f)] private float suddenRushDurationSeconds = 10f;

    public int WaveNumber => waveNumber;
    public int VisualGuestCount => visualGuestCount;
    public NeedDemand[] Demands => demands;
    public int SuddenRushUnits => suddenRushUnits;
    public float SuddenRushStartSeconds => suddenRushStartSeconds;
    public float SuddenRushDurationSeconds => suddenRushDurationSeconds;
    public int TotalUnits
    {
        get { int total = Mathf.Max(0, suddenRushUnits); if (demands != null) foreach (var demand in demands) total += Mathf.Max(0, demand.units); return total; }
    }

    private void OnValidate()
    {
        if (demands == null || demands.Length == 0)
        {
            Debug.LogError("A wave needs at least one positive service demand.", this);
            return;
        }
        int total = 0;
        foreach (var demand in demands) total += Mathf.Max(0, demand.units);
        if (total == 0) Debug.LogError("A wave needs at least one positive service demand.", this);
        for (int i = 0; i < demands.Length; i++)
            for (int j = i + 1; j < demands.Length; j++)
                if (demands[i].need == demands[j].need)
                    Debug.LogError("A wave definition can contain each need only once.", this);
    }
}
