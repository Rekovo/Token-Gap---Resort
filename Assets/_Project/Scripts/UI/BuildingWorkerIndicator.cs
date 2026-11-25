using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows saved staffing plus live service occupancy and bottleneck status.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BuildingSlot))]
public class BuildingWorkerIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BuildingSlot building;
    [SerializeField] private GameObject staffedMarker;
    [SerializeField] private GameObject unstaffedMarker;
    [Header("Capacity ring")]
    [SerializeField, Min(48f)] private float ringDiameter = 116f;
    [SerializeField, Min(2f)] private float ringThickness = 8f;
    [SerializeField, Range(0f, 0.1f)] private float bottleneckPulse = 0.035f;

    private readonly List<WaveNeedSnapshot> snapshots = new List<WaveNeedSnapshot>(5);
    private GuestWaveController controller;
    private SegmentedCapacityRingGraphic staffedRing;
    private SegmentedCapacityRingGraphic unstaffedRing;
    private RectTransform staffedRingRect;
    private RectTransform unstaffedRingRect;
    private bool? lastUnlocked;
    private bool? lastStaffed;
    private ResortNeed need;
    private bool hasNeed;
    private int capacity;
    private int inService;
    private bool isBottleneck;

    private void Awake()
    {
        if (building == null) building = GetComponent<BuildingSlot>();
        hasNeed = TryNeedForBuilding(building != null ? building.BuildingKey : null, out need);
    }

    private void Start()
    {
        controller = FindAnyObjectByType<GuestWaveController>();
        CreateCapacityRings();
        if (controller != null)
        {
            controller.OnWaveStateChanged += HandleWaveStateChanged;
            controller.OnNeedStateChanged += HandleNeedStateChanged;
            controller.OnNeedStatusChanged += HandleNeedStatusChanged;
            controller.OnBuildingUpgraded += HandleBuildingUpgraded;
        }
        RefreshFromSave();
        RefreshLiveSnapshot();
    }

    private void OnDestroy()
    {
        if (controller == null) return;
        controller.OnWaveStateChanged -= HandleWaveStateChanged;
        controller.OnNeedStateChanged -= HandleNeedStateChanged;
        controller.OnNeedStatusChanged -= HandleNeedStatusChanged;
        controller.OnBuildingUpgraded -= HandleBuildingUpgraded;
    }

    private void LateUpdate()
    {
        bool unlocked = building != null && building.IsUnlocked;
        bool staffed = building != null && building.HasWorker;
        if (lastUnlocked != unlocked || lastStaffed != staffed)
        {
            lastUnlocked = unlocked;
            lastStaffed = staffed;
            ShowWorkerState(unlocked, staffed);
        }

        float scale = isBottleneck
            ? 1f + Mathf.Sin(Time.unscaledTime * 3.3f) * bottleneckPulse
            : 1f;
        if (staffedRingRect != null) staffedRingRect.localScale = Vector3.one * scale;
        if (unstaffedRingRect != null) unstaffedRingRect.localScale = Vector3.one * scale;
    }

    /// <summary>Can be called after loading or changing saved assignments.</summary>
    public void RefreshFromSave()
    {
        if (building == null) return;
        building.RefreshPersistedState();
        lastUnlocked = null;
        lastStaffed = null;
        RefreshCapacityFromSave();
    }

    private void CreateCapacityRings()
    {
        staffedRing = CreateRing(staffedMarker, "StaffedCapacityRing", out staffedRingRect);
        unstaffedRing = CreateRing(unstaffedMarker, "UnstaffedCapacityRing", out unstaffedRingRect);
        RefreshCapacityFromSave();
    }

    private SegmentedCapacityRingGraphic CreateRing(GameObject marker, string objectName,
        out RectTransform ringRect)
    {
        ringRect = null;
        if (marker == null) return null;

        GameObject ringObject = new GameObject(objectName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(SegmentedCapacityRingGraphic));
        ringObject.transform.SetParent(marker.transform, false);
        ringRect = (RectTransform)ringObject.transform;
        ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 0.5f);
        ringRect.pivot = new Vector2(0.5f, 0.5f);
        ringRect.anchoredPosition = new Vector2(4f, 8f);
        ringRect.sizeDelta = new Vector2(ringDiameter, ringDiameter);
        ringRect.SetSiblingIndex(Mathf.Min(1, marker.transform.childCount - 1));

        SegmentedCapacityRingGraphic graphic = ringObject.GetComponent<SegmentedCapacityRingGraphic>();
        graphic.raycastTarget = false;
        graphic.Thickness = ringThickness;
        return graphic;
    }

    private void ShowWorkerState(bool unlocked, bool staffed)
    {
        if (staffedMarker != null) staffedMarker.SetActive(unlocked && staffed);
        if (unstaffedMarker != null) unstaffedMarker.SetActive(unlocked && !staffed);
    }

    private void RefreshLiveSnapshot()
    {
        if (controller == null || !hasNeed)
        {
            inService = 0;
            isBottleneck = false;
            RefreshCapacityRing();
            return;
        }

        if (controller.CurrentState == GuestWaveState.Ready || controller.CurrentState == GuestWaveState.Completed)
        {
            inService = 0;
            isBottleneck = false;
            RefreshCapacityRing();
            return;
        }

        controller.GetNeedSnapshots(snapshots);
        foreach (WaveNeedSnapshot snapshot in snapshots)
        {
            if (snapshot.Need != need) continue;
            ApplySnapshot(snapshot);
            return;
        }
        inService = 0;
        isBottleneck = false;
        RefreshCapacityRing();
    }

    private void ApplySnapshot(WaveNeedSnapshot snapshot)
    {
        inService = snapshot.Processing;
        isBottleneck = snapshot.Status == WaveNeedStatus.Bottleneck;
        RefreshCapacityRing();
    }

    private void RefreshCapacityRing()
    {
        bool unlocked = building != null && building.IsUnlocked;
        inService = Mathf.Clamp(inService, 0, capacity);
        if (staffedRing != null)
        {
            staffedRing.gameObject.SetActive(unlocked);
            staffedRing.SetState(capacity, inService, isBottleneck);
        }
        if (unstaffedRing != null)
        {
            unstaffedRing.gameObject.SetActive(unlocked);
            unstaffedRing.SetState(capacity, inService, isBottleneck);
        }
    }

    private void RefreshCapacityFromSave()
    {
        bool unlocked = building != null && building.IsUnlocked;
        capacity = unlocked && controller != null && hasNeed
            && controller.TryGetServicePlaces(need, out int count) ? count : 0;
        RefreshCapacityRing();
    }

    private void HandleNeedStateChanged(WaveNeedSnapshot snapshot)
    {
        if (hasNeed && snapshot.Need == need) ApplySnapshot(snapshot);
    }

    private void HandleNeedStatusChanged(ResortNeed changedNeed, WaveNeedStatus status, int _)
    {
        if (!hasNeed || changedNeed != need) return;
        isBottleneck = status == WaveNeedStatus.Bottleneck;
        RefreshCapacityRing();
    }

    private void HandleBuildingUpgraded(BuildingUpgradeInfo info)
    {
        if (building == null || info.BuildingKey != building.BuildingKey) return;
        building.RefreshPersistedState();
        RefreshCapacityFromSave();
    }

    private void HandleWaveStateChanged(GuestWaveState state)
    {
        if (state == GuestWaveState.Ready || state == GuestWaveState.Completed)
        {
            inService = 0;
            isBottleneck = false;
            RefreshCapacityRing();
        }
        else RefreshLiveSnapshot();
    }

    private static bool TryNeedForBuilding(string key, out ResortNeed serviceNeed)
    {
        switch (key)
        {
            case "RECEPTION": serviceNeed = ResortNeed.Rooms; return true;
            case "BEACH_BAR": serviceNeed = ResortNeed.Drinks; return true;
            case "RESTAURANT": serviceNeed = ResortNeed.Food; return true;
            case "POOL": serviceNeed = ResortNeed.Relax; return true;
            case "BUNGALOWS": serviceNeed = ResortNeed.OceanView; return true;
            default: serviceNeed = default; return false;
        }
    }
}

