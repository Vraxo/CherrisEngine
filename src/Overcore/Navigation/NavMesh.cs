using Overcore.Components;
using System.Numerics;

namespace Overcore.Navigation;

/// <summary>Handle returned by <see cref="NavMesh.AddNavMeshData"/> so a registered slice can be removed individually.</summary>
public readonly record struct NavMeshDataInstance(int Id)
{
    public bool Valid => Id > 0;
    public void Remove() => NavMesh.RemoveNavMeshData(this);
}

/// <summary>
/// Runtime navigation mesh registry and query engine — Unity's <c>NavMesh</c>:
/// A* polygon search with area masks/costs, Simple Stupid Funnel string-pulling, nearest-point projection
/// (<see cref="SamplePosition"/>), NavMesh boundary raycasting (<see cref="Raycast"/>), closest boundary edge
/// (<see cref="FindClosestEdge"/>), and triangulation export (<see cref="CalculateTriangulation"/>).
/// </summary>
public static partial class NavMesh
{
    public const int Walkable = 0;
    public const int NotWalkable = 1;
    public const int Jump = 2;
    public const int AllAreas = ~0;

    private sealed record RegisteredEntry(int Id, NavMeshData Data);

    private static readonly List<RegisteredEntry> _entries = [];
    private static int _nextInstanceId = 1;
    private static readonly NavMeshQueryFilter _defaultFilter = new();

    /// <summary>Version counter incremented whenever NavMesh data is added, updated, or cleared so agents know to replan.</summary>
    public static int Version { get; private set; }

    /// <summary>Currently active primary <see cref="NavMeshData"/>, or null when none has been baked/added.</summary>
    public static NavMeshData? ActiveData => _entries.Count > 0 ? _entries[^1].Data : null;

    /// <summary>Registers baked <see cref="NavMeshData"/> and returns a handle that can remove it later.</summary>
    public static NavMeshDataInstance AddNavMeshData(NavMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        int id = _nextInstanceId++;
        _entries.Add(new RegisteredEntry(id, data));
        Version++;
        return new NavMeshDataInstance(id);
    }

    /// <summary>Replaces an existing <see cref="NavMeshDataInstance"/> in place, or registers a new one if invalid.</summary>
    internal static NavMeshDataInstance ReplaceNavMeshData(NavMeshDataInstance existing, NavMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Id == existing.Id)
            {
                _entries[i] = new RegisteredEntry(existing.Id, data);
                Version++;
                return existing;
            }
        }

        return AddNavMeshData(data);
    }

    /// <summary>Removes a previously registered <see cref="NavMeshDataInstance"/>.</summary>
    public static void RemoveNavMeshData(NavMeshDataInstance handle)
    {
        if (!handle.Valid)
        {
            return;
        }

        int removed = _entries.RemoveAll(e => e.Id == handle.Id);
        if (removed > 0)
        {
            Version++;
        }
    }

    /// <summary>Removes all registered <see cref="NavMeshData"/> instances.</summary>
    public static void RemoveAllNavMeshData()
    {
        if (_entries.Count > 0)
        {
            _entries.Clear();
            Version++;
        }
    }

    /// <summary>Exports the combined triangulation of all registered <see cref="NavMeshData"/> slices.</summary>
    public static NavMeshTriangulation CalculateTriangulation()
    {
        if (_entries.Count == 0)
        {
            return new NavMeshTriangulation([], [], []);
        }

        if (_entries.Count == 1)
        {
            return _entries[0].Data.ToTriangulation();
        }

        List<Vector3> vertices = [];
        List<int> indices = [];
        List<int> areas = [];

        foreach (RegisteredEntry entry in _entries)
        {
            NavMeshTriangulation part = entry.Data.ToTriangulation();
            int baseVert = vertices.Count;
            vertices.AddRange(part.Vertices);
            foreach (int idx in part.Indices)
            {
                indices.Add(baseVert + idx);
            }

            areas.AddRange(part.Areas);
        }

        return new NavMeshTriangulation([.. vertices], [.. indices], [.. areas]);
    }
}
