using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Building detail and source selection for moving a Worker to a service need.</summary>
public sealed class WorkerManagementPanelUI : MonoBehaviour
{
    private sealed class SourceCard
    {
        public Image Background;
        public Button Button;
        public SegmentedCapacityRingGraphic Ring;
        public TMP_Text Name;
        public TMP_Text Detail;
        public TMP_Text Action;
    }

    private readonly List<BuildingSlot> available = new List<BuildingSlot>(5);
    private readonly List<WaveNeedSnapshot> snapshots = new List<WaveNeedSnapshot>(5);
    private readonly Dictionary<ResortNeed, SourceCard> sourceCards = new Dictionary<ResortNeed, SourceCard>(5);
    private GuestWaveController controller;
    private TMP_Text buildingTitle;
    private TMP_Text buildingDetails;
    private TMP_Text workerState;
    private TMP_Text sectionTitle;
    private TMP_Text messageText;
    private TMP_Text upgradeAction;
    private Button upgradeButton;
    private TMP_Text upgradeButtonText;
    private Button moveWorkerButton;
    private TMP_Text moveWorkerButtonText;
    private ResortNeed? destinationNeed;
    private ResortNeed? selectedSourceNeed;
    private string requestedBuildingKey;
    private string resultMessage;
    private bool duplicateBuilding;

    public void Initialize(GuestWaveController waveController) => controller = waveController;

    public void FocusNeed(ResortNeed? need)
    {
        destinationNeed = need;
        requestedBuildingKey = null;
        selectedSourceNeed = null;
        resultMessage = null;
        if (isActiveAndEnabled) Refresh();
    }

    public void FocusBuilding(string buildingKey)
    {
        requestedBuildingKey = buildingKey;
        destinationNeed = TryNeedForKey(buildingKey, out ResortNeed need) ? need : (ResortNeed?)null;
        selectedSourceNeed = null;
        resultMessage = null;
        if (isActiveAndEnabled) Refresh();
    }

    private void OnEnable()
    {
        if (controller == null) controller = FindAnyObjectByType<GuestWaveController>();
        if (sourceCards.Count == 0) BuildUI();
        if (controller != null)
        {
            controller.OnWaveStateChanged += HandleStateChanged;
            controller.OnServicePerformanceChanged += HandlePerformanceChanged;
            controller.OnNeedStateChanged += HandleNeedStateChanged;
            controller.OnBuildingUpgraded += HandleBuildingUpgraded;
        }
        Refresh();
    }

    private void OnDisable()
    {
        if (controller == null) return;
        controller.OnWaveStateChanged -= HandleStateChanged;
        controller.OnServicePerformanceChanged -= HandlePerformanceChanged;
        controller.OnNeedStateChanged -= HandleNeedStateChanged;
        controller.OnBuildingUpgraded -= HandleBuildingUpgraded;
    }

    private void BuildUI()
    {
        RectTransform panel = (RectTransform)transform;
        panel.sizeDelta = new Vector2(900f, 760f);
        Image panelBackground = GetComponent<Image>();
        if (panelBackground != null) panelBackground.color = new Color(0.10f, 0.23f, 0.20f, 0.96f);

        buildingTitle = WaveNeedRowUI.CreateText("SelectedBuilding", panel, new Vector2(0f, 310f),
            new Vector2(820f, 40f), 30f, TextAlignmentOptions.Center);
        buildingDetails = WaveNeedRowUI.CreateText("BuildingDetails", panel, new Vector2(0f, 265f),
            new Vector2(820f, 38f), 22f, TextAlignmentOptions.Center);
        workerState = WaveNeedRowUI.CreateText("CurrentWorkerState", panel, new Vector2(0f, 222f),
            new Vector2(820f, 34f), 22f, TextAlignmentOptions.Center);
        workerState.color = new Color(1f, 0.79f, 0.48f);

        upgradeAction = WaveNeedRowUI.CreateText("UpgradeDetails", panel, new Vector2(0f, 175f),
            new Vector2(820f, 28f), 18f, TextAlignmentOptions.Center);
        upgradeButton = CreateButton("BuildingUpgradeButton", panel, new Vector2(0f, 135f),
            new Vector2(820f, 52f), out upgradeButtonText);
        upgradeButton.onClick.AddListener(TryUpgradeFocusedBuilding);

        sectionTitle = WaveNeedRowUI.CreateText("WorkerSourcesHeading", panel, new Vector2(0f, 91f),
            new Vector2(820f, 32f), 21f, TextAlignmentOptions.MidlineLeft);
        sectionTitle.text = "AVAILABLE WORKER SOURCES";
        sectionTitle.color = new Color(0.78f, 0.93f, 0.86f);

        for (int i = 0; i < WaveNeedPresentation.StableOrder.Length; i++)
            sourceCards.Add(WaveNeedPresentation.StableOrder[i],
                BuildSourceCard(panel, WaveNeedPresentation.StableOrder[i], 46f - i * 55f));

        messageText = WaveNeedRowUI.CreateText("WorkerMessage", panel, new Vector2(0f, -245f),
            new Vector2(820f, 34f), 19f, TextAlignmentOptions.Center);
        messageText.color = new Color(0.91f, 0.96f, 0.92f);
        moveWorkerButton = CreateButton("MoveWorkerHereButton", panel, new Vector2(0f, -300f),
            new Vector2(820f, 54f), out moveWorkerButtonText);
        moveWorkerButton.onClick.AddListener(TryTransferSelectedWorker);
    }

