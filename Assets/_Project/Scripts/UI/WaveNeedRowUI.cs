using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One fixed-position, aggregated service need row.</summary>
public sealed class WaveNeedRowUI : MonoBehaviour
{
    private ResortNeed need;
    private TMP_Text countText;
    private TMP_Text bottleneckText;
    private Image progressFill;
    private Image background;
    private Button button;
    private Action<ResortNeed> selected;
    private int completed;
    private int total;
    private WaveNeedStatus status;

    public void Initialize(ResortNeed serviceNeed, Texture icon, Action<ResortNeed> onSelected)
    {
        need = serviceNeed;
        selected = onSelected;
        Color accent = WaveNeedPresentation.ColorFor(need);
        bool premium = need == ResortNeed.OceanView;

        background = gameObject.AddComponent<Image>();
        background.color = premium
            ? new Color(0.26f, 0.22f, 0.30f, 0.94f)
            : new Color(0.10f, 0.25f, 0.23f, 0.92f);

        button = gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.interactable = false; // SCRUM-84 supplies the navigation handler later.
        button.onClick.AddListener(() => selected?.Invoke(need));

        Image accentLine = CreateImage("Accent", transform, new Vector2(-402f, 0f), new Vector2(5f, 55f), accent);
        accentLine.raycastTarget = false;

        GameObject iconObject = new GameObject("NeedIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.SetParent(transform, false);
        Place(iconRect, new Vector2(-374f, 7f), new Vector2(33f, 33f));
        RawImage iconImage = iconObject.GetComponent<RawImage>();
        iconImage.texture = icon;
        iconImage.color = accent;
        iconImage.raycastTarget = false;

        TMP_Text nameText = CreateText("NeedName", transform, new Vector2(-249f, 8f),
            new Vector2(204f, 34f), premium ? 25f : 24f, TextAlignmentOptions.MidlineLeft);
        nameText.text = WaveNeedPresentation.NameFor(need);
        nameText.color = premium ? accent : Color.white;

        countText = CreateText("Completion", transform, new Vector2(211f, 8f),
            new Vector2(100f, 34f), 24f, TextAlignmentOptions.MidlineRight);
        countText.color = Color.white;

        bottleneckText = CreateText("Bottleneck", transform, new Vector2(332f, 8f),
            new Vector2(125f, 32f), 17f, TextAlignmentOptions.Center);
        bottleneckText.color = new Color(1f, 0.58f, 0.43f);

        Image track = CreateImage("ProgressTrack", transform, new Vector2(-45f, -19f),
            new Vector2(500f, 9f), new Color(0.04f, 0.13f, 0.13f, 0.95f));
        track.raycastTarget = false;
        GameObject fillObject = new GameObject("ProgressFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform fillRect = (RectTransform)fillObject.transform;
        fillRect.SetParent(track.transform, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        progressFill = fillObject.GetComponent<Image>();
        progressFill.color = accent;
        progressFill.raycastTarget = false;
    }

    public void SetNavigationAvailable(bool available)
    {
        if (button != null) button.interactable = available;
    }

    public void SetSnapshot(WaveNeedSnapshot snapshot)
    {
        completed = snapshot.Completed;
        total = snapshot.Total;
        status = snapshot.Status;
        Render();
    }

    public void SetProgress(int served, int demandTotal)
    {
        if (completed == served && total == demandTotal) return;
        completed = served;
        total = demandTotal;
        Render();
    }

    public void SetStatus(WaveNeedStatus newStatus)
    {
        if (status == newStatus) return;
        status = newStatus;
        Render();
    }

    private void Render()
    {
        int shownTotal = Mathf.Max(0, total);
        int shownCompleted = Mathf.Clamp(completed, 0, shownTotal);
        countText.text = $"{shownCompleted}/{shownTotal}";
        RectTransform fillRect = (RectTransform)progressFill.transform;
        fillRect.anchorMax = new Vector2(shownTotal > 0 ? (float)shownCompleted / shownTotal : 0f, 1f);
        bool bottleneck = status == WaveNeedStatus.Bottleneck;
        bottleneckText.text = bottleneck ? "BOTTLENECK" : string.Empty;
        background.color = bottleneck
            ? new Color(0.33f, 0.20f, 0.19f, 0.96f)
            : need == ResortNeed.OceanView
                ? new Color(0.26f, 0.22f, 0.30f, 0.94f)
                : new Color(0.10f, 0.25f, 0.23f, 0.92f);
    }

    public static TMP_Text CreateText(string objectName, Transform parent, Vector2 position,
        Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rect = (RectTransform)textObject.transform;
        rect.SetParent(parent, false);
        Place(rect, position, size);
        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.raycastTarget = false;
        return label;
    }

    private static Image CreateImage(string objectName, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.SetParent(parent, false);
        Place(rect, position, size);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
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
