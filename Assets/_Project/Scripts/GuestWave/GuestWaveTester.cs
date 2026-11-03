using UnityEngine;
using UnityEngine.InputSystem;

public class GuestWaveTester : MonoBehaviour
{
    [SerializeField] private GuestWaveController controller;

    private void Update()
    {
        if (Keyboard.current == null || controller == null)
            return;

        // Start a wave with 5 guests
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            controller.StartWave(5);
        }

        // Simulate a guest arriving
        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            controller.GuestArrived();
        }

        // Simulate a guest leaving
        if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            controller.GuestLeftResort();
        }

        // Finish guest arrivals
        if (Keyboard.current.digit4Key.wasPressedThisFrame)
        {
            controller.FinishArrivals();
        }

        // Prepare the next wave
        if (Keyboard.current.digit5Key.wasPressedThisFrame)
        {
            controller.PrepareNextWave();
        }
        // Pause the current wave
        if (Keyboard.current.digit6Key.wasPressedThisFrame)
        {
            controller.PauseWave();
        }
        // Resume the paused wave
        if (Keyboard.current.digit7Key.wasPressedThisFrame)
        {
            controller.ResumeWave();
        }
    }
}