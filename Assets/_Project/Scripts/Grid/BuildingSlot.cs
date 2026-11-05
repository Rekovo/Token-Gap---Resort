using UnityEngine;

public class BuildingSlot : MonoBehaviour
{
    [SerializeField] GridMap grid;
    [SerializeField] Vector2Int footprint = new(2, 2);
    [Tooltip("Where the Worker stands (empty child transform)")]
    [SerializeField] Transform workerAnchor;
    [Header("Optional visual replacement")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform replacementVisualPrefab;
    [SerializeField] private Vector3 replacementVisualLocalPosition;
    [SerializeField] private Vector3 replacementVisualLocalEulerAngles;
    [SerializeField] private Vector3 replacementVisualLocalScale = Vector3.one;
    [SerializeField, Tooltip("Stable key matching BUILDING_DEFINITION, e.g. RESTAURANT")]
    private string buildingKey;

    public Vector2Int Origin { get; private set; }
    public Transform WorkerAnchor => workerAnchor;
    public BuildingStateRecord PersistedState { get; private set; }
    public string BuildingKey => buildingKey;
    public bool IsUnlocked => PersistedState != null && PersistedState.is_unlocked == 1;
    public bool HasWorker => PersistedState != null && PersistedState.workers_assigned > 0;
    public int Level => PersistedState != null ? PersistedState.level : 0;
    private ResortDatabase database;

    private void Awake()
    {
        if (replacementVisualPrefab == null) return;
        Transform parent = visualRoot != null ? visualRoot : transform;
        foreach (Renderer renderer in parent.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
        Transform visual = Instantiate(replacementVisualPrefab, parent, false);
        visual.name = "BuildingVisual";
        visual.localPosition += replacementVisualLocalPosition;
        visual.localRotation *= Quaternion.Euler(replacementVisualLocalEulerAngles);
        visual.localScale = Vector3.Scale(visual.localScale, replacementVisualLocalScale);
    }

    void Start()
    {
        if (!string.IsNullOrWhiteSpace(buildingKey))
        {
            database = new ResortDatabase();
            database.CreateNewPlayer("test-player-001");
            RefreshPersistedState();
            if (PersistedState == null)
                Debug.LogWarning($"{name}: no saved building state found for key '{buildingKey}'.", this);
        }

        if (grid == null || grid.Data == null)
        {
            Debug.LogError($"{name}: assign a GridMap before starting.", this);
            return;
        }
        Vector3 bottomLeftCentre = transform.position
            - new Vector3(footprint.x - 1, 0, footprint.y - 1) * 0.5f * grid.CellSize;
        Origin = grid.WorldToCell(bottomLeftCentre);

        if (!grid.Data.TryPlace(this, Origin, footprint))
            Debug.LogError($"{name}: footprint overlaps another building or leaves the grid.", this);
    }

    public bool TryMoveWorkerTo(BuildingSlot target)
    {
        if (target == null || database == null || target.database == null || !IsUnlocked || target.HasWorker)
            return false;
        if (!database.MoveWorker("test-player-001", buildingKey, target.buildingKey)) return false;
        RefreshPersistedState();
        target.RefreshPersistedState();
        return true;
    }

    public void RefreshPersistedState()
    {
        if (database == null || string.IsNullOrWhiteSpace(buildingKey)) return;
        PersistedState = database.GetBuildingState("test-player-001", buildingKey);
    }

    void OnDestroy()
    {
        if (grid != null && grid.Data != null) grid.Data.Remove(this);
        database?.Dispose();
    }

    [ContextMenu("Snap to grid")]
    void SnapToGrid()
    {
        Vector3 local = transform.position - grid.transform.position;
        float cs = grid.CellSize;

        Vector3 corner = local - new Vector3(footprint.x, 0, footprint.y) * 0.5f * cs;
        corner.x = Mathf.Round(corner.x / cs) * cs;
        corner.z = Mathf.Round(corner.z / cs) * cs;
        transform.position = grid.transform.position + corner
            + new Vector3(footprint.x, 0, footprint.y) * 0.5f * cs
            + Vector3.up * (transform.position.y - grid.transform.position.y);
    }
}
