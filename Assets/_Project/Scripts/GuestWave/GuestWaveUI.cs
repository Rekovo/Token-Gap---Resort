using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GuestWaveUI : MonoBehaviour
{
    [SerializeField] private GuestWaveController controller;
    [SerializeField] private TMP_Text waveStatusText;
    [SerializeField] private Button startWaveButton;

    private void Start()
    {
        UpdateWaveUI();
    }

    private void Update()
    {
        UpdateWaveUI();
    }

    private void UpdateWaveUI()
    {
        if (controller == null)
            return;

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
}