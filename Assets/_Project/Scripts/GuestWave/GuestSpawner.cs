using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public sealed class GuestSpawner : MonoBehaviour
{
    private sealed class PendingVisit
    {
        public ResortNeed Need;
        public int UnitIndex;
    }

    [Header("References")]
    [SerializeField] private GuestWaveController waveController;
    [SerializeField] private GameObject guestPrefab;
    [SerializeField] private Transform spawnPoint;

    [Header("Arrival presentation")]
    [Tooltip("Studio prototype spec: the incoming guest group enters over approximately 2–4 seconds.")]
    [SerializeField, Min(2f)] private float guestGroupArrivalSeconds = 3f;

    private readonly List<GuestMovement> spawnedGuests = new List<GuestMovement>();
    private GuestMovement[] guestsByIndex = Array.Empty<GuestMovement>();
    private Dictionary<ResortNeed, Transform> needDestinations = new Dictionary<ResortNeed, Transform>();
    private readonly Queue<PendingVisit> pendingVisits = new Queue<PendingVisit>();
    private readonly HashSet<long> routedNeedUnits = new HashSet<long>();
    private int targetGuestCount;
    private int nextGuestIndex;
    private int nextVisualRecipient;
    private float spawnCountdown;
    private float spawnInterval;
    private bool returningGuests;

    private void OnEnable()
    {
        if (waveController == null)
            waveController = FindAnyObjectByType<GuestWaveController>();

        if (waveController == null)
        {
            Debug.LogError("GuestSpawner: no GuestWaveController is assigned or present in the scene.", this);
            return;
        }

        waveController.OnWaveStarted += HandleWaveStarted;
        waveController.OnWaveCompleted += HandleWaveCompleted;
        waveController.OnNeedArrived += HandleNeedArrived;
    }

    private void OnDisable()
    {
        if (waveController != null)
        {
            waveController.OnWaveStarted -= HandleWaveStarted;
            waveController.OnWaveCompleted -= HandleWaveCompleted;
            waveController.OnNeedArrived -= HandleNeedArrived;
        }
        ClearSpawnedGuests();
    }

    private void Update()
    {
        if (waveController == null || returningGuests || waveController.CurrentState != GuestWaveState.Active)
            return;

        SpawnIncomingGuest();
        DispatchPendingVisits();
    }

    private void HandleWaveStarted(int guestCount)
    {
        ClearSpawnedGuests();
        if (guestPrefab == null || spawnPoint == null)
        {
            Debug.LogError("GuestSpawner: assign a guest prefab and island entrance spawn point.", this);
            return;
        }

        targetGuestCount = Mathf.Max(0, guestCount);
        pendingVisits.Clear();
        routedNeedUnits.Clear();
        if (targetGuestCount == 0)
            return;

        needDestinations = ResolveNeedDestinations();
        if (needDestinations.Count == 0)
        {
            Debug.LogError("GuestSpawner: no wave demand has a matching building destination.", this);
            targetGuestCount = 0;
            return;
        }

        guestsByIndex = new GuestMovement[targetGuestCount];
        nextVisualRecipient = 0;

        spawnInterval = targetGuestCount > 1
            ? guestGroupArrivalSeconds / (targetGuestCount - 1)
            : 0f;
        spawnCountdown = 0f;
        returningGuests = false;
    }

    private void SpawnIncomingGuest()
    {
        if (nextGuestIndex >= targetGuestCount)
            return;

        spawnCountdown -= Time.deltaTime;
        if (spawnCountdown > 0f)
            return;

        SpawnGuest(nextGuestIndex++);
        spawnCountdown = spawnInterval;
    }

    private void SpawnGuest(int guestIndex)
    {
        Vector3 entrancePosition = spawnPoint.position;
        GameObject guestObject = Instantiate(guestPrefab, entrancePosition, spawnPoint.rotation);
        guestObject.name = $"Guest_{guestIndex + 1:00}";

        GuestMovement movement = guestObject.GetComponent<GuestMovement>();
        if (movement == null)
        {
            Debug.LogError($"{guestObject.name} is missing GuestMovement.", guestObject);
            Destroy(guestObject);
            return;
        }

        movement.BeginWave(entrancePosition, waveController);
        movement.StartedWalking += HandleGuestStartedWalking;
        movement.ReturnedToEntrance += HandleGuestReturned;
        spawnedGuests.Add(movement);
        guestsByIndex[guestIndex] = movement;
    }

    private void HandleNeedArrived(ResortNeed need, int unitIndex)
    {
        if (!needDestinations.ContainsKey(need))
        {
            Debug.LogWarning($"GuestSpawner: no unlocked visual destination is available for {need}; service simulation continues.", this);
            return;
        }

        long key = ((long)(int)need << 32) | (uint)unitIndex;
        if (!routedNeedUnits.Add(key)) return;
        pendingVisits.Enqueue(new PendingVisit { Need = need, UnitIndex = unitIndex });
        DispatchPendingVisits();
    }

    private void DispatchPendingVisits()
    {
        while (pendingVisits.Count > 0 && guestsByIndex.Length > 0)
        {
            int guestIndex = FindNextVisualRecipient();
            if (guestIndex < 0) return;
            PendingVisit visit = pendingVisits.Dequeue();
            Transform building = needDestinations[visit.Need];
            int guestSlot = guestIndex % 8;
            float angle = (guestSlot * 45f + visit.UnitIndex * 17f) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                * (0.45f + (guestSlot / 4) * 0.3f);
            guestsByIndex[guestIndex].QueueVisit(visit.Need, visit.UnitIndex, building.position + offset);
        }
    }

    private int FindNextVisualRecipient()
    {
        if (guestsByIndex == null || guestsByIndex.Length == 0) return -1;
        for (int offset = 0; offset < guestsByIndex.Length; offset++)
        {
            int candidate = (nextVisualRecipient + offset) % guestsByIndex.Length;
            if (guestsByIndex[candidate] == null) continue;
            nextVisualRecipient = (candidate + 1) % guestsByIndex.Length;
            return candidate;
        }
        return -1;
    }

    private Dictionary<ResortNeed, Transform> ResolveNeedDestinations()
    {
        Dictionary<ResortNeed, Transform> resolved = new Dictionary<ResortNeed, Transform>();
        BuildingSlot[] slots = FindObjectsByType<BuildingSlot>();
        WaveDefinition definition = waveController.CurrentWaveDefinition;
        if (definition == null || definition.Demands == null) return resolved;

        foreach (NeedDemand demand in definition.Demands)
        {
            if (demand.units <= 0 || !waveController.IsNeedUnlocked(demand.need)) continue;
            string expectedKey = BuildingKeyFor(demand.need);
            foreach (BuildingSlot slot in slots)
            {
                if (slot == null) continue;
                if (MatchesBuilding(slot.BuildingKey, expectedKey) || MatchesBuilding(slot.gameObject.name, expectedKey))
                {
                    resolved[demand.need] = slot.transform;
                    break;
                }
            }
            if (!resolved.ContainsKey(demand.need))
            {
                Debug.LogWarning($"GuestSpawner: no destination building is assigned for unlocked service {demand.need}.", this);
            }
        }
        return resolved;
    }

    private static bool MatchesBuilding(string candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        string normalized = new string(candidate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return normalized.StartsWith(expected, StringComparison.Ordinal);
    }

    private static string BuildingKeyFor(ResortNeed need)
    {
        switch (need)
        {
            case ResortNeed.Rooms: return "RECEPTION";
            case ResortNeed.Drinks: return "BEACHBAR";
            case ResortNeed.Food: return "RESTAURANT";
            case ResortNeed.Relax: return "POOL";
            case ResortNeed.OceanView: return "BUNGALOWS";
            default: throw new ArgumentOutOfRangeException(nameof(need), need, null);
        }
    }

    private void HandleWaveCompleted()
    {
        returningGuests = true;
        foreach (GuestMovement guest in spawnedGuests)
            if (guest != null)
                guest.ReturnToEntrance();
    }

    private void HandleGuestReturned()
    {
        waveController?.GuestLeftResort();
    }

    private void HandleGuestStartedWalking()
    {
        waveController?.GuestArrived();
    }

    private void ClearSpawnedGuests()
    {
        foreach (GuestMovement guest in spawnedGuests)
            if (guest != null)
                Destroy(guest.gameObject);
        spawnedGuests.Clear();
        guestsByIndex = Array.Empty<GuestMovement>();
        pendingVisits.Clear();
        routedNeedUnits.Clear();
        needDestinations.Clear();
        targetGuestCount = 0;
        nextGuestIndex = 0;
        nextVisualRecipient = 0;
        returningGuests = false;
    }
}
