using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Player-facing view of saved assignments; BuildingSlot owns the transfer.</summary>
public sealed class WorkerManagementPanelUI : MonoBehaviour
{
    private sealed class BuildingRow
    {
        public Image Background;
        public Image Accent;
        public Image ActionBadge;
        public Button Button;
        public TMP_Text Name;
        public TMP_Text Detail;
        public TMP_Text Action;
    }

    private readonly List<BuildingSlot> available = new List<BuildingSlot>(5);
    private readonly List<WaveNeedSnapshot> snapshots = new List<WaveNeedSnapshot>(5);
    private readonly Dictionary<ResortNeed, BuildingRow> rows = new Dictionary<ResortNeed, BuildingRow>(5);
    private GuestWaveController controller;
    private TMP_Text summaryText;
    private TMP_Text focusText;
    private TMP_Text destinationText;
    private TMP_Text messageText;
    private ResortNeed? focusedNeed;
    private string resultMessage;
    private bool duplicateBuilding;

    public void Initialize(GuestWaveController waveController)
    {
        controller = waveController;
    }

    public void FocusNeed(ResortNeed? need)
    {
        focusedNeed = need;
        resultMessage = null;
        if (isActiveAndEnabled) Refresh();
    }

    public void FocusBuilding(string buildingKey)
    {
        focusedNeed = TryNeedForKey(buildingKey, out ResortNeed need) ? need : (ResortNeed?)null;
        resultMessage = null;
        if (isActiveAndEnabled) Refresh();
    }

    private void OnEnable()
    {
        if (controller == null) controller = FindAnyObjectByType<GuestWaveController>();
        if (rows.Count == 0) BuildUI();
        if (controller != null)
        {
            controller.OnWaveStateChanged += HandleStateChanged;
            controller.OnServicePerformanceChanged += HandlePerformanceChanged;
            controller.OnNeedStatusChanged += HandleNeedStatusChanged;
        }
        Refresh();
    }

    private void OnDisable()
    {
        if (controller == null) return;
        controller.OnWaveStateChanged -= HandleStateChanged;
        controller.OnServicePerformanceChanged -= HandlePerformanceChanged;
        controller.OnNeedStatusChanged -= HandleNeedStatusChanged;
    }

    private void BuildUI()
    {
        RectTransform panel = (RectTransform)transform;
        panel.sizeDelta = new Vector2(900f, 740f);
        Image panelBackground = GetComponent<Image>();
        if (panelBackground != null)
            panelBackground.color = new Color(0.10f, 0.23f, 0.20f, 0.94f);

        summaryText = WaveNeedRowUI.CreateText("WorkerSummary", panel, new Vector2(0f, 279f),
            new Vector2(820f, 38f), 27f, TextAlignmentOptions.Center);
        focusText = WaveNeedRowUI.CreateText("WorkerFocus", panel, new Vector2(0f, 235f),
            new Vector2(820f, 34f), 23f, TextAlignmentOptions.Center);
        focusText.color = new Color(0.78f, 0.93f, 0.86f);
        destinationText = WaveNeedRowUI.CreateText("WorkerDestination", panel, new Vector2(0f, 190f),
            new Vector2(820f, 50f), 23f, TextAlignmentOptions.Center);
        destinationText.color = new Color(1f, 0.78f, 0.41f);

        TMP_Text section = WaveNeedRowUI.CreateText("WorkerSourceHeading", panel, new Vector2(0f, 143f),
            new Vector2(820f, 32f), 23f, TextAlignmentOptions.MidlineLeft);
        section.text = "SERVICE BUILDINGS  •  Tap a green row to move its Worker";
        section.color = new Color(0.78f, 0.93f, 0.86f);

        for (int i = 0; i < WaveNeedPresentation.StableOrder.Length; i++)
        {
            ResortNeed need = WaveNeedPresentation.StableOrder[i];
            rows.Add(need, BuildRow(panel, need, 87f - i * 65f));
        }

        messageText = WaveNeedRowUI.CreateText("WorkerMessage", panel, new Vector2(0f, -235f),
            new Vector2(820f, 36f), 22f, TextAlignmentOptions.Center);
        messageText.color = new Color(0.91f, 0.96f, 0.92f);
    }

    private BuildingRow BuildRow(RectTransform panel, ResortNeed need, float y)
    {
        GameObject rowObject = new GameObject("WorkerRow_" + need,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)rowObject.transform;
        rect.SetParent(panel, false);
        Place(rect, new Vector2(0f, y), new Vector2(820f, 62f));
        Image background = rowObject.GetComponent<Image>();
        Button button = rowObject.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => TryTransferFrom(need));

        Image accent = CreateImage("StaffingAccent", rect, new Vector2(-401f, 0f),
            new Vector2(7f, 52f));
        Image actionBadge = CreateImage("TransferActionBackground", rect, new Vector2(298f, 13f),
            new Vector2(192f, 34f));
        Image menuBackground = panel.GetComponent<Image>();
        if (menuBackground != null)
        {
            actionBadge.sprite = menuBackground.sprite;
            actionBadge.type = menuBackground.type;
        }

