using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Displays the authoritative aggregate of each active service need in the Guests menu.</summary>
public sealed class WaveNeedsPanelUI : MonoBehaviour
{
    [SerializeField] private GuestWaveController controller;
    [Header("Need icons (SVG textures)")]
    [SerializeField] private Texture roomsIcon;
    [SerializeField] private Texture drinksIcon;
    [SerializeField] private Texture foodIcon;
    [SerializeField] private Texture relaxIcon;
    [SerializeField] private Texture oceanViewIcon;

    private readonly List<WaveNeedSnapshot> snapshots = new List<WaveNeedSnapshot>(5);
    private readonly Dictionary<ResortNeed, WaveNeedRowUI> rows = new Dictionary<ResortNeed, WaveNeedRowUI>(5);
    private readonly HashSet<ResortNeed> filteredLockedNeeds = new HashSet<ResortNeed>();
    private TMP_Text arrivalText;
    private Action<ResortNeed> managementOpener;

    /// <summary>SCRUM-84 registers an opener that returns to this Guests panel on close.</summary>
    public void SetManagementOpener(Action<ResortNeed> opener)
    {
        managementOpener = opener;
        foreach (WaveNeedRowUI row in rows.Values)
            row.SetNavigationAvailable(opener != null);
    }

    private void OnEnable()
    {
        if (controller == null) controller = FindAnyObjectByType<GuestWaveController>();
        if (rows.Count == 0) BuildUI();
        if (controller == null)
        {
            arrivalText.text = "Wave data unavailable";
            return;
        }

        controller.OnWaveStarted += HandleWaveStarted;
        controller.OnWaveStateChanged += HandleStateChanged;
        controller.OnNeedProgressChanged += HandleNeedProgressChanged;
        controller.OnNeedStatusChanged += HandleNeedStatusChanged;
        controller.OnArrivalProgressChanged += HandleArrivalProgressChanged;
        RefreshFromSimulation();
    }

    private void OnDisable()
    {
        if (controller == null) return;
        controller.OnWaveStarted -= HandleWaveStarted;
        controller.OnWaveStateChanged -= HandleStateChanged;
        controller.OnNeedProgressChanged -= HandleNeedProgressChanged;
        controller.OnNeedStatusChanged -= HandleNeedStatusChanged;
        controller.OnArrivalProgressChanged -= HandleArrivalProgressChanged;
    }

    private void BuildUI()
    {
        RectTransform panel = (RectTransform)transform;
        arrivalText = WaveNeedRowUI.CreateText("WaveNeedsStatus", panel, new Vector2(0f, 237f),
            new Vector2(820f, 32f), 23f, TextAlignmentOptions.Center);
        arrivalText.color = new Color(0.87f, 0.96f, 0.91f);

        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
        {
            int index = rows.Count;
            GameObject rowObject = new GameObject("NeedRow_" + need, typeof(RectTransform), typeof(CanvasRenderer));
            RectTransform rect = (RectTransform)rowObject.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(820f, 62f);
            rect.anchoredPosition = new Vector2(0f, -122f - index * 69f);
            WaveNeedRowUI row = rowObject.AddComponent<WaveNeedRowUI>();
            row.Initialize(need, IconFor(need), OpenManagement);
            row.SetNavigationAvailable(managementOpener != null);
            rowObject.SetActive(false);
            rows.Add(need, row);
        }
    }

    private Texture IconFor(ResortNeed need)
    {
        switch (need)
        {
            case ResortNeed.Rooms: return roomsIcon;
            case ResortNeed.Drinks: return drinksIcon;
            case ResortNeed.Food: return foodIcon;
            case ResortNeed.Relax: return relaxIcon;
            case ResortNeed.OceanView: return oceanViewIcon;
            default: return null;
        }
    }

    private void RefreshFromSimulation()
    {
        controller.GetNeedSnapshots(snapshots);
        foreach (WaveNeedRowUI row in rows.Values) row.gameObject.SetActive(false);
        filteredLockedNeeds.Clear();
        foreach (WaveNeedSnapshot snapshot in snapshots)
        {
            if (snapshot.Total <= 0) continue;
            if (!controller.IsNeedUnlocked(snapshot.Need))
            {
                filteredLockedNeeds.Add(snapshot.Need);
                continue;
            }
            if (!rows.TryGetValue(snapshot.Need, out WaveNeedRowUI row)) continue;
            row.SetSnapshot(snapshot);
            row.gameObject.SetActive(true);
        }
        UpdateArrivalText();
    }

    private void UpdateArrivalText()
    {
        switch (controller.CurrentState)
        {
            case GuestWaveState.Ready:
                arrivalText.text = "Start a wave to see guest needs";
                break;
            case GuestWaveState.Completed:
                arrivalText.text = "All guests arrived - wave complete";
                break;
            case GuestWaveState.Resolving:
                arrivalText.text = "All guests arrived - finalizing";
                break;
            case GuestWaveState.Paused:
                arrivalText.text = controller.AreNeedArrivalsClosed
                    ? "All guests arrived - wave paused"
                    : "Wave paused";
                break;
            default:
                arrivalText.text = controller.AreNeedArrivalsClosed
                    ? "All guests arrived - finishing services"
                    : "Guests arriving";
                break;
        }
    }

    private void HandleWaveStarted(int _) => RefreshFromSimulation();
    private void HandleStateChanged(GuestWaveState _) => RefreshFromSimulation();
    private void HandleArrivalProgressChanged(float _) => UpdateArrivalText();

    private void HandleNeedProgressChanged(ResortNeed need, int completed, int total)
    {
        if (!rows.TryGetValue(need, out WaveNeedRowUI row)) return;
        if (!row.gameObject.activeSelf)
        {
            if (total <= 0 || filteredLockedNeeds.Contains(need)) return;
            if (controller.IsNeedUnlocked(need)) RefreshFromSimulation();
            else filteredLockedNeeds.Add(need);
            return;
        }
        row.SetProgress(completed, total);
    }

    private void HandleNeedStatusChanged(ResortNeed need, WaveNeedStatus status, int _)
    {
        if (rows.TryGetValue(need, out WaveNeedRowUI row) && row.gameObject.activeSelf)
            row.SetStatus(status);
    }

    private void OpenManagement(ResortNeed need)
    {
        // The callback is deliberately absent until SCRUM-84 provides real navigation.
        managementOpener?.Invoke(need);
    }
}
