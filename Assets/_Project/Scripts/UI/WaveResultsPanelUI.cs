using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Read-only presentation of one committed wave result.</summary>
public sealed class WaveResultsPanelUI : MonoBehaviour
{
    private sealed class ServiceView
    {
        public ResortNeed Need;
        public int Demand;
        public int Served;
        public double? Contribution;
        public double? BottleneckSeconds;
    }

    private sealed class ServiceRow
    {
        public GameObject Root;
        public Image Background;
        public TMP_Text Name;
        public TMP_Text Detail;
        public TMP_Text Bottleneck;
    }

    private readonly List<ServiceRow> rows = new List<ServiceRow>(5);
    private readonly List<WaveResultStarGraphic> stars = new List<WaveResultStarGraphic>(5);
    private GuestWaveController controller;
    private Action continueAction;
    private RectTransform panelRect;
    private RectTransform contentRect;
    private RectTransform recommendationRect;
    private RectTransform continueRect;
    private TMP_Text heading;
    private TMP_Text ratingText;
    private TMP_Text satisfactionText;
    private TMP_Text coinsText;
    private TMP_Text energyText;
    private TMP_Text guestsText;
    private TMP_Text needUnitsText;
    private TMP_Text summaryText;
    private TMP_Text recommendationText;

    public bool IsShowing => gameObject.activeSelf;

    public void Initialize(GuestWaveController waveController, Action onContinue)
    {
        controller = waveController;
        continueAction = onContinue;
    }

    public void Show(WaveCompletionResult live, SavedWaveResult saved)
    {
        if (live == null) return;
        if (heading == null) BuildUI();
        if (saved != null && saved.CompletionToken != live.CompletionToken) saved = null;

        int wave = saved?.WaveNumber ?? live.WaveNumber;
        int rating = saved?.Rating ?? live.Rating;
        double satisfaction = saved?.Satisfaction ?? live.Satisfaction;
        int coins = saved?.Coins ?? live.Coins;
        int energy = saved?.Energy ?? live.Energy;
        int completed = saved?.CompletedNeedUnits ?? live.CompletedNeedUnits;
        int total = saved?.TotalNeedUnits ?? live.TotalNeedUnits;
        int guests = saved?.VisualGuestCount ?? live.VisualGuestCount;
        bool hasCompletedGuestCount = controller != null && controller.CurrentState == GuestWaveState.Completed
            && controller.LatestCompletedResult?.CompletionToken == live.CompletionToken;
        List<ServiceView> services = ReadServices(live, saved);

        heading.text = "WAVE " + wave + " COMPLETE";
        int filledStars = rating == 6 ? 5 : Mathf.Clamp(rating, 0, 5);
        for (int i = 0; i < stars.Count; i++)
            stars[i].color = i < filledStars ? new Color(1f, 0.82f, 0.43f)
                : new Color(0.35f, 0.54f, 0.47f);
        ratingText.text = rating == 6 ? "PERFECT STAY" : rating + (rating == 1 ? " STAR" : " STARS");
        satisfactionText.text = "SATISFACTION  " + (satisfaction * 100d).ToString("0.#") + "%";
        coinsText.text = "+" + coins + " COINS";
        energyText.text = "+" + energy + " ENERGY";
        guestsText.text = hasCompletedGuestCount
            ? "Guests arrived: " + controller.GuestsArrived + "/" + guests
            : "Visual guests: " + guests;
        needUnitsText.text = "Need units: " + completed + "/" + total;
        summaryText.text = completed == total ? "Every recorded need unit was served."
            : completed + " of " + total + " need units were served.";
        recommendationText.text = Recommendation(services);

        for (int i = 0; i < rows.Count; i++)
        {
            ServiceRow row = rows[i];
            if (i >= services.Count) { row.Root.SetActive(false); continue; }
            ServiceView service = services[i];
            row.Root.SetActive(true);
            row.Name.text = WaveNeedPresentation.NameFor(service.Need);
            row.Detail.text = service.Served + "/" + service.Demand + " served"
                + (service.Contribution.HasValue
                    ? "  •  Wait-time score " + (service.Contribution.Value * 100d).ToString("0.#") + "%"
                    : string.Empty);
            bool bottleneck = service.BottleneckSeconds > 0d;
            row.Bottleneck.text = bottleneck
                ? "BOTTLENECK  " + Duration(service.BottleneckSeconds.Value)
                : "No bottleneck";
            row.Bottleneck.color = bottleneck ? new Color(1f, 0.72f, 0.53f)
                : new Color(0.73f, 0.89f, 0.80f);
            row.Background.color = bottleneck ? new Color(0.34f, 0.22f, 0.20f, 1f)
                : new Color(0.12f, 0.31f, 0.26f, 1f);
        }
        int unusedRows = rows.Count - Mathf.Min(rows.Count, services.Count);
        panelRect.sizeDelta = new Vector2(1000f, 820f - unusedRows * 48f);
        panelRect.anchoredPosition = new Vector2(0f, unusedRows * 24f);
        contentRect.anchoredPosition = new Vector2(0f, -unusedRows * 24f);
        recommendationRect.anchoredPosition = new Vector2(0f, -274f + unusedRows * 48f);
        continueRect.anchoredPosition = new Vector2(0f, -350f + unusedRows * 48f);
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    private List<ServiceView> ReadServices(WaveCompletionResult live, SavedWaveResult saved)
    {
        var result = new List<ServiceView>(5);
        foreach (ResortNeed need in WaveNeedPresentation.StableOrder)
        {
            if (saved != null)
            {
                foreach (SavedWaveNeedResult service in saved.Services)
                    if (TryNeed(service.NeedKey, out ResortNeed savedNeed) && savedNeed == need)
                    {
                        result.Add(new ServiceView { Need = need, Demand = service.DemandTotal,
                            Served = service.UnitsServed, Contribution = service.AverageContribution,
                            BottleneckSeconds = service.BottleneckSeconds });
                        break;
                    }
            }
            else
            {
                foreach (WaveNeedPerformance service in live.Services)
                    if (service.Need == need)
                    {
                        result.Add(new ServiceView { Need = need, Demand = service.DemandTotal,
                            Served = service.UnitsServed, Contribution = service.AverageContribution,
                            BottleneckSeconds = service.BottleneckSeconds });
                        break;
                    }
            }
        }
        return result;
    }

    private string Recommendation(List<ServiceView> services)
    {
        ServiceView best = null;
        int bestCapacity = 0;
        int bestNextCapacity = 0;
        bool anyBottleneck = false;
        foreach (ServiceView service in services)
        {
            if (!service.BottleneckSeconds.HasValue || service.BottleneckSeconds.Value <= 0d) continue;
            anyBottleneck = true;
            if (controller == null || !controller.TryGetUnlockedServiceLevel(service.Need, out int level)
                || level >= 3 || controller.GetBuildingUpgradeCost(BuildingKey(service.Need)) < 0)
                continue;
            int current = controller.GetConfiguredServicePlacesForLevel(level);
            int next = controller.GetConfiguredServicePlacesForLevel(level + 1);
            if (next <= current || (best != null
                && service.BottleneckSeconds.Value <= best.BottleneckSeconds.Value))
                continue;
            best = service;
            bestCapacity = current;
            bestNextCapacity = next;
        }
        if (best != null)
            return "OPTIONAL: Consider upgrading " + BuildingName(best.Need) + " (" + bestCapacity
                + " → " + bestNextCapacity + " places). " + WaveNeedPresentation.NameFor(best.Need)
                + " had a bottleneck; more places may reduce waiting.";
        return anyBottleneck ? "Bottlenecks were recorded, but no eligible capacity upgrade is available."
            : "No bottlenecks were recorded. No capacity upgrade is suggested.";
    }

    private void BuildUI()
    {
        RectTransform backdrop = (RectTransform)transform;
        backdrop.anchorMin = Vector2.zero;
        backdrop.anchorMax = Vector2.one;
        backdrop.offsetMin = Vector2.zero;
        backdrop.offsetMax = Vector2.zero;
        Image shade = GetComponent<Image>();
        shade.color = new Color(0.025f, 0.08f, 0.08f, 0.84f);
        shade.raycastTarget = true;

        panelRect = CreateImage("ResultsCard", backdrop, Vector2.zero,
            new Vector2(1000f, 820f), new Color(0.08f, 0.20f, 0.18f, 1f));
        GameObject content = new GameObject("ResultsContent", typeof(RectTransform));
        contentRect = (RectTransform)content.transform;
        contentRect.SetParent(panelRect, false);
        contentRect.anchorMin = contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(1000f, 820f);
        heading = Text("WaveHeading", contentRect, new Vector2(0f, 350f), new Vector2(920f, 42f),
            34f, new Color(0.85f, 0.96f, 0.89f));
        RectTransform starRow = new GameObject("RatingStars", typeof(RectTransform)).GetComponent<RectTransform>();
        starRow.SetParent(contentRect, false);
        starRow.anchorMin = starRow.anchorMax = new Vector2(0.5f, 0.5f);
        starRow.pivot = new Vector2(0.5f, 0.5f);
        starRow.anchoredPosition = new Vector2(0f, 300f);
        starRow.sizeDelta = new Vector2(340f, 58f);
        for (int i = 0; i < 5; i++)
        {
            GameObject star = new GameObject("RatingStar" + (i + 1), typeof(RectTransform),
                typeof(CanvasRenderer), typeof(WaveResultStarGraphic));
            RectTransform starRect = (RectTransform)star.transform;
            starRect.SetParent(starRow, false);
            starRect.anchorMin = starRect.anchorMax = new Vector2(0.5f, 0.5f);
            starRect.pivot = new Vector2(0.5f, 0.5f);
            starRect.anchoredPosition = new Vector2((i - 2) * 64f, 0f);
            starRect.sizeDelta = new Vector2(56f, 56f);
            WaveResultStarGraphic graphic = star.GetComponent<WaveResultStarGraphic>();
            graphic.raycastTarget = false;
            stars.Add(graphic);
        }
        ratingText = Text("RatingLabel", contentRect, new Vector2(0f, 255f),
            new Vector2(920f, 28f), 25f, new Color(1f, 0.82f, 0.43f));
        satisfactionText = Text("Satisfaction", contentRect, new Vector2(0f, 214f), new Vector2(920f, 34f),
            30f, Color.white);
        RectTransform coinCard = CreateImage("CoinReward", contentRect, new Vector2(-230f, 155f),
            new Vector2(440f, 80f), new Color(0.17f, 0.38f, 0.29f, 1f));
        coinsText = Text("CoinsEarned", coinCard, Vector2.zero, new Vector2(410f, 52f),
            31f, new Color(1f, 0.85f, 0.52f));
        RectTransform energyCard = CreateImage("EnergyReward", contentRect, new Vector2(230f, 155f),
            new Vector2(440f, 80f), new Color(0.17f, 0.38f, 0.29f, 1f));
        energyText = Text("EnergyEarned", energyCard, Vector2.zero, new Vector2(410f, 52f),
            31f, new Color(0.75f, 0.94f, 0.84f));
        guestsText = Text("GuestCount", contentRect, new Vector2(-230f, 90f),
            new Vector2(440f, 30f), 22f, Color.white);
        needUnitsText = Text("NeedUnitCount", contentRect, new Vector2(230f, 90f),
            new Vector2(440f, 30f), 22f, Color.white);
        summaryText = Text("PositiveSummary", contentRect, new Vector2(0f, 50f),
            new Vector2(920f, 32f), 22f, new Color(0.78f, 0.93f, 0.84f));
        Text("ServiceHeading", contentRect, new Vector2(0f, 12f), new Vector2(920f, 26f),
            20f, new Color(0.85f, 0.95f, 0.88f)).text = "SERVICE PERFORMANCE";
        for (int i = 0; i < WaveNeedPresentation.StableOrder.Length; i++)
            rows.Add(CreateServiceRow(contentRect, -31f - i * 48f));
        recommendationRect = CreateImage("RecommendationCard", contentRect, new Vector2(0f, -274f),
            new Vector2(920f, 54f), new Color(0.17f, 0.33f, 0.27f, 1f));
        recommendationText = Text("Recommendation", recommendationRect, Vector2.zero,
            new Vector2(884f, 48f), 19f, new Color(1f, 0.86f, 0.63f));
        recommendationText.textWrappingMode = TextWrappingModes.Normal;

        continueRect = CreateImage("ContinueButton", contentRect, new Vector2(0f, -350f),
            new Vector2(760f, 90f), new Color(0.18f, 0.47f, 0.34f, 1f));
        Button button = continueRect.gameObject.AddComponent<Button>();
        button.onClick.AddListener(() => continueAction?.Invoke());
        Text("ContinueLabel", continueRect, Vector2.zero, new Vector2(720f, 54f),
            28f, Color.white).text = "CONTINUE";
    }

    private ServiceRow CreateServiceRow(RectTransform panel, float y)
    {
        RectTransform card = CreateImage("ServiceResult", panel, new Vector2(0f, y),
            new Vector2(920f, 44f), new Color(0.12f, 0.31f, 0.26f, 1f));
        TMP_Text name = Text("ServiceName", card, new Vector2(-330f, 0f),
            new Vector2(220f, 32f), 21f, Color.white);
        TMP_Text detail = Text("ServiceDetail", card, new Vector2(5f, 0f),
            new Vector2(420f, 32f), 18f, Color.white);
        TMP_Text bottleneck = Text("Bottleneck", card, new Vector2(335f, 0f),
            new Vector2(230f, 32f), 18f, Color.white);
        return new ServiceRow { Root = card.gameObject, Background = card.GetComponent<Image>(),
            Name = name, Detail = detail, Bottleneck = bottleneck };
    }

    private static TMP_Text Text(string name, Transform parent, Vector2 position, Vector2 size,
        float sizeInPoints, Color color)
    {
        TMP_Text label = WaveNeedRowUI.CreateText(name, parent, position, size,
            sizeInPoints, TextAlignmentOptions.Center);
        label.color = color;
        label.enableAutoSizing = true;
        label.fontSizeMin = sizeInPoints - 2f;
        label.fontSizeMax = sizeInPoints;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private static RectTransform CreateImage(string name, Transform parent, Vector2 position,
        Vector2 size, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)obj.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = obj.GetComponent<Image>();
        image.color = color;
        return rect;
    }

    private static string Duration(double seconds) => seconds < 0.1d ? "<0.1s"
        : seconds.ToString("0.#") + "s";

    private static bool TryNeed(string key, out ResortNeed need)
    {
        switch (key)
        {
            case "ROOMS": need = ResortNeed.Rooms; return true;
            case "DRINKS": need = ResortNeed.Drinks; return true;
            case "FOOD": need = ResortNeed.Food; return true;
            case "RELAX": need = ResortNeed.Relax; return true;
            case "OCEAN_VIEW": need = ResortNeed.OceanView; return true;
            default: need = default; return false;
        }
    }

    private static string BuildingKey(ResortNeed need)
    {
        switch (need)
        {
            case ResortNeed.Rooms: return "RECEPTION";
            case ResortNeed.Drinks: return "BEACH_BAR";
            case ResortNeed.Food: return "RESTAURANT";
            case ResortNeed.Relax: return "POOL";
            case ResortNeed.OceanView: return "BUNGALOWS";
            default: throw new ArgumentOutOfRangeException(nameof(need));
        }
    }

    private static string BuildingName(ResortNeed need)
    {
        switch (need)
        {
            case ResortNeed.Rooms: return "Reception";
            case ResortNeed.Drinks: return "Beach Bar";
            case ResortNeed.Food: return "Restaurant";
            case ResortNeed.Relax: return "Pool";
            case ResortNeed.OceanView: return "Bungalows";
            default: throw new ArgumentOutOfRangeException(nameof(need));
        }
    }
}

/// <summary>Five-point UI star that does not depend on a font glyph or an imported sprite.</summary>
public sealed class WaveResultStarGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float outer = Mathf.Min(rect.width, rect.height) * 0.48f;
        float inner = outer * 0.44f;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = new Vector3(center.x, center.y);
        helper.AddVert(vertex);
        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float radius = i % 2 == 0 ? outer : inner;
            vertex.position = new Vector3(center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius);
            helper.AddVert(vertex);
        }
        for (int i = 0; i < 10; i++)
            helper.AddTriangle(0, i + 1, (i + 1) % 10 + 1);
    }
}
