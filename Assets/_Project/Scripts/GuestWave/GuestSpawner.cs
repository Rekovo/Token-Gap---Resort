using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public sealed class GuestSpawner : MonoBehaviour
{
    [Serializable]
    private struct NeedDestination
    {
        public ResortNeed need;
        public Transform destination;
    }

    private sealed class ScheduledVisit
    {
        public ResortNeed Need;
        public int UnitIndex;
        public float ArrivalTime;
        public Vector3 Position;
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
    private List<ScheduledVisit>[] visitsByGuest = Array.Empty<List<ScheduledVisit>>();
    private int[] nextVisitByGuest = Array.Empty<int>();
    private int targetGuestCount;
    private int nextGuestIndex;
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
    }

    private void OnDisable()
    {
        if (waveController != null)
        {
            waveController.OnWaveStarted -= HandleWaveStarted;
            waveController.OnWaveCompleted -= HandleWaveCompleted;
        }
        ClearSpawnedGuests();
    }

    private void Update()
    {
        if (waveController == null || returningGuests || waveController.CurrentState != GuestWaveState.Active)
            return;

        SpawnIncomingGuest();
        DispatchDueVisits();
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
        if (targetGuestCount == 0)
            return;

        needDestinations = ResolveNeedDestinations();
        BuildVisitSchedule(targetGuestCount);
        if (visitsByGuest.Length == 0)
        {
            Debug.LogError("GuestSpawner: no wave demand has a matching building destination.", this);
            targetGuestCount = 0;
            return;
        }

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

    private void DispatchDueVisits()
    {
        float elapsed = waveController.Elapsed;
        int availableGuests = Mathf.Min(nextGuestIndex, visitsByGuest.Length);
        for (int guestIndex = 0; guestIndex < availableGuests; guestIndex++)
        {
            GuestMovement guest = guestsByIndex[guestIndex];
            if (guest == null)
                continue;

            List<ScheduledVisit> schedule = visitsByGuest[guestIndex];
            while (nextVisitByGuest[guestIndex] < schedule.Count)
            {
                ScheduledVisit visit = schedule[nextVisitByGuest[guestIndex]];
                if (visit.ArrivalTime > elapsed)
                    break;

                guest.QueueVisit(visit.Need, visit.UnitIndex, visit.Position);
                nextVisitByGuest[guestIndex]++;
            }
        }
    }

    private void BuildVisitSchedule(int guestCount)
    {
        visitsByGuest = new List<ScheduledVisit>[guestCount];
        guestsByIndex = new GuestMovement[guestCount];
        nextVisitByGuest = new int[guestCount];
        for (int i = 0; i < guestCount; i++)
            visitsByGuest[i] = new List<ScheduledVisit>();

        WaveDefinition definition = waveController.CurrentWaveDefinition;
        if (definition == null || definition.Demands == null)
            return;

        float arrivalWindow = waveController.NeedArrivalWindow;
        int assignmentIndex = 0;
        foreach (NeedDemand demand in definition.Demands)
        {
            if (demand.units <= 0)
                continue;
            if (!needDestinations.TryGetValue(demand.need, out Transform building) || building == null)
            {
                Debug.LogWarning($"GuestSpawner: no building destination found for {demand.need}; those visits are not shown.", this);
                continue;
            }

            for (int unit = 0; unit < demand.units; unit++)
            {
                int guestIndex = assignmentIndex++ % guestCount;
                int guestSlot = guestIndex % 8;
                float angle = (guestSlot * 45f + unit * 17f) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (0.45f + (guestSlot / 4) * 0.3f);
                visitsByGuest[guestIndex].Add(new ScheduledVisit
                {
                    Need = demand.need,
                    UnitIndex = unit,
                    ArrivalTime = arrivalWindow * unit / demand.units,
                    Position = building.position + offset
                });
            }
        }

        foreach (List<ScheduledVisit> schedule in visitsByGuest)
            schedule.Sort((a, b) => a.ArrivalTime.CompareTo(b.ArrivalTime));
    }

    private Dictionary<ResortNeed, Transform> ResolveNeedDestinations()
    {
        Dictionary<ResortNeed, Transform> resolved = new Dictionary<ResortNeed, Transform>();

        BuildingSlot[] slots = FindObjectsByType<BuildingSlot>();
        WaveDefinition definition = waveController.CurrentWaveDefinition;
        if (definition != null && definition.Demands != null)
        {
            foreach (NeedDemand demand in definition.Demands)
            {
                if (demand.units <= 0)
                    continue;

                string expectedKey = BuildingKeyFor(demand.need);
                foreach (BuildingSlot slot in slots)
                {
                    if (slot == null)
                        continue;
                    if (MatchesBuilding(slot.BuildingKey, expectedKey) || MatchesBuilding(slot.gameObject.name, expectedKey))
                    {
                        resolved[demand.need] = slot.transform;
                        break;
                    }
                }

                if (!resolved.ContainsKey(demand.need))
                    Debug.LogError($"GuestSpawner: no destination building is assigned for {demand.need}.", this);
            }
        }
        return resolved;
    }

    private static bool MatchesBuilding(string candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;
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
        visitsByGuest = Array.Empty<List<ScheduledVisit>>();
        nextVisitByGuest = Array.Empty<int>();
        targetGuestCount = 0;
        nextGuestIndex = 0;
        returningGuests = false;
    }
}
