using UnityEngine;
using TMPro;

public class TopBarDatabaseUI : MonoBehaviour
{
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text energyText;
    [SerializeField] private TMP_Text guestsText;
    [SerializeField] private TMP_Text waveRewardText;
    [SerializeField] private GuestWaveController waveController;

    private ResortDatabase database;
    private string playerId = "test-player-001";
    private TMP_Text generatedRewardText;

    private void Awake()
    {
        database = new ResortDatabase();
        database.CreateNewPlayer(playerId);
        if (waveRewardText == null && moneyText != null)
        {
            GameObject rewardObject = new GameObject("WaveRewardText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            rewardObject.transform.SetParent(moneyText.transform.parent, false);
            RectTransform rewardRect = (RectTransform)rewardObject.transform;
            RectTransform moneyRect = moneyText.rectTransform;
            rewardRect.anchorMin = moneyRect.anchorMin;
            rewardRect.anchorMax = moneyRect.anchorMax;
            rewardRect.pivot = moneyRect.pivot;
            rewardRect.sizeDelta = moneyRect.sizeDelta;
            rewardRect.anchoredPosition = moneyRect.anchoredPosition + Vector2.down * (moneyRect.rect.height + 4f);
            waveRewardText = rewardObject.GetComponent<TMP_Text>();
            waveRewardText.font = moneyText.font;
            waveRewardText.fontSize = moneyText.fontSize;
            waveRewardText.color = moneyText.color;
            waveRewardText.alignment = moneyText.alignment;
            generatedRewardText = waveRewardText;
        }
    }

    private void OnEnable()
    {
        if (waveController == null) waveController = FindAnyObjectByType<GuestWaveController>();
        if (waveController != null)
        {
            waveController.OnWaveResult += HandleWaveResult;
            waveController.OnWaveStarted += HandleWaveStarted;
            waveController.OnWaveStateChanged += HandleWaveStateChanged;
            waveController.OnGuestCountChanged += HandleGuestCountChanged;
        }
        UpdateMoney();
    }

    private void OnDisable()
    {
        if (waveController != null)
        {
            waveController.OnWaveResult -= HandleWaveResult;
            waveController.OnWaveStarted -= HandleWaveStarted;
            waveController.OnWaveStateChanged -= HandleWaveStateChanged;
            waveController.OnGuestCountChanged -= HandleGuestCountChanged;
        }
    }

    private void HandleWaveResult(float satisfaction, int rating, int coins)
    {
        UpdateMoney();
        if (waveRewardText != null) waveRewardText.text = $"+{coins}";
    }

    private void HandleWaveStarted(int _) 
    {
        if (waveRewardText != null) waveRewardText.text = string.Empty;
        UpdateGuests();
    }

    private void HandleWaveStateChanged(GuestWaveState _) => UpdateGuests();
    private void HandleGuestCountChanged(int _, int __) => UpdateGuests();

    public void UpdateMoney()
    {
        int coins = database.GetCurrencyAmount(playerId, "COINS");
        int energy = database.GetCurrencyAmount(playerId, "ENERGY");
        if (moneyText != null) moneyText.text = "Coins: " + coins;
        if (energyText != null) energyText.text = "Energy: " + energy;
        UpdateGuests();
    }

    private void UpdateGuests()
    {
        if (guestsText != null && waveController != null)
            guestsText.text = $"Guests: {waveController.GuestsArrived}/{waveController.TotalGuests}";
    }

    private void OnDestroy()
    {
        database?.Dispose();
        if (generatedRewardText != null) Destroy(generatedRewardText.gameObject);
    }
}
