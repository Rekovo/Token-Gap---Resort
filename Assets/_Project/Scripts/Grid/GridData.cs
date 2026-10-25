using System.Collections.Generic;
using UnityEngine;

public sealed class GridData
{
    public Vector2Int Size { get; }
    public float CellSize { get; }

    readonly Dictionary<Vector2Int, object> occupants = new();

    public GridData(Vector2Int size, float cellSize)
    {
        Size = size;
        CellSize = cellSize;
    }

    public bool InBounds(Vector2Int c) =>
        c.x >= 0 && c.y >= 0 && c.x < Size.x && c.y < Size.y;

    public Vector2Int LocalToCell(Vector3 localPos) =>
        new(Mathf.FloorToInt(localPos.x / CellSize), Mathf.FloorToInt(localPos.z / CellSize));

    public Vector3 CellToLocalCenter(Vector2Int c) =>
        new((c.x + 0.5f) * CellSize, 0f, (c.y + 0.5f) * CellSize);

    public bool CanPlace(Vector2Int origin, Vector2Int footprint)
    {
        for (int x = 0; x < footprint.x; x++)
            for (int y = 0; y < footprint.y; y++)
            {
                var c = origin + new Vector2Int(x, y);
                if (!InBounds(c) || occupants.ContainsKey(c)) return false;
            }
        return true;
    }

    public bool TryPlace(object owner, Vector2Int origin, Vector2Int footprint)
    {
        if (!CanPlace(origin, footprint)) return false;
        for (int x = 0; x < footprint.x; x++)
            for (int y = 0; y < footprint.y; y++)
                occupants[origin + new Vector2Int(x, y)] = owner;
        return true;
    }

    public void Remove(object owner)
    {
        var toRemove = new List<Vector2Int>();
        foreach (var kv in occupants)
            if (ReferenceEquals(kv.Value, owner)) toRemove.Add(kv.Key);
        foreach (var c in toRemove) occupants.Remove(c);
    }

    public bool TryGetOccupant<T>(Vector2Int c, out T owner) where T : class
    {
        if (occupants.TryGetValue(c, out var o) && o is T t) { owner = t; return true; }
        owner = null;
        return false;
    }
}