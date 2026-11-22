using UnityEngine;

/// <summary>Shared names and colors for wave need displays.</summary>
public static class WaveNeedPresentation
{
    public static readonly ResortNeed[] StableOrder =
    {
        ResortNeed.Rooms,
        ResortNeed.Drinks,
        ResortNeed.Food,
        ResortNeed.Relax,
        ResortNeed.OceanView
    };

    public static string NameFor(ResortNeed need)
    {
        switch (need)
        {
            case ResortNeed.Rooms: return "Rooms";
            case ResortNeed.Drinks: return "Drinks";
            case ResortNeed.Food: return "Food";
            case ResortNeed.Relax: return "Relax";
            case ResortNeed.OceanView: return "Ocean View";
            default: return need.ToString();
        }
    }

    public static Color ColorFor(ResortNeed need)
    {
        switch (need)
        {
            case ResortNeed.Rooms: return new Color(0.48f, 0.86f, 0.75f);
            case ResortNeed.Drinks: return new Color(0.45f, 0.79f, 0.96f);
            case ResortNeed.Food: return new Color(1f, 0.72f, 0.45f);
            case ResortNeed.Relax: return new Color(0.67f, 0.86f, 0.56f);
            case ResortNeed.OceanView: return new Color(1f, 0.83f, 0.43f);
            default: return Color.white;
        }
    }
}