        TMP_Text nameText = WaveNeedRowUI.CreateText("BuildingName", rect,
            new Vector2(-114f, 14f), new Vector2(560f, 26f), 24f, TextAlignmentOptions.MidlineLeft);
        TMP_Text detailText = WaveNeedRowUI.CreateText("BuildingDetail", rect,
            new Vector2(-15f, -17f), new Vector2(760f, 24f), 20f, TextAlignmentOptions.MidlineLeft);
        TMP_Text actionText = WaveNeedRowUI.CreateText("WorkerAction", rect,
            new Vector2(298f, 13f), new Vector2(180f, 28f), 20f, TextAlignmentOptions.Center);

        return new BuildingRow
        {
            Background = background,
            Accent = accent,
            ActionBadge = actionBadge,
            Button = button,
            Name = nameText,
            Detail = detailText,
            Action = actionText
        };
    }

    private void Refresh()
    {
        if (summaryText == null) return;
        if (controller == null)
        {
            summaryText.text = "Worker data unavailable";
            destinationText.text = string.Empty;
            focusText.text = string.Empty;
            messageText.text = "Wave controller is missing.";
            foreach (BuildingRow row in rows.Values) row.Button.gameObject.SetActive(false);
            return;
        }

        CollectAvailableBuildings();
        controller.GetNeedSnapshots(snapshots);
        int totalWorkers = 0;
        int emptyCount = 0;
        BuildingSlot destination = null;
        foreach (BuildingSlot building in available)
        {
            totalWorkers += building.PersistedState.workers_assigned;
            if (building.HasWorker) continue;
            emptyCount++;
            destination = building;
        }

        bool consistent = !duplicateBuilding && emptyCount == 1;
        bool allowedState = controller.CurrentState == GuestWaveState.Ready
            || controller.CurrentState == GuestWaveState.Active
            || controller.CurrentState == GuestWaveState.Resolving;
        bool canTransfer = consistent && allowedState;

        summaryText.text = $"{totalWorkers} Workers  •  {available.Count} available service buildings";
        focusText.text = focusedNeed.HasValue
            ? "Viewing " + WaveNeedPresentation.NameFor(focusedNeed.Value)
            : "Choose a staffed source below";

        if (duplicateBuilding)
            destinationText.text = "Duplicate service buildings found. Transfers disabled.";
        else if (emptyCount != 1)
            destinationText.text = $"Staffing inconsistent: {emptyCount} unstaffed buildings. Transfers disabled.";
        else
            destinationText.text = "Automatic destination: " + DisplayName(destination)
                + "  •  gains a Worker and runs faster";

        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
        {
            BuildingRow row = rows[need];
            BuildingSlot building = FindAvailable(need);
            row.Button.gameObject.SetActive(building != null);
            if (building == null) continue;

            bool staffed = building.HasWorker;
            bool focused = focusedNeed.HasValue && focusedNeed.Value == need;
            row.Background.color = staffed
                ? focused ? new Color(0.16f, 0.42f, 0.33f, 0.99f)
                    : new Color(0.10f, 0.32f, 0.26f, 0.98f)
                : focused ? new Color(0.43f, 0.31f, 0.16f, 0.99f)
                    : new Color(0.35f, 0.26f, 0.14f, 0.98f);
            row.Accent.color = staffed ? new Color(0.47f, 0.88f, 0.63f) : new Color(1f, 0.72f, 0.32f);
            row.ActionBadge.color = staffed
                ? canTransfer ? new Color(0.19f, 0.53f, 0.37f) : new Color(0.29f, 0.37f, 0.33f)
                : new Color(0.58f, 0.40f, 0.18f);
            row.Name.text = DisplayName(building) + (staffed ? "  •  STAFFED" : "  •  SLOW / UNSTAFFED");
            row.Name.color = staffed ? new Color(0.66f, 0.92f, 0.76f) : new Color(1f, 0.79f, 0.43f);
            row.Detail.text = PerformanceText(need) + StatusText(need);
            row.Action.text = staffed
                ? canTransfer ? "MOVE WORKER" : controller.CurrentState == GuestWaveState.Paused ? "PAUSED" : "UNAVAILABLE"
                : "AUTO TARGET";
            row.Action.color = Color.white;
            row.Button.interactable = staffed && canTransfer;
        }

        if (controller.CurrentState == GuestWaveState.Paused)
            messageText.text = "Wave paused. Resume to transfer Workers.";
        else if (!allowedState)
            messageText.text = "Prepare the next wave to transfer Workers.";
        else if (!consistent)
            messageText.text = "Check saved assignments before moving a Worker.";
        else
            messageText.text = string.IsNullOrEmpty(resultMessage)
                ? "The source will run more slowly after its Worker moves."
                : resultMessage;
    }

    private void CollectAvailableBuildings()
    {
        available.Clear();
        duplicateBuilding = false;
        BuildingSlot[] sceneBuildings = FindObjectsByType<BuildingSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
        {
            BuildingSlot found = null;
            foreach (BuildingSlot building in sceneBuildings)
            {
                if (!building.gameObject.activeInHierarchy || !building.enabled) continue;
                if (!TryNeedForKey(building.BuildingKey, out ResortNeed candidate) || candidate != need) continue;
                building.RefreshPersistedState();
                if (!building.IsUnlocked) continue;
                if (found != null) duplicateBuilding = true;
                else found = building;
            }
            if (found != null) available.Add(found);
        }
    }

    private BuildingSlot FindAvailable(ResortNeed need)
    {
        foreach (BuildingSlot building in available)
            if (TryNeedForKey(building.BuildingKey, out ResortNeed candidate) && candidate == need)
                return building;
        return null;
    }

    private string PerformanceText(ResortNeed need)
    {
        string places = controller.TryGetServicePlaces(need, out int count) ? count + " places" : "places unavailable";
        if (!controller.TryGetServicePerformance(need, out ServicePerformance performance)
            || performance.ServiceRatePerSecond <= 0f) return places;
        float seconds = 1f / performance.ServiceRatePerSecond;
        return places + "  •  " + (performance.Utilization * 100f).ToString("0")
            + "% speed  •  " + seconds.ToString("0.0") + "s/unit/place"
            + (performance.EnergyBoosted ? " (boosted)" : string.Empty);
    }

    private string StatusText(ResortNeed need)
    {
        foreach (WaveNeedSnapshot snapshot in snapshots)
        {
            if (snapshot.Need != need) continue;
            if (snapshot.Status == WaveNeedStatus.Bottleneck) return "  •  Bottleneck";
            if (snapshot.Status == WaveNeedStatus.Recovering) return "  •  Recovering";
            break;
        }
        return string.Empty;
    }

    private void TryTransferFrom(ResortNeed need)
    {
        if (controller == null || controller.CurrentState == GuestWaveState.Paused) return;
        CollectAvailableBuildings();
        BuildingSlot source = FindAvailable(need);
        BuildingSlot destination = null;
        int unstaffedCount = 0;
        foreach (BuildingSlot building in available)
            if (!building.HasWorker) { destination = building; unstaffedCount++; }

        if (duplicateBuilding || unstaffedCount != 1 || source == null || !source.HasWorker
            || (controller.CurrentState != GuestWaveState.Ready
                && controller.CurrentState != GuestWaveState.Active
                && controller.CurrentState != GuestWaveState.Resolving))
        {
            resultMessage = "Transfer unavailable. Check the current staffing and wave state.";
            Refresh();
            return;
        }

        string sourceName = DisplayName(source);
        string destinationName = DisplayName(destination);
        if (!source.TryMoveWorkerTo(destination))
        {
            resultMessage = "Transfer failed. Saved assignments may have changed.";
            Refresh();
            return;
        }

        source.GetComponent<BuildingWorkerIndicator>()?.RefreshFromSave();
        destination.GetComponent<BuildingWorkerIndicator>()?.RefreshFromSave();
        resultMessage = destinationName + " faster; " + sourceName + " slower. Worker transferred.";
        Refresh();
    }

    private void HandleStateChanged(GuestWaveState _) => Refresh();
    private void HandlePerformanceChanged(ServicePerformance _) => Refresh();
    private void HandleNeedStatusChanged(ResortNeed _, WaveNeedStatus __, int ___) => Refresh();

    private static string DisplayName(BuildingSlot building)
    {
        if (building == null) return "Unknown building";
        if (TryNeedForKey(building.BuildingKey, out ResortNeed need))
        {
            switch (need)
            {
                case ResortNeed.Rooms: return "Reception";
                case ResortNeed.Drinks: return "Beach Bar";
                case ResortNeed.Food: return "Restaurant";
                case ResortNeed.Relax: return "Pool";
                case ResortNeed.OceanView: return "Bungalows";
            }
        }
        return building.name;
    }

    private static bool TryNeedForKey(string key, out ResortNeed need)
    {
        switch (key)
        {
            case "RECEPTION": need = ResortNeed.Rooms; return true;
            case "BEACH_BAR": need = ResortNeed.Drinks; return true;
            case "RESTAURANT": need = ResortNeed.Food; return true;
            case "POOL": need = ResortNeed.Relax; return true;
            case "BUNGALOWS": need = ResortNeed.OceanView; return true;
            default: need = default; return false;
        }
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static Image CreateImage(string objectName, RectTransform parent, Vector2 position, Vector2 size)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.SetParent(parent, false);
        Place(rect, position, size);
        Image image = imageObject.GetComponent<Image>();
        image.raycastTarget = false; // The full row remains the touch target.
        return image;
    }
}
