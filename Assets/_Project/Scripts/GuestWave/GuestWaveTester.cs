using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class GuestWaveTester : MonoBehaviour
{
    [SerializeField] private GuestWaveController controller;

#if UNITY_EDITOR
    private const string DebugPlayerId = "test-player-001";
    private static readonly ResortNeed[] AllNeeds = (ResortNeed[])Enum.GetValues(typeof(ResortNeed));
    private ResortDatabase debugDatabase;
    private bool showDebugOverlay = true;
    private string lastDebugAction = "Ready. Start a wave with 1.";

    private void Awake()
    {
        if (controller == null) controller = FindAnyObjectByType<GuestWaveController>();
        debugDatabase = new ResortDatabase();
        debugDatabase.CreateNewPlayer(DebugPlayerId);
    }
#endif

    private void Update()
    {
        if (Keyboard.current == null || controller == null) return;

#if UNITY_EDITOR
        if (Keyboard.current.f2Key.wasPressedThisFrame)
            showDebugOverlay = !showDebugOverlay;

        if (Keyboard.current.rKey.wasPressedThisFrame)
            ToggleReceptionBeachBarWorker();

        if (Keyboard.current.gKey.wasPressedThisFrame)
            GrantTestEnergy();

        if (Keyboard.current.qKey.wasPressedThisFrame)
            TryTestBoost();
#endif

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

#if UNITY_EDITOR
    private void ToggleReceptionBeachBarWorker()
    {
        BuildingSlot reception = FindBuilding("RECEPTION");
        BuildingSlot beachBar = FindBuilding("BEACH_BAR");
        if (reception == null || beachBar == null)
        {
            lastDebugAction = "Worker check unavailable: Reception/Beach Bar slot missing.";
            Debug.LogWarning(lastDebugAction, this);
            return;
        }

        BuildingSlot source = reception.HasWorker ? reception : beachBar.HasWorker ? beachBar : null;
        BuildingSlot destination = source == reception ? beachBar : reception;
        if (source == null)
        {
            lastDebugAction = "Worker check unavailable: neither building has a Worker.";
            Debug.LogWarning(lastDebugAction, this);
            return;
        }

        bool moved = source.TryMoveWorkerTo(destination);
        lastDebugAction = moved
            ? $"Worker moved {source.BuildingKey} -> {destination.BuildingKey}; compare the staffed/rate rows."
            : $"Worker move failed ({source.BuildingKey} -> {destination.BuildingKey}).";
        Debug.Log(lastDebugAction, this);
    }

    private void GrantTestEnergy()
    {
        if (debugDatabase == null) return;
        debugDatabase.AddCurrency(DebugPlayerId, "ENERGY", 3, "DEV_TEST_ENERGY_GRANT");
        int balance = debugDatabase.GetCurrencyAmount(DebugPlayerId, "ENERGY");
        lastDebugAction = $"Development test grant: +3 Energy. Current balance: {balance}.";
        Debug.Log(lastDebugAction, this);
    }

    private void TryTestBoost()
    {
        if (debugDatabase == null) return;
        int before = debugDatabase.GetCurrencyAmount(DebugPlayerId, "ENERGY");
        bool succeeded = controller.TryBoost(ResortNeed.Drinks);
        int after = debugDatabase.GetCurrencyAmount(DebugPlayerId, "ENERGY");
        lastDebugAction = succeeded
            ? $"Boost PASS: Drinks, Energy {before} -> {after}; rate and 15s timer shown below."
            : $"Boost correctly rejected: Drinks, Energy {before} -> {after}. Check wave state, balance, and once-per-wave limit.";
        Debug.Log(lastDebugAction, this);
    }

    private static BuildingSlot FindBuilding(string key)
    {
        BuildingSlot[] slots = FindObjectsByType<BuildingSlot>();
        foreach (BuildingSlot slot in slots)
            if (slot != null && slot.BuildingKey == key) return slot;
        return null;
    }

    private void OnGUI()
    {
        if (!showDebugOverlay || controller == null) return;

        GUILayout.BeginArea(new Rect(12f, 12f, 450f, 430f), "QA / DEVELOPMENT ONLY", GUI.skin.window);
        GUILayout.Label("QA / DEVELOPMENT ONLY — F2 hides this panel");
        GUILayout.Label($"Wave: {controller.CurrentState}   Energy: {GetEnergyBalance()}");
        GUILayout.Label("1 start | R move Worker | G +3 test Energy | Q try Drinks boost | 6 pause | 7 resume");
        GUILayout.Space(6f);
        GUILayout.Label(lastDebugAction);
        GUILayout.Space(6f);

        foreach (ResortNeed need in AllNeeds)
        {
            if (!controller.TryGetServicePerformance(need, out ServicePerformance performance)) continue;
            float effectiveUnitSeconds = performance.ServiceRatePerSecond > 0f
                ? 1f / performance.ServiceRatePerSecond
                : float.PositiveInfinity;
            string boost = performance.EnergyBoosted
                ? $"BOOST {performance.BoostSecondsRemaining:F1}s"
                : "no boost";
            GUILayout.Label($"{need}: {(performance.Staffed ? "staffed" : "empty")}, util {performance.Utilization:P0}, " +
                $"{performance.ServiceRatePerSecond:F3}/s ({effectiveUnitSeconds:F1}s/place), {boost}");
        }

        GUILayout.Space(6f);
        GUILayout.Label("Service places (remaining base work):");
        GUILayout.Label(controller.ServiceDiagnostics);
        GUILayout.EndArea();
    }

    private int GetEnergyBalance() => debugDatabase != null
        ? debugDatabase.GetCurrencyAmount(DebugPlayerId, "ENERGY")
        : 0;

    private void OnDestroy() => debugDatabase?.Dispose();
#endif
}
