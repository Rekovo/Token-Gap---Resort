using UnityEngine;

public class BuildingSlot : MonoBehaviour
{
    [SerializeField] GridMap grid;
    [SerializeField] Vector2Int footprint = new(2, 2);
    [Tooltip("Where the Worker stands (empty child transform)")]
    [SerializeField] Transform workerAnchor;

    public Vector2Int Origin { get; private set; }
    public Transform WorkerAnchor => workerAnchor;

    void Start()
    {
        Vector3 bottomLeftCentre = transform.position
            - new Vector3(footprint.x - 1, 0, footprint.y - 1) * 0.5f * grid.CellSize;
        Origin = grid.WorldToCell(bottomLeftCentre);

        if (!grid.Data.TryPlace(this, Origin, footprint))
            Debug.LogError($"{name}: footprint overlaps another building or leaves the grid.", this);
    }

    void OnDestroy()
    {
        if (grid != null && grid.Data != null) grid.Data.Remove(this);
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