    private SourceCard BuildSourceCard(RectTransform panel, ResortNeed need, float y)
    {
        GameObject cardObject = new GameObject("WorkerSource_" + need,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)cardObject.transform;
        rect.SetParent(panel, false);
        Place(rect, new Vector2(0f, y), new Vector2(820f, 50f));
        Image background = cardObject.GetComponent<Image>();
        Button button = cardObject.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => SelectSource(need));

        GameObject ringObject = new GameObject("CapacityRing", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(SegmentedCapacityRingGraphic));
        RectTransform ringRect = (RectTransform)ringObject.transform;
        ringRect.SetParent(rect, false);
        Place(ringRect, new Vector2(-365f, 0f), new Vector2(40f, 40f));
        SegmentedCapacityRingGraphic ring = ringObject.GetComponent<SegmentedCapacityRingGraphic>();
        ring.Thickness = 4f;
        ring.raycastTarget = false;

        TMP_Text name = WaveNeedRowUI.CreateText("SourceName", rect, new Vector2(-250f, 9f),
            new Vector2(300f, 25f), 21f, TextAlignmentOptions.MidlineLeft);
        TMP_Text detail = WaveNeedRowUI.CreateText("SourceDetails", rect, new Vector2(-225f, -12f),
            new Vector2(470f, 20f), 16f, TextAlignmentOptions.MidlineLeft);
        TMP_Text action = WaveNeedRowUI.CreateText("SourceAction", rect, new Vector2(330f, 0f),
            new Vector2(155f, 28f), 17f, TextAlignmentOptions.Center);