/// <summary>Low-cost segmented occupancy ring for the world-space worker marker.</summary>
public sealed class SegmentedCapacityRingGraphic : MaskableGraphic
{
    private static readonly Color EmptyColor = new Color(0.79f, 0.65f, 0.45f, 0.88f);
    private static readonly Color OccupiedColor = new Color(0.39f, 0.69f, 0.45f, 1f);
    private static readonly Color BottleneckColor = new Color(0.84f, 0.36f, 0.31f, 1f);
    private static readonly Color BottleneckEmptyColor = new Color(0.66f, 0.39f, 0.35f, 0.9f);

    public float Thickness { get; set; } = 8f;
    private int segments = 3;
    private int occupied;
    private bool bottleneck;

    public void SetState(int capacity, int inService, bool isBottleneck)
    {
        int nextSegments = Mathf.Clamp(capacity, 0, 15);
        int nextOccupied = Mathf.Clamp(inService, 0, nextSegments);
        if (segments == nextSegments && occupied == nextOccupied && bottleneck == isBottleneck) return;
        segments = nextSegments;
        occupied = nextOccupied;
        bottleneck = isBottleneck;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (segments <= 0) return;

        Rect rect = GetPixelAdjustedRect();
        float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f - 1f;
        float innerRadius = Mathf.Max(0f, outerRadius - Thickness);
        float step = Mathf.PI * 2f / segments;
        float gap = Mathf.Min(0.11f, step * 0.18f);
        const int arcSubdivisions = 5;

        for (int segment = 0; segment < segments; segment++)
        {
            float segmentStart = Mathf.PI * 0.5f - segment * step - gap * 0.5f;
            float segmentEnd = Mathf.PI * 0.5f - (segment + 1) * step + gap * 0.5f;
            Color segmentColor = bottleneck
                ? segment < occupied ? BottleneckColor : BottleneckEmptyColor
                : segment < occupied ? OccupiedColor : EmptyColor;

            for (int part = 0; part < arcSubdivisions; part++)
            {
                float t0 = (float)part / arcSubdivisions;
                float t1 = (float)(part + 1) / arcSubdivisions;
                float a0 = Mathf.Lerp(segmentStart, segmentEnd, t0);
                float a1 = Mathf.Lerp(segmentStart, segmentEnd, t1);
                AddArcQuad(vertexHelper, rect.center, innerRadius, outerRadius, a0, a1, segmentColor);
            }
        }
    }

    private static void AddArcQuad(VertexHelper vh, Vector2 center, float innerRadius,
        float outerRadius, float startAngle, float endAngle, Color segmentColor)
    {
        int first = vh.currentVertCount;
        vh.AddVert(Point(center, outerRadius, startAngle), segmentColor, Vector2.zero);
        vh.AddVert(Point(center, outerRadius, endAngle), segmentColor, Vector2.zero);
        vh.AddVert(Point(center, innerRadius, endAngle), segmentColor, Vector2.zero);
        vh.AddVert(Point(center, innerRadius, startAngle), segmentColor, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }

    private static Vector2 Point(Vector2 center, float radius, float angle) =>
        center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
}
