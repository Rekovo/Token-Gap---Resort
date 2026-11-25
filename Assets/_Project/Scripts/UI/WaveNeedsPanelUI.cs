using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Compact live need strip that expands into the full wave service panel.</summary>
public sealed class WaveNeedsPanelUI : MonoBehaviour
{
    private sealed class MiniNeedRow
    {
        public ResortNeed Need;
        public RectTransform Rect;
        public Image Background;
        public TMP_Text Name;
        public TMP_Text Count;
        public Image Fill;
    }

    [SerializeField] private GuestWaveController controller;
    [Header("Need icons (SVG textures)")]
    [SerializeField] private Texture roomsIcon;
    [SerializeField] private Texture drinksIcon;
    [SerializeField] private Texture foodIcon;
    [SerializeField] private Texture relaxIcon;
    [SerializeField] private Texture oceanViewIcon;

    private readonly List<WaveNeedSnapshot> snapshots = new List<WaveNeedSnapshot>(5);
    private readonly Dictionary<ResortNeed, WaveNeedRowUI> rows = new Dictionary<ResortNeed, WaveNeedRowUI>(5);
    private readonly Dictionary<ResortNeed, MiniNeedRow> miniRows = new Dictionary<ResortNeed, MiniNeedRow>(5);
    private readonly Dictionary<GameObject, bool> sceneChildStates = new Dictionary<GameObject, bool>();
    private readonly HashSet<ResortNeed> filteredLockedNeeds = new HashSet<ResortNeed>();
    private RectTransform expandedContent;
    private RectTransform compactContent;
    private TMP_Text arrivalText;
    private TMP_Text compactArrivalText;
    private TMP_Text toggleText;
    private Button toggleButton;
    private GameObject collapseControl;
    private Action<ResortNeed> managementOpener;
    private bool waveOverlayMode;
    private bool expanded = true;

    public bool IsCompactWaveOverlay => waveOverlayMode && !expanded;
    public bool BlocksIslandInput => !IsCompactWaveOverlay;

    /// <summary>SCRUM-84 supplies the navigation entry point to building staffing controls.</summary>
    public void SetManagementOpener(Action<ResortNeed> opener)
    {
        managementOpener = opener;
        foreach (WaveNeedRowUI row in rows.Values)
            row.SetNavigationAvailable(opener != null);
    }

    public void SetWaveOverlayMode(bool active)
    {
        waveOverlayMode = active;
        expanded = !active;
        ApplyPanelLayout();
    }

    public void SetExpanded(bool value)
    {
        if (!waveOverlayMode) return;
        expanded = value;
        ApplyPanelLayout();
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
        controller.OnNeedStateChanged += HandleNeedStateChanged;
        controller.OnArrivalProgressChanged += HandleArrivalProgressChanged;
        waveOverlayMode = IsLiveWave(controller.CurrentState);
        expanded = !waveOverlayMode;
        ApplyPanelLayout();
        RefreshFromSimulation();
    }

    private void OnDisable()
    {
        if (controller == null) return;
        controller.OnWaveStarted -= HandleWaveStarted;
        controller.OnWaveStateChanged -= HandleStateChanged;
        controller.OnNeedProgressChanged -= HandleNeedProgressChanged;
        controller.OnNeedStatusChanged -= HandleNeedStatusChanged;
        controller.OnNeedStateChanged -= HandleNeedStateChanged;
        controller.OnArrivalProgressChanged -= HandleArrivalProgressChanged;
    }