        return new SourceCard { Background = background, Button = button, Ring = ring,
            Name = name, Detail = detail, Action = action };
    }

    private void Refresh()
    {
        if (buildingTitle == null) return;
        if (controller == null)
        {
            buildingTitle.text = "Worker data unavailable";
            buildingDetails.text = string.Empty;
            workerState.text = string.Empty;
            messageText.text = "Wave controller is missing.";
            foreach (SourceCard card in sourceCards.Values) card.Button.gameObject.SetActive(false);
            upgradeButton.gameObject.SetActive(false);
            moveWorkerButton.gameObject.SetActive(false);
            return;
        }

        CollectAvailableBuildings();
        controller.GetNeedSnapshots(snapshots);
        BuildingSlot target = ResolveDestination();
        bool targetIsUnstaffed = target != null && !target.HasWorker;
        bool transferPhase = controller.CurrentState == GuestWaveState.Ready
            || controller.CurrentState == GuestWaveState.Active
            || controller.CurrentState == GuestWaveState.Resolving;
        bool upgradePhase = controller.CurrentState == GuestWaveState.Ready
            || controller.CurrentState == GuestWaveState.Completed;
        bool consistent = !duplicateBuilding;

        if (target == null)
        {
            buildingTitle.text = "Choose a service building";
            buildingDetails.text = "Tap an unstaffed building on the island to view its capacity and staffing.";
            workerState.text = string.Empty;
        }
        else
        {
            int places = controller.TryGetServicePlaces(NeedForKey(target.BuildingKey), out int activeCapacity)
                ? activeCapacity : controller.GetConfiguredServicePlacesForLevel(Mathf.Max(1, target.Level));
            string name = DisplayName(target);
            buildingTitle.text = name + "  •  LEVEL " + target.Level;
            string performance = "Service speed unavailable";
            if (TryNeedForKey(target.BuildingKey, out ResortNeed targetNeed)
                && controller.TryGetServicePerformance(targetNeed, out ServicePerformance rate)
                && rate.ServiceRatePerSecond > 0f)
                performance = (rate.Utilization * 100f).ToString("0") + "% speed  •  "
                    + (1f / rate.ServiceRatePerSecond).ToString("0.0") + "s / unit / place";
            buildingDetails.text = "Capacity: " + places + " places  •  " + performance;
            workerState.text = target.HasWorker ? "Current Worker: assigned" : "Current Worker: none";
            workerState.color = target.HasWorker ? new Color(0.49f, 0.83f, 0.58f)
                : new Color(1f, 0.72f, 0.4f);
            if (TryNeedForKey(target.BuildingKey, out targetNeed))
                buildingDetails.text += StatusText(targetNeed);
        }

        UpdateUpgradeSection(target, upgradePhase);
        UpdateSourceCards(target, targetIsUnstaffed && transferPhase && consistent);
        bool hasSelectedSource = selectedSourceNeed.HasValue
            && FindAvailable(selectedSourceNeed.Value) != null && targetIsUnstaffed;
        moveWorkerButton.gameObject.SetActive(targetIsUnstaffed);
        moveWorkerButton.interactable = hasSelectedSource && transferPhase && consistent;
        moveWorkerButtonText.text = "MOVE WORKER HERE";

        if (controller.CurrentState == GuestWaveState.Paused)
            messageText.text = "Wave paused. Resume to move a Worker.";
        else if (controller.CurrentState == GuestWaveState.Active || controller.CurrentState == GuestWaveState.Resolving)
            messageText.text = targetIsUnstaffed
                ? "Choose a staffed building below. The service speed changes immediately."
                : "Worker movement is available while the wave is running.";
        else if (targetIsUnstaffed && selectedSourceNeed.HasValue)
            messageText.text = "Confirm the source to move its Worker here.";
        else if (targetIsUnstaffed)
            messageText.text = "Choose a staffed building below to supply a Worker.";
        else
            messageText.text = "Tap an unstaffed service building to choose where to send a Worker.";

        if (!string.IsNullOrEmpty(resultMessage)) messageText.text = resultMessage;
    }

    private BuildingSlot ResolveDestination()
    {
        if (!string.IsNullOrWhiteSpace(requestedBuildingKey))
            return FindByKey(requestedBuildingKey);
        if (destinationNeed.HasValue) return FindAvailable(destinationNeed.Value);
        foreach (BuildingSlot building in available)
            if (!building.HasWorker) return building;
        return null;
    }

    private void UpdateUpgradeSection(BuildingSlot target, bool upgradePhase)
    {
        bool showUpgrade = target != null && target.IsUnlocked && upgradePhase;
        upgradeAction.gameObject.SetActive(showUpgrade);
        upgradeButton.gameObject.SetActive(showUpgrade);
        if (!showUpgrade) return;

        int currentCapacity = controller.GetConfiguredServicePlacesForLevel(target.Level);
        if (target.Level >= 3)
        {
            upgradeAction.text = "Building capacity is at its prototype maximum.";
            upgradeButton.interactable = false;
            upgradeButtonText.text = "MAX LEVEL  •  " + currentCapacity + " PLACES";
            return;
        }

        int nextCapacity = controller.GetConfiguredServicePlacesForLevel(target.Level + 1);
        int cost = controller.GetBuildingUpgradeCost(target.BuildingKey);
        bool canAfford = target.CanUpgrade;
        upgradeAction.text = canAfford ? "A permanent capacity improvement for the next wave."
            : "Keep your Coins for later or choose another investment.";
        upgradeButton.interactable = canAfford;
        upgradeButtonText.text = "UPGRADE  •  " + currentCapacity + " → " + nextCapacity
            + " PLACES  •  " + cost + " COINS";
    }

    private void UpdateSourceCards(BuildingSlot target, bool canSelectSources)
    {
        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
        {
            SourceCard card = sourceCards[need];
            BuildingSlot source = FindAvailable(need);
            bool show = source != null && source.HasWorker && source != target;
            card.Button.gameObject.SetActive(show);
            if (!show) continue;

            bool selected = selectedSourceNeed.HasValue && selectedSourceNeed.Value == need;
            bool bottleneck = IsBottleneck(need);
            card.Background.color = selected ? new Color(0.18f, 0.46f, 0.34f, 1f)
                : bottleneck ? new Color(0.40f, 0.22f, 0.20f, 0.98f)
                : new Color(0.12f, 0.32f, 0.27f, 0.98f);
            card.Name.text = DisplayName(source);
            card.Name.color = Color.white;
            card.Detail.text = SourceDetails(need, source);
            card.Action.text = selected ? "SELECTED" : "CHOOSE";
            card.Action.color = bottleneck ? new Color(1f, 0.65f, 0.53f) : new Color(0.77f, 0.93f, 0.81f);
            card.Button.interactable = canSelectSources;
            int capacity = controller.TryGetServicePlaces(need, out int count) ? count : 0;
            int occupied = FindProcessingCount(need);
            card.Ring.SetState(capacity, occupied, bottleneck);
        }
    }

    private string SourceDetails(ResortNeed need, BuildingSlot source)
    {
        int capacity = controller.TryGetServicePlaces(need, out int count) ? count : 0;
        string text = "STAFFED  •  " + capacity + " places";
        if (controller.TryGetServicePerformance(need, out ServicePerformance performance))
            text += "  •  " + (performance.Utilization * 100f).ToString("0") + "% speed";
        return text + StatusText(need);
    }

    private string StatusText(ResortNeed need)
    {
        foreach (WaveNeedSnapshot snapshot in snapshots)
            if (snapshot.Need == need && snapshot.Status == WaveNeedStatus.Bottleneck)
                return "  •  BOTTLENECK";
        return string.Empty;
    }

    private bool IsBottleneck(ResortNeed need)
    {
        foreach (WaveNeedSnapshot snapshot in snapshots)
            if (snapshot.Need == need) return snapshot.Status == WaveNeedStatus.Bottleneck;
        return false;
    }

    private int FindProcessingCount(ResortNeed need)
    {
        foreach (WaveNeedSnapshot snapshot in snapshots)
            if (snapshot.Need == need) return snapshot.Processing;
        return 0;
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

    private BuildingSlot FindByKey(string key)
    {
        foreach (BuildingSlot building in available)
            if (building.BuildingKey == key) return building;
        return null;
    }

    private void SelectSource(ResortNeed need)
    {
        selectedSourceNeed = need;
        resultMessage = null;
        Refresh();
    }

    private void TryTransferSelectedWorker()
    {
        if (controller == null || !selectedSourceNeed.HasValue) return;
        CollectAvailableBuildings();
        BuildingSlot target = ResolveDestination();
        BuildingSlot source = FindAvailable(selectedSourceNeed.Value);
        bool allowed = controller.CurrentState == GuestWaveState.Ready
            || controller.CurrentState == GuestWaveState.Active
            || controller.CurrentState == GuestWaveState.Resolving;
        if (duplicateBuilding || !allowed || target == null || target.HasWorker
            || source == null || !source.HasWorker || !source.TryMoveWorkerTo(target))
        {
            resultMessage = "Worker movement is unavailable. Check the selected building and wave state.";
            Refresh();
            return;
        }

        string sourceName = DisplayName(source);
        string destinationName = DisplayName(target);
        source.GetComponent<BuildingWorkerIndicator>()?.RefreshFromSave();
        target.GetComponent<BuildingWorkerIndicator>()?.RefreshFromSave();
        selectedSourceNeed = null;
        resultMessage = destinationName + " is staffed. " + sourceName + " will run unstaffed.";
        Refresh();
    }

    private void TryUpgradeFocusedBuilding()
    {
        CollectAvailableBuildings();
        BuildingSlot target = ResolveDestination();
        if (target == null || !target.TryPurchaseUpgrade())
        {
            resultMessage = "Upgrade unavailable. Check the Coin balance and current building level.";
            Refresh();
            return;
        }
        resultMessage = DisplayName(target) + " upgraded. More guests can be served at the same time.";
        Refresh();
    }

    private void HandleStateChanged(GuestWaveState _) => Refresh();
    private void HandlePerformanceChanged(ServicePerformance _) => Refresh();
    private void HandleNeedStateChanged(WaveNeedSnapshot _) => Refresh();
    private void HandleBuildingUpgraded(BuildingUpgradeInfo _) => Refresh();

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

    private static ResortNeed NeedForKey(string key) =>
        TryNeedForKey(key, out ResortNeed need) ? need : default;

    private static Button CreateButton(string objectName, RectTransform panel, Vector2 position,
        Vector2 size, out TMP_Text label)
    {
        GameObject buttonObject = new GameObject(objectName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.SetParent(panel, false);
        Place(rect, position, size);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.15f, 0.39f, 0.30f, 0.98f);
        Button button = buttonObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        label = WaveNeedRowUI.CreateText(objectName + "Label", rect, Vector2.zero, size - new Vector2(24f, 8f),
            19f, TextAlignmentOptions.Center);
        label.color = Color.white;
        return button;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
