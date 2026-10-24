using UnityEngine;

public class GridMap : MonoBehaviour
{
    [SerializeField] Vector2Int size = new(30, 30);
    [SerializeField, Min(0.1f)] float cellSize = 1f;

    public GridData Data { get; private set; }
    public float CellSize => cellSize;

    void Awake() => Data = new GridData(size, cellSize);

    public Vector2Int WorldToCell(Vector3 world) =>
        Data.LocalToCell(world - transform.position);

    public Vector3 CellToWorldCenter(Vector2Int cell) =>
        transform.position + Data.CellToLocalCenter(cell);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
        Vector3 o = transform.position;
        for (int x = 0; x <= size.x; x++)
            Gizmos.DrawLine(o + new Vector3(x * cellSize, 0, 0), o + new Vector3(x * cellSize, 0, size.y * cellSize));
        for (int y = 0; y <= size.y; y++)
            Gizmos.DrawLine(o + new Vector3(0, 0, y * cellSize), o + new Vector3(size.x * cellSize, 0, y * cellSize));
    }
}