    private void BuildUI()
    {
        RectTransform panel = (RectTransform)transform;
        foreach (Transform child in panel)
            sceneChildStates[child.gameObject] = child.gameObject.activeSelf;
        panel.sizeDelta = new Vector2(900f, 740f);
        Image panelBackground = GetComponent<Image>();
        if (panelBackground != null) panelBackground.color = new Color(0.11f, 0.25f, 0.22f, 0.985f);

        GameObject expandedObject = new GameObject("ExpandedWaveNeedsContent", typeof(RectTransform));
        expandedContent = (RectTransform)expandedObject.transform;
        expandedContent.SetParent(panel, false);
        expandedContent.anchorMin = Vector2.zero;
        expandedContent.anchorMax = Vector2.one;
        expandedContent.offsetMin = Vector2.zero;
        expandedContent.offsetMax = Vector2.zero;

        GameObject compactObject = new GameObject("CompactWaveNeedsContent", typeof(RectTransform));
        compactContent = (RectTransform)compactObject.transform;
        compactContent.SetParent(panel, false);
        compactContent.anchorMin = Vector2.zero;
        compactContent.anchorMax = Vector2.one;
        compactContent.offsetMin = Vector2.zero;
        compactContent.offsetMax = Vector2.zero;

        arrivalText = WaveNeedRowUI.CreateText("WaveNeedsStatus", expandedContent,
            new Vector2(-80f, 245f), new Vector2(620f, 36f), 24f, TextAlignmentOptions.Center);
        arrivalText.color = Color.white;

        collapseControl = CreateButton("CollapseWaveNeeds", expandedContent,
            new Vector2(340f, 245f), new Vector2(120f, 48f), "CLOSE");
        collapseControl.GetComponent<Button>().onClick.AddListener(CollapseOrClose);

        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
        {
            int index = rows.Count;
            GameObject rowObject = new GameObject("NeedRow_" + need, typeof(RectTransform), typeof(CanvasRenderer));
            RectTransform rect = (RectTransform)rowObject.transform;
            rect.SetParent(expandedContent, false);
            Place(rect, new Vector2(0f, 166f - index * 67f), new Vector2(820f, 60f));
            WaveNeedRowUI row = rowObject.AddComponent<WaveNeedRowUI>();
            row.Initialize(need, IconFor(need), OpenManagement);
            row.SetNavigationAvailable(managementOpener != null);
            rowObject.SetActive(false);
            rows.Add(need, row);
            miniRows.Add(need, BuildMiniNeed(compactContent, need));
        }

        GameObject miniToggle = CreateButton("ExpandWaveNeeds", compactContent,
            new Vector2(0f, 43f), new Vector2(860f, 30f), "GUEST NEEDS  •  TAP TO EXPAND");
        toggleButton = miniToggle.GetComponent<Button>();
        toggleText = miniToggle.GetComponentInChildren<TMP_Text>();
        toggleButton.onClick.AddListener(() => SetExpanded(true));
        compactArrivalText = WaveNeedRowUI.CreateText("CompactWaveStatus", compactContent,
            new Vector2(0f, 19f), new Vector2(850f, 24f), 16f, TextAlignmentOptions.Center);
        compactArrivalText.color = Color.white;
    }

    private MiniNeedRow BuildMiniNeed(RectTransform parent, ResortNeed need)
    {
        GameObject card = CreateButton("MiniNeed_" + need, parent, Vector2.zero,
            new Vector2(160f, 52f), string.Empty);
        Button button = card.GetComponent<Button>();
        button.onClick.AddListener(() => SetExpanded(true));
        RectTransform rect = (RectTransform)card.transform;
        Image background = card.GetComponent<Image>();
        background.color = new Color(0.12f, 0.28f, 0.24f, 0.94f);

        TMP_Text name = WaveNeedRowUI.CreateText("NeedName", rect, new Vector2(-28f, 13f),
            new Vector2(100f, 20f), 14f, TextAlignmentOptions.MidlineLeft);
        name.text = WaveNeedPresentation.NameFor(need);
        name.color = Color.white;
        TMP_Text count = WaveNeedRowUI.CreateText("NeedCount", rect, new Vector2(56f, 13f),
            new Vector2(56f, 20f), 14f, TextAlignmentOptions.MidlineRight);
        count.color = Color.white;

        Image track = CreateImage("ProgressTrack", rect, new Vector2(0f, -13f),
            new Vector2(140f, 7f), new Color(0.04f, 0.13f, 0.13f, 0.95f));
        GameObject fillObject = new GameObject("ProgressFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform fillRect = (RectTransform)fillObject.transform;
        fillRect.SetParent(track.transform, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillObject.GetComponent<Image>();
        fill.color = WaveNeedPresentation.ColorFor(need);
        fill.raycastTarget = false;
        card.SetActive(false);
        return new MiniNeedRow { Need = need, Rect = rect, Background = background,
            Name = name, Count = count, Fill = fill };
    }

    private void ApplyPanelLayout()
    {
        if (expandedContent == null) return;
        RectTransform panel = (RectTransform)transform;
        if (waveOverlayMode)
        {
            RectTransform parent = panel.parent as RectTransform;
            float width = parent != null ? Mathf.Min(920f, parent.rect.width - 32f) : 880f;
            float height = expanded ? 650f : 138f;
            if (parent != null && expanded) height = Mathf.Min(height, parent.rect.height - 180f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 130f);
            panel.sizeDelta = new Vector2(Mathf.Max(320f, width), height);
        }
        else
        {
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(900f, 740f);
        }

        foreach (KeyValuePair<GameObject, bool> child in sceneChildStates)
            if (child.Key != null) child.Key.SetActive(!waveOverlayMode && child.Value);
        expandedContent.gameObject.SetActive(!waveOverlayMode || expanded);
        collapseControl.SetActive(waveOverlayMode && expanded);
        compactContent.gameObject.SetActive(waveOverlayMode && !expanded);
        if (toggleText != null) toggleText.text = "GUEST NEEDS  •  TAP TO EXPAND";
        LayoutMiniRows();
    }

    private void LayoutMiniRows()
    {
        if (compactContent == null) return;
        RectTransform parent = compactContent;
        List<MiniNeedRow> visible = new List<MiniNeedRow>(miniRows.Count);
        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
            if (miniRows[need].Rect.gameObject.activeSelf) visible.Add(miniRows[need]);
        if (visible.Count == 0) return;

        float width = Mathf.Max(72f, (parent.rect.width - 24f - (visible.Count - 1) * 8f) / visible.Count);
        float startX = -(visible.Count - 1) * (width + 8f) * 0.5f;
        for (int i = 0; i < visible.Count; i++)
        {
            RectTransform rect = visible[i].Rect;
            rect.sizeDelta = new Vector2(width, 52f);
            rect.anchoredPosition = new Vector2(startX + i * (width + 8f), -39f);
        }
    }

    private void RefreshFromSimulation()
    {
        controller.GetNeedSnapshots(snapshots);
        filteredLockedNeeds.Clear();
        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
        {
            rows[need].gameObject.SetActive(false);
            miniRows[need].Rect.gameObject.SetActive(false);
        }

        foreach (WaveNeedSnapshot snapshot in snapshots)
        {
            if (snapshot.Total <= 0) continue;
            if (!controller.IsNeedUnlocked(snapshot.Need))
            {
                filteredLockedNeeds.Add(snapshot.Need);
                continue;
            }
            if (rows.TryGetValue(snapshot.Need, out WaveNeedRowUI row))
            {
                row.SetSnapshot(snapshot);
                row.gameObject.SetActive(true);
            }
            UpdateMiniNeed(snapshot);
        }
        UpdateArrivalText();
        LayoutMiniRows();
    }

    private void UpdateMiniNeed(WaveNeedSnapshot snapshot)
    {
        if (!miniRows.TryGetValue(snapshot.Need, out MiniNeedRow row)) return;
        int total = Mathf.Max(0, snapshot.Total);
        int completed = Mathf.Clamp(snapshot.Completed, 0, total);
        row.Rect.gameObject.SetActive(total > 0);
        row.Count.text = completed + "/" + total;
        RectTransform fillRect = (RectTransform)row.Fill.transform;
        float ratio = total > 0 ? (float)completed / total : 0f;
        fillRect.anchorMax = new Vector2(ratio, 1f);
        bool bottleneck = snapshot.Status == WaveNeedStatus.Bottleneck;
        row.Fill.color = bottleneck ? new Color(0.84f, 0.36f, 0.31f) : WaveNeedPresentation.ColorFor(snapshot.Need);
        row.Background.color = bottleneck ? new Color(0.4f, 0.22f, 0.2f, 0.96f)
            : new Color(0.12f, 0.28f, 0.24f, 0.94f);
    }

    private void UpdateArrivalText()
    {
        string text;
        switch (controller.CurrentState)
        {
            case GuestWaveState.Ready:
                text = "Start a wave to see guest needs";
                break;
            case GuestWaveState.Completed:
                text = "All guests arrived • wave complete";
                break;
            case GuestWaveState.Resolving:
                text = "Arrivals ended • finishing services";
                break;
            case GuestWaveState.Paused:
                text = controller.AreNeedArrivalsClosed ? "Arrivals ended • wave paused" : "Wave paused";
                break;
            default:
                text = controller.AreNeedArrivalsClosed ? "Arrivals ended • queues still serving" : "Guests arriving";
                break;
        }
        arrivalText.text = text;
        if (compactArrivalText != null) compactArrivalText.text = text;
    }

    private void HandleWaveStarted(int _) => RefreshFromSimulation();
    private void HandleStateChanged(GuestWaveState state)
    {
        bool active = IsLiveWave(state);
        if (active != waveOverlayMode) SetWaveOverlayMode(active);
        RefreshFromSimulation();
    }
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
        foreach (WaveNeedSnapshot snapshot in snapshots)
            if (snapshot.Need == need) { UpdateMiniNeed(new WaveNeedSnapshot(need, completed, total,
                snapshot.Arrived, snapshot.Waiting, snapshot.Processing, snapshot.Status)); break; }
    }

    private void HandleNeedStatusChanged(ResortNeed need, WaveNeedStatus status, int _)
    {
        if (rows.TryGetValue(need, out WaveNeedRowUI row) && row.gameObject.activeSelf)
            row.SetStatus(status);
        for (int i = 0; i < snapshots.Count; i++)
        {
            WaveNeedSnapshot snapshot = snapshots[i];
            if (snapshot.Need != need) continue;
            snapshots[i] = new WaveNeedSnapshot(need, snapshot.Completed, snapshot.Total,
                snapshot.Arrived, snapshot.Waiting, snapshot.Processing, status);
            UpdateMiniNeed(snapshots[i]);
            break;
        }
    }

    private void HandleNeedStateChanged(WaveNeedSnapshot snapshot)
    {
        for (int i = 0; i < snapshots.Count; i++)
            if (snapshots[i].Need == snapshot.Need) { snapshots[i] = snapshot; break; }
        if (rows.TryGetValue(snapshot.Need, out WaveNeedRowUI row) && row.gameObject.activeSelf)
            row.SetSnapshot(snapshot);
        UpdateMiniNeed(snapshot);
    }

    private void OpenManagement(ResortNeed need) => managementOpener?.Invoke(need);

    private void CollapseOrClose()
    {
        if (waveOverlayMode) SetExpanded(false);
        else gameObject.SetActive(false);
    }

    private static bool IsLiveWave(GuestWaveState state) => state == GuestWaveState.Active
        || state == GuestWaveState.Resolving || state == GuestWaveState.Paused;

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

    private static GameObject CreateButton(string objectName, RectTransform parent, Vector2 position,
        Vector2 size, string label)
    {
        GameObject buttonObject = new GameObject(objectName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.SetParent(parent, false);
        Place(rect, position, size);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.10f, 0.27f, 0.23f, 0.985f);
        Button button = buttonObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        TMP_Text text = WaveNeedRowUI.CreateText(objectName + "Label", rect, Vector2.zero,
            size - new Vector2(12f, 4f), 16f, TextAlignmentOptions.Center);
        text.text = label;
        text.color = Color.white;
        return buttonObject;
    }

    private static Image CreateImage(string objectName, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        GameObject imageObject = new GameObject(objectName,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.SetParent(parent, false);
        Place(rect, position, size);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
