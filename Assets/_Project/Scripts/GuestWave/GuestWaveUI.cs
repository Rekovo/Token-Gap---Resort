using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GuestWaveUI : MonoBehaviour
{
    [SerializeField] private GuestWaveController controller;
    [SerializeField] private TMP_Text waveStatusText;
    [SerializeField] private TMP_Text waveResultText;
    [SerializeField] private Button startWaveButton;

    private void OnEnable()
    {
        if (controller != null)
        {
            controller.OnWaveStarted += HandleWaveChanged;
            controller.OnWaveCompleted += HandleWaveChanged;
            controller.OnWaveResult += HandleWaveResult;
            controller.OnWaveStateChanged += HandleWaveStateChanged;
        }
        UpdateWaveUI();
    }

    private void OnDisable()
    {
        if (controller != null)
        {
            controller.OnWaveStarted -= HandleWaveChanged;
            controller.OnWaveCompleted -= HandleWaveChanged;
            controller.OnWaveResult -= HandleWaveResult;
            controller.OnWaveStateChanged -= HandleWaveStateChanged;
        }
    }

    private void HandleWaveChanged(int _) => UpdateWaveUI();
    private void HandleWaveChanged() => UpdateWaveUI();
    private void HandleWaveStateChanged(GuestWaveState _) => UpdateWaveUI();
    private void HandleWaveResult(float satisfaction, int rating, int coins)
    {
        if (waveResultText != null)
            waveResultText.text = $"Guests served · {(rating == 6 ? "Perfect" : rating + "★")} · {satisfaction:P0} · +{coins} Coins";
        UpdateWaveUI();
    }

    private void Start()
    {
        if (startWaveButton != null && controller != null)
            startWaveButton.onClick.AddListener(controller.StartWaveFromButton);
    }

    private void UpdateWaveUI()
    {
        if (controller == null)
            return;

        if (waveStatusText == null || startWaveButton == null) return;
        switch (controller.CurrentState)
        {
            case GuestWaveState.Ready:
                waveStatusText.text = "Wave: Ready";
                startWaveButton.interactable = true;
                break;

            case GuestWaveState.Active:
                waveStatusText.text = "Wave: In Progress";
                startWaveButton.interactable = false;
                break;

            case GuestWaveState.Paused:
                waveStatusText.text = "Wave: Paused";
                startWaveButton.interactable = false;
                break;

            case GuestWaveState.Completed:
                waveStatusText.text = "Wave: Completed";
                startWaveButton.interactable = false;
                break;
        }
    }

    private void OnDestroy()
    {
        if (startWaveButton != null && controller != null)
            startWaveButton.onClick.RemoveListener(controller.StartWaveFromButton);
    }
}
