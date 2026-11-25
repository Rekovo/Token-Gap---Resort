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
    private string staffingReadout = "Staffing data loading...";
    private float nextStaffingReadoutTime;
    private Vector2 debugScroll;

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

        if (Keyboard.current.tKey.wasPressedThisFrame)
            PreparePoolPurchaseTest();

        if (Keyboard.current.pKey.wasPressedThisFrame)
            TryTestPoolPurchase();
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

    private void PreparePoolPurchaseTest()
    {
        if (debugDatabase == null || controller == null) return;
        if (controller.CurrentState == GuestWaveState.Active || controller.CurrentState == GuestWaveState.Resolving
            || controller.CurrentState == GuestWaveState.Paused)
        {
            lastDebugAction = "Wait until the current wave is complete before preparing a Pool purchase test.";
            Debug.LogWarning(lastDebugAction, this);
            return;
        }

        BuildingSlot pool = FindBuilding("POOL");
        if (pool == null)
        {
            lastDebugAction = "Pool purchase test unavailable: Pool BuildingSlot is missing.";
            Debug.LogWarning(lastDebugAction, this);
            return;
        }
        if (pool.IsUnlocked)
        {
            lastDebugAction = "Pool is already purchased. Staffing: " + RefreshStaffingReadout();
            Debug.Log(lastDebugAction, this);
            return;
        }

        int cost = controller.GetBuildingUnlockCost("POOL");
        if (cost < 0)
        {
            lastDebugAction = "Pool purchase test unavailable: Pool unlock cost is not configured.";
            Debug.LogWarning(lastDebugAction, this);
            return;
        }

        // Keep QA grants out of completed-wave history.
        if (debugDatabase.GetHighestWaveCompleted(DebugPlayerId) < 3)
        {
            lastDebugAction = "Complete Wave 3 before preparing a Pool purchase test.";
            Debug.LogWarning(lastDebugAction, this);
            return;
        }
        debugDatabase.AddCurrency(DebugPlayerId, "COINS", cost, "DEV_TEST_POOL_PURCHASE_GRANT");
        lastDebugAction = $"Pool purchase test ready: +{cost} test Coins granted. Press P to purchase.";
        Debug.Log(lastDebugAction, this);
        RefreshStaffingReadout();
    }

    private void TryTestPoolPurchase()
    {
        BuildingSlot pool = FindBuilding("POOL");
        if (pool == null)
        {
            lastDebugAction = "Pool purchase test unavailable: Pool BuildingSlot is missing.";
            Debug.LogWarning(lastDebugAction, this);
            return;
        }

        pool.RefreshPersistedState();
        if (pool.IsUnlocked)
        {
            lastDebugAction = "Pool purchase already complete. " + RefreshStaffingReadout();
            Debug.Log(lastDebugAction, this);
            return;
        }

        bool purchased = pool.TryPurchaseUnlock();
        pool.RefreshPersistedState();
        pool.GetComponent<BuildingWorkerIndicator>()?.RefreshFromSave();
        string staffing = RefreshStaffingReadout();
        lastDebugAction = purchased
            ? "Pool purchase PASS: Pool is unlocked and staffed. " + staffing
            : "Pool purchase failed. Check wave state, Wave 3 gate, and Coins. " + staffing;
        Debug.Log(lastDebugAction, this);
    }

    private string RefreshStaffingReadout()
    {
        BuildingSlot[] slots = FindObjectsByType<BuildingSlot>();
        int availableBuildings = 0;
        int assignedWorkers = 0;
        foreach (BuildingSlot slot in slots)
        {
            if (slot == null) continue;
            slot.RefreshPersistedState();
            if (!slot.IsUnlocked) continue;
            availableBuildings++;
            if (slot.HasWorker) assignedWorkers++;
        }

        BuildingSlot pool = FindBuilding("POOL");
        string poolStatus = pool == null ? "Pool slot missing"
            : pool.IsUnlocked ? (pool.HasWorker ? "Pool purchased / staffed" : "Pool purchased / unstaffed")
            : "Pool not purchased";
        staffingReadout = $"{assignedWorkers} Workers / {availableBuildings} available buildings; " +
            $"{availableBuildings - assignedWorkers} unstaffed. {poolStatus}.";
        nextStaffingReadoutTime = Time.unscaledTime + 0.25f;
        return staffingReadout;
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

        if (Time.unscaledTime >= nextStaffingReadoutTime)
            RefreshStaffingReadout();

        float width = Mathf.Min(470f, Screen.width - 24f);
        float height = Mathf.Max(160f, Mathf.Min(500f, Screen.height - 132f));
        GUILayout.BeginArea(new Rect(Screen.width - width - 12f, 120f, width, height),
            "QA / DEVELOPMENT ONLY", GUI.skin.window);
        debugScroll = GUILayout.BeginScrollView(debugScroll);
        GUILayout.Label("QA / DEVELOPMENT ONLY — F2 hides this panel");
        GUILayout.Label($"Wave: {controller.CurrentState}   Energy: {GetEnergyBalance()}   Coins: {GetCoinBalance()}");
        GUILayout.Label("1 start | R move Worker | G +3 Energy | Q Drinks boost | 6 pause | 7 resume");
        GUILayout.Label("T prepare Pool purchase test | P purchase Pool");
        GUILayout.Label(staffingReadout);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Prepare Pool purchase test")) PreparePoolPurchaseTest();
        if (GUILayout.Button("Purchase Pool")) TryTestPoolPurchase();
        GUILayout.EndHorizontal();
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
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private int GetEnergyBalance() => debugDatabase != null
        ? debugDatabase.GetCurrencyAmount(DebugPlayerId, "ENERGY")
        : 0;

    private int GetCoinBalance() => debugDatabase != null
        ? debugDatabase.GetCurrencyAmount(DebugPlayerId, "COINS")
        : 0;

    private void OnDestroy() => debugDatabase?.Dispose();
#endif
}
