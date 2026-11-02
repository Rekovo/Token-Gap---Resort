using System;
using UnityEngine;

public class GuestWaveController : MonoBehaviour
{
    // Current wave state
    public GuestWaveState CurrentState { get; private set; }
        = GuestWaveState.Ready;

    // Events for other gameplay systems
    public event Action<int> OnWaveStarted;
    public event Action OnWaveCompleted;

    // Guest tracking
    public int TotalGuests { get; private set; }
    public int GuestsArrived { get; private set; }
    public int GuestsLeft { get; private set; }

    private bool arrivalsFinished;

    // Changes the wave state
    private void ChangeState(GuestWaveState newState)
    {
        CurrentState = newState;

        Debug.Log("Guest Wave State: " + CurrentState);
    }

    // Called when the player presses Start Wave
    public void StartWaveFromButton()
    {
        if (CurrentState != GuestWaveState.Ready)
            return;

        StartWave(5);
    }

    // Starts a new wave
    public void StartWave(int guestCount)
    {
        if (CurrentState != GuestWaveState.Ready || guestCount <= 0)
            return;

        TotalGuests = guestCount;
        GuestsArrived = 0;
        GuestsLeft = 0;
        arrivalsFinished = false;

        ChangeState(GuestWaveState.Active);

        Debug.Log("Wave started with " + TotalGuests + " guests.");

        // Notify other systems that a wave has started
        OnWaveStarted?.Invoke(TotalGuests);
    }

    // Called when a guest arrives at the resort
    public void GuestArrived()
    {
        if (CurrentState != GuestWaveState.Active)
            return;

        if (GuestsArrived >= TotalGuests)
            return;

        GuestsArrived++;

        Debug.Log("Guests arrived: " + GuestsArrived + "/" + TotalGuests);
    }

    // Called when a guest leaves the resort
    public void GuestLeftResort()
    {
        if (CurrentState != GuestWaveState.Active)
            return;

        if (GuestsLeft >= GuestsArrived)
            return;

        GuestsLeft++;

        Debug.Log("Guests left: " + GuestsLeft + "/" + TotalGuests);

        CheckWaveCompletion();
    }

    // Called when all scheduled guests have arrived
    public void FinishArrivals()
    {
        if (CurrentState != GuestWaveState.Active)
            return;

        if (GuestsArrived != TotalGuests)
            return;

        arrivalsFinished = true;

        Debug.Log("All guests have arrived.");

        CheckWaveCompletion();
    }

    // Checks whether the wave can finish
    private void CheckWaveCompletion()
    {
        if (CurrentState != GuestWaveState.Active)
            return;

        if (arrivalsFinished && GuestsLeft == TotalGuests)
        {
            CompleteWave();
        }
    }

    // Completes the current wave
    private void CompleteWave()
    {
        ChangeState(GuestWaveState.Completed);

        Debug.Log("All guests have left. Wave completed!");

        // Notify other systems that the wave has finished
        OnWaveCompleted?.Invoke();
    }

    // Prepares the next wave
    public void PrepareNextWave()
    {
        if (CurrentState != GuestWaveState.Completed)
            return;

        ChangeState(GuestWaveState.Ready);
    }

    // Pauses the current wave
    public void PauseWave()
    {
        if (CurrentState != GuestWaveState.Active)
            return;

        ChangeState(GuestWaveState.Paused);
    }

    // Resumes a paused wave
    public void ResumeWave()
    {
        if (CurrentState != GuestWaveState.Paused)
            return;

        ChangeState(GuestWaveState.Active);
    }
}