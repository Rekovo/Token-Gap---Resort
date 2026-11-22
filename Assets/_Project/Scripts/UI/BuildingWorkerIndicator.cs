
using UnityEngine;

// SCRUM-20: Shows whether a building has a Worker.
// This script only controls visuals.
[DisallowMultipleComponent]
[RequireComponent(typeof(BuildingSlot))]
public class BuildingWorkerIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BuildingSlot building;
    [SerializeField] private GameObject staffedMarker;
    [SerializeField] private GameObject unstaffedMarker;

    private bool? lastUnlocked;
    private bool? lastStaffed;

    private void Awake()
    {
        if (building == null)
        {
            building = GetComponent<BuildingSlot>();
        }
    }

    private void LateUpdate()
    {
        bool unlocked = building != null && building.IsUnlocked;
        bool staffed = building != null && building.HasWorker;

        // No need to update the visuals if nothing changed.
        if (lastUnlocked == unlocked && lastStaffed == staffed)
        {
            return;
        }

        lastUnlocked = unlocked;
        lastStaffed = staffed;

        ShowState(unlocked, staffed);
    }

    // Can be called after loading or changing saved assignments.
    public void RefreshFromSave()
    {
        if (building == null) return;

        building.RefreshPersistedState();

        lastUnlocked = null;
        lastStaffed = null;
    }

    private void ShowState(bool unlocked, bool staffed)
    {
        if (staffedMarker != null)
        {
            staffedMarker.SetActive(unlocked && staffed);
        }

        if (unstaffedMarker != null)
        {
            unstaffedMarker.SetActive(unlocked && !staffed);
        }
    }
}
