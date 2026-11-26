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
    private readonly Dictionary<ResortNeed, string> sourceBaseDetails = new Dictionary<ResortNeed, string>(5);
    private GuestWaveController controller;
    private TMP_Text buildingTitle;
    private TMP_Text workforceTotal;
    private TMP_Text buildingDetails;
    private TMP_Text workerState;
    private TMP_Text sectionTitle;
    private TMP_Text messageText;
    private Image upgradeBackground;
    private TMP_Text upgradeCapacity;
    private TMP_Text upgradeAction;
    private TMP_Text upgradeHint;
    private TMP_Text upgradeMissing;
    private Button upgradeButton;
    private TMP_Text upgradeButtonText;
    private ResortNeed? destinationNeed;
    private ResortNeed? selectedSourceNeed;
    private string requestedBuildingKey;
    private string resultMessage;
    private SavedWaveResult lastSavedWave;
    private bool duplicateBuilding;
    private bool upgradePurchaseInProgress;
    private float nextUpgradePressTime;

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
        panel.sizeDelta = new Vector2(900f, 900f);
        RectTransform menuTitle = panel.Find("MenuTitle") as RectTransform;
        if (menuTitle != null) menuTitle.anchoredPosition = new Vector2(menuTitle.anchoredPosition.x, -40f);
        Image panelBackground = GetComponent<Image>();
        if (panelBackground != null) panelBackground.color = new Color(0.10f, 0.23f, 0.20f, 0.985f);

        buildingTitle = WaveNeedRowUI.CreateText("SelectedBuilding", panel, new Vector2(0f, 311f),
            new Vector2(820f, 36f), 30f, TextAlignmentOptions.Center);
        FitSingleLine(buildingTitle, 26f);
        workforceTotal = WaveNeedRowUI.CreateText("WorkforceTotal", panel, new Vector2(0f, 349f),
            new Vector2(820f, 28f), 20f, TextAlignmentOptions.Center);
        FitSingleLine(workforceTotal, 18f);
        buildingDetails = WaveNeedRowUI.CreateText("BuildingDetails", panel, new Vector2(0f, 275f),
            new Vector2(820f, 32f), 22f, TextAlignmentOptions.Center);
        FitSingleLine(buildingDetails, 19f);
        workerState = WaveNeedRowUI.CreateText("CurrentWorkerState", panel, new Vector2(0f, 243f),
            new Vector2(820f, 30f), 22f, TextAlignmentOptions.Center);
        FitSingleLine(workerState, 20f);
        workerState.color = new Color(1f, 0.79f, 0.48f);

        GameObject previewObject = new GameObject("UpgradePreview", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        RectTransform previewRect = (RectTransform)previewObject.transform;
        previewRect.SetParent(panel, false);
        Place(previewRect, new Vector2(0f, 121f), new Vector2(820f, 206f));
        upgradeBackground = previewObject.GetComponent<Image>();
        upgradeBackground.color = new Color(0.14f, 0.35f, 0.29f, 0.98f);
        upgradeBackground.raycastTarget = false;
        TMP_Text previewHeading = WaveNeedRowUI.CreateText("UpgradeHeading", previewRect, new Vector2(0f, 83f),
            new Vector2(780f, 24f), 21f, TextAlignmentOptions.Center);
        previewHeading.text = "UPGRADE PREVIEW";
        previewHeading.color = new Color(0.78f, 0.93f, 0.86f);
        upgradeCapacity = WaveNeedRowUI.CreateText("UpgradeCapacity", previewRect, new Vector2(0f, 52f),
            new Vector2(780f, 34f), 30f, TextAlignmentOptions.Center);
        FitSingleLine(upgradeCapacity, 26f);
        upgradeAction = WaveNeedRowUI.CreateText("UpgradeBenefit", previewRect, new Vector2(0f, 18f),
            new Vector2(780f, 30f), 22f, TextAlignmentOptions.Center);
        FitSingleLine(upgradeAction, 20f);
        upgradeHint = WaveNeedRowUI.CreateText("UpgradeHistory", previewRect, new Vector2(0f, -11f),
            new Vector2(780f, 24f), 20f, TextAlignmentOptions.Center);
        FitSingleLine(upgradeHint, 18f);
        upgradeHint.color = new Color(1f, 0.83f, 0.58f);
        upgradeMissing = WaveNeedRowUI.CreateText("UpgradeMissing", previewRect, new Vector2(0f, -36f),
            new Vector2(780f, 22f), 21f, TextAlignmentOptions.Center);
        FitSingleLine(upgradeMissing, 19f);
        upgradeMissing.color = new Color(1f, 0.72f, 0.53f);
        upgradeButton = CreateButton("BuildingUpgradeButton", panel, new Vector2(0f, 45f),
            new Vector2(780f, 52f), out upgradeButtonText);
        upgradeButtonText.fontSize = 23f;
        FitSingleLine(upgradeButtonText, 21f);
        upgradeButton.onClick.AddListener(TryUpgradeFocusedBuilding);

        sectionTitle = WaveNeedRowUI.CreateText("WorkerSourcesHeading", panel, new Vector2(0f, 2f),
            new Vector2(820f, 32f), 21f, TextAlignmentOptions.MidlineLeft);
        sectionTitle.text = "AVAILABLE WORKER SOURCES";
        sectionTitle.color = new Color(0.78f, 0.93f, 0.86f);

        for (int i = 0; i < WaveNeedPresentation.StableOrder.Length; i++)
            sourceCards.Add(WaveNeedPresentation.StableOrder[i],
                BuildSourceCard(panel, WaveNeedPresentation.StableOrder[i], -48f - i * 69f));

        GameObject messageObject = new GameObject("WorkerMessageBackground", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        RectTransform messageRect = (RectTransform)messageObject.transform;
        messageRect.SetParent(panel, false);
        Place(messageRect, new Vector2(0f, -307f), new Vector2(820f, 36f));
        Image messageBackground = messageObject.GetComponent<Image>();
        messageBackground.color = new Color(0.08f, 0.19f, 0.17f, 0.98f);
        messageBackground.raycastTarget = false;
        messageText = WaveNeedRowUI.CreateText("WorkerMessage", messageRect, Vector2.zero,
            new Vector2(790f, 32f), 20f, TextAlignmentOptions.Center);
        FitSingleLine(messageText, 18f);
        messageText.color = new Color(0.91f, 0.96f, 0.92f);
    }

    private SourceCard BuildSourceCard(RectTransform panel, ResortNeed need, float y)
    {
        GameObject cardObject = new GameObject("WorkerSource_" + need,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)cardObject.transform;
        rect.SetParent(panel, false);
        Place(rect, new Vector2(0f, y), new Vector2(820f, 64f));
        Image background = cardObject.GetComponent<Image>();
        Button button = cardObject.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => SelectSource(need));

        GameObject ringObject = new GameObject("CapacityRing", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(SegmentedCapacityRingGraphic));
        RectTransform ringRect = (RectTransform)ringObject.transform;
        ringRect.SetParent(rect, false);
        Place(ringRect, new Vector2(-365f, 0f), new Vector2(44f, 44f));
        SegmentedCapacityRingGraphic ring = ringObject.GetComponent<SegmentedCapacityRingGraphic>();
        ring.Thickness = 4f;
        ring.raycastTarget = false;

        TMP_Text workerIcon = WaveNeedRowUI.CreateText("WorkerIndicator", rect, new Vector2(-315f, 0f),
            new Vector2(36f, 30f), 20f, TextAlignmentOptions.Center);
        workerIcon.text = "W";
        workerIcon.color = new Color(0.59f, 0.91f, 0.68f);

        TMP_Text name = WaveNeedRowUI.CreateText("SourceName", rect, new Vector2(-60f, 13f),
            new Vector2(460f, 26f), 23f, TextAlignmentOptions.MidlineLeft);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        TMP_Text detail = WaveNeedRowUI.CreateText("SourceDetails", rect, new Vector2(-60f, -14f),
            new Vector2(460f, 23f), 18f, TextAlignmentOptions.MidlineLeft);
        detail.enableAutoSizing = true;
        detail.fontSizeMin = 16f;
        detail.fontSizeMax = 18f;
        detail.textWrappingMode = TextWrappingModes.NoWrap;
        detail.overflowMode = TextOverflowModes.Ellipsis;
        TMP_Text action = WaveNeedRowUI.CreateText("SourceAction", rect, new Vector2(300f, 0f),
            new Vector2(180f, 34f), 20f, TextAlignmentOptions.Center);

        return new SourceCard { Background = background, Button = button, Ring = ring,
            Name = name, Detail = detail, Action = action };
    }

    private void Refresh(bool reloadBuildings = true)
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
            return;
        }

        if (reloadBuildings) CollectAvailableBuildings();
        if (reloadBuildings || controller.CurrentState == GuestWaveState.Completed)
            lastSavedWave = controller.GetLatestSavedWaveResult();
        controller.GetNeedSnapshots(snapshots);
        BuildingSlot destination = ResolveDestination();
        BuildingSlot target = !string.IsNullOrWhiteSpace(requestedBuildingKey)
            ? FindByKey(requestedBuildingKey)
            : destinationNeed.HasValue ? FindAvailable(destinationNeed.Value) : destination;
        int workerCount = 0;
        int unstaffedCount = 0;
        foreach (BuildingSlot building in available)
        {
            if (building.HasWorker) workerCount++;
            else unstaffedCount++;
        }
        workforceTotal.text = $"{workerCount} Workers  •  {available.Count} available buildings  •  {unstaffedCount} unstaffed";
        bool targetIsUnstaffed = destination != null && !destination.HasWorker;
        bool transferPhase = controller.CurrentState == GuestWaveState.Ready
            || controller.CurrentState == GuestWaveState.Active
            || controller.CurrentState == GuestWaveState.Resolving;
        bool upgradePhase = controller.CurrentState == GuestWaveState.Ready
            || controller.CurrentState == GuestWaveState.Completed;
        bool consistent = !duplicateBuilding && unstaffedCount == 1;

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
        UpdateSourceCards(destination, targetIsUnstaffed && transferPhase && consistent);

        if (controller.CurrentState == GuestWaveState.Paused)
            messageText.text = "Wave paused. Resume to move a Worker.";
        else if (!consistent)
            messageText.text = duplicateBuilding ? "Duplicate service buildings found. Transfers disabled."
                : "Exactly one available building must be unstaffed to move a Worker.";
        else if (target != null && target.HasWorker)
            messageText.text = "Tap " + DisplayName(target) + " above to move its Worker to "
                + DisplayName(destination) + ".";
        else if (targetIsUnstaffed)
            messageText.text = "Tap a staffed source above to move its Worker to "
                + DisplayName(destination) + ".";
        else
            messageText.text = "Tap an unstaffed service building to choose where to send a Worker.";

        if (!string.IsNullOrEmpty(resultMessage)) messageText.text = resultMessage;
    }

    private BuildingSlot ResolveDestination()
    {
        foreach (BuildingSlot building in available)
            if (!building.HasWorker) return building;
        return null;
    }

    private void UpdateUpgradeSection(BuildingSlot target, bool upgradePhase)
    {
        bool showUpgrade = target != null && target.IsUnlocked && upgradePhase;
        upgradeBackground.gameObject.SetActive(showUpgrade);
        upgradeButton.gameObject.SetActive(showUpgrade);
        if (!showUpgrade) return;

        int currentCapacity = controller.GetConfiguredServicePlacesForLevel(target.Level);
        if (target.Level >= 3)
        {
            upgradeCapacity.text = currentCapacity + " SERVICE PLACES  •  MAX LEVEL";
            upgradeAction.text = "Current prototype capacity is at its maximum.";
            upgradeHint.text = string.Empty;
            upgradeMissing.text = string.Empty;
            upgradeButton.interactable = false;
            upgradeButtonText.text = "MAX LEVEL  •  " + currentCapacity + " PLACES";
            upgradeButtonText.color = new Color(0.84f, 0.89f, 0.86f);
            return;
        }

        int nextCapacity = controller.GetConfiguredServicePlacesForLevel(target.Level + 1);
        int cost = controller.GetBuildingUpgradeCost(target.BuildingKey);
        bool canAfford = target.CanUpgrade;
        int addedPlaces = nextCapacity - currentCapacity;
        string benefit = "Adds " + addedPlaces + " service " + (addedPlaces == 1 ? "place" : "places")
            + ". More guests at once; speed unchanged.";
        string history = UpgradeBottleneckHint(target.BuildingKey);
        string missing = !canAfford && cost > controller.CurrentCoins
            ? "Missing: " + (cost - controller.CurrentCoins) + " Coins" : string.Empty;
        upgradeCapacity.text = currentCapacity + " → " + nextCapacity + " SERVICE PLACES";
        upgradeAction.text = benefit;
        upgradeHint.text = history;
        upgradeMissing.text = missing;
        upgradeButton.interactable = canAfford;
        upgradeButtonText.text = "UPGRADE  •  " + cost + " COINS";
        upgradeButtonText.color = canAfford ? Color.white : new Color(0.84f, 0.89f, 0.86f);
    }

    private string UpgradeBottleneckHint(string buildingKey)
    {
        if (!TryNeedForKey(buildingKey, out ResortNeed need)) return string.Empty;
        if (lastSavedWave == null) return string.Empty;
        string needKey = need == ResortNeed.OceanView ? "OCEAN_VIEW" : need.ToString().ToUpperInvariant();
        foreach (SavedWaveNeedResult service in lastSavedWave.Services)
            if (service.NeedKey == needKey && service.BottleneckSeconds > 0d)
                return "Last wave: " + (need == ResortNeed.OceanView ? "Ocean View" : need.ToString())
                    + " bottleneck. More places may reduce waits.";
        return string.Empty;
    }

    private void UpdateSourceCards(BuildingSlot target, bool canSelectSources)
    {
        int visibleIndex = 0;
        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
        {
            SourceCard card = sourceCards[need];
            BuildingSlot source = FindAvailable(need);
            bool show = source != null && source.HasWorker && source != target;
            card.Button.gameObject.SetActive(show);
            if (!show) continue;
            ((RectTransform)card.Button.transform).anchoredPosition = new Vector2(0f, -48f - visibleIndex * 69f);
            visibleIndex++;

            bool selected = selectedSourceNeed.HasValue && selectedSourceNeed.Value == need;
            bool bottleneck = IsBottleneck(need);
            card.Background.color = selected ? new Color(0.18f, 0.46f, 0.34f, 1f)
                : bottleneck ? new Color(0.40f, 0.22f, 0.20f, 0.98f)
                : new Color(0.12f, 0.32f, 0.27f, 0.98f);
            card.Name.text = DisplayName(source) + StatusText(need);
            card.Name.color = Color.white;
            sourceBaseDetails[need] = SourceDetails(need, source);
            card.Detail.text = sourceBaseDetails[need];
            card.Action.text = "MOVE WORKER";
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
        {
            text += "  •  " + (performance.Utilization * 100f).ToString("0") + "% speed";
            if (performance.EnergyBoosted) text += "  •  BOOSTED";
        }
        return text;
    }

    private string StatusText(ResortNeed need)
    {
        foreach (WaveNeedSnapshot snapshot in snapshots)
        {
            if (snapshot.Need == need && snapshot.Status == WaveNeedStatus.Bottleneck)
                return "  •  BOTTLENECK";
            if (snapshot.Need == need && snapshot.Status == WaveNeedStatus.Recovering)
                return "  •  RECOVERING";
        }
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
        BuildingSlot[] sceneBuildings = FindObjectsByType<BuildingSlot>(FindObjectsInactive.Include);
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
        TryTransferSelectedWorker();
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
        int unstaffedCount = 0;
        foreach (BuildingSlot building in available) if (!building.HasWorker) unstaffedCount++;
        if (duplicateBuilding || unstaffedCount != 1 || !allowed || target == null || target.HasWorker
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
        requestedBuildingKey = null;
        resultMessage = destinationName + " is staffed. " + sourceName + " will run unstaffed.";
        Refresh();
    }

    private void TryUpgradeFocusedBuilding()
    {
        if (upgradePurchaseInProgress || Time.unscaledTime < nextUpgradePressTime) return;
        upgradePurchaseInProgress = true;
        upgradeButton.interactable = false;
        try
        {
            CollectAvailableBuildings();
            BuildingSlot target = !string.IsNullOrWhiteSpace(requestedBuildingKey)
                ? FindByKey(requestedBuildingKey)
                : destinationNeed.HasValue ? FindAvailable(destinationNeed.Value) : ResolveDestination();
            if (target == null)
                resultMessage = "Selected building is unavailable.";
            else if (controller.CurrentState != GuestWaveState.Ready
                && controller.CurrentState != GuestWaveState.Completed)
                resultMessage = "Upgrades are unavailable during this wave phase.";
            else if (target.Level >= 3)
                resultMessage = DisplayName(target) + " is already at maximum level.";
            else
            {
                int cost = controller.GetBuildingUpgradeCost(target.BuildingKey);
                if (cost >= 0 && controller.CurrentCoins < cost)
                    resultMessage = "Missing: " + (cost - controller.CurrentCoins) + " Coins";
                else
                {
                    int previousCapacity = controller.GetConfiguredServicePlacesForLevel(target.Level);
                    if (target.TryPurchaseUpgrade())
                    {
                        target.RefreshPersistedState();
                        int capacity = controller.TryGetServicePlaces(NeedForKey(target.BuildingKey),
                            out int savedCapacity) ? savedCapacity
                            : controller.GetConfiguredServicePlacesForLevel(target.Level);
                        resultMessage = DisplayName(target) + " upgraded to Level " + target.Level
                            + ". Capacity increased from " + previousCapacity + " to " + capacity + " places.";
                        nextUpgradePressTime = Time.unscaledTime + 0.4f;
                    }
                    else
                        resultMessage = "Purchase failed. Please try again.";
                }
            }
        }
        catch (System.Exception error)
        {
            Debug.LogError("Building upgrade failed: " + error, this);
            resultMessage = "Purchase failed. Please try again.";
        }
        finally
        {
            upgradePurchaseInProgress = false;
            Refresh();
        }
    }

    private void HandleStateChanged(GuestWaveState _) => Refresh(false);
    private void HandlePerformanceChanged(ServicePerformance _) => Refresh(false);
    private void HandleNeedStateChanged(WaveNeedSnapshot snapshot)
    {
        for (int i = 0; i < snapshots.Count; i++)
        {
            if (snapshots[i].Need != snapshot.Need) continue;
            snapshots[i] = snapshot;
            break;
        }
        if (!sourceCards.TryGetValue(snapshot.Need, out SourceCard card) || !card.Button.gameObject.activeSelf)
            return;
        BuildingSlot source = FindAvailable(snapshot.Need);
        if (source == null) return;
        card.Ring.SetState(controller.GetConfiguredServicePlacesForLevel(source.Level),
            snapshot.Processing, snapshot.Status == WaveNeedStatus.Bottleneck);
        if (sourceBaseDetails.TryGetValue(snapshot.Need, out string details))
            card.Detail.text = details;
        card.Name.text = DisplayName(source) + StatusText(snapshot.Need);
        card.Background.color = snapshot.Status == WaveNeedStatus.Bottleneck
            ? new Color(0.40f, 0.22f, 0.20f, 0.98f)
            : new Color(0.12f, 0.32f, 0.27f, 0.98f);
    }
    private void HandleBuildingUpgraded(BuildingUpgradeInfo _)
    {
        if (!upgradePurchaseInProgress) Refresh();
    }

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
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 0.88f, 1f);
        colors.pressedColor = new Color(0.72f, 0.86f, 0.76f, 1f);
        colors.disabledColor = new Color(0.74f, 0.79f, 0.76f, 1f);
        button.colors = colors;
        label = WaveNeedRowUI.CreateText(objectName + "Label", rect, Vector2.zero, size - new Vector2(24f, 8f),
            19f, TextAlignmentOptions.Center);
        label.color = Color.white;
        return button;
    }

    private static void FitSingleLine(TMP_Text label, float minimumSize)
    {
        label.fontSizeMin = minimumSize;
        label.fontSizeMax = label.fontSize;
        label.enableAutoSizing = true;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
