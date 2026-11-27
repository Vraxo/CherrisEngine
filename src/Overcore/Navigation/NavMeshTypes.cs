using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Navigation;

/// <summary>Result status of a <see cref="NavMesh.CalculatePath"/> query.</summary>
public enum NavMeshPathStatus
{
    /// <summary>The path reaches the requested target position on the NavMesh.</summary>
    PathComplete,

    /// <summary>The destination could not be reached, so the path stops at the closest reachable polygon.</summary>
    PathPartial,

    /// <summary>Either the start or the target could not be mapped to any valid NavMesh polygon.</summary>
    PathInvalid
}

/// <summary>How scene geometry is gathered when baking a <see cref="NavMeshSurface"/>.</summary>
public enum NavMeshCollectGeometry
{
    /// <summary>Collects triangles from enabled <see cref="MeshRenderer"/> components.</summary>
    RenderMeshes,

    /// <summary>Collects tessellated surfaces from enabled <see cref="Collider"/> components.</summary>
    PhysicsColliders,

    /// <summary>Collects both <see cref="MeshRenderer"/> meshes and <see cref="Collider"/> shapes.</summary>
    Both
}

/// <summary>Which GameObjects a <see cref="NavMeshSurface"/> includes when baking.</summary>
public enum NavMeshCollectObjects
{
    /// <summary>Every active object in the scene matching the surface's layer mask.</summary>
    All,

    /// <summary>Only the surface's own GameObject and its hierarchy descendants.</summary>
    Children,

    /// <summary>Active objects overlapping the surface's axis-aligned volume.</summary>
    Volume
}

/// <summary>Parameters controlling Recast-style voxelization, agent clearance, and polygon generation.</summary>
public record struct NavMeshBuildSettings
{
    public int AgentTypeID { get; set; }
    public float AgentRadius { get; set; }
    public float AgentHeight { get; set; }
    public float AgentSlope { get; set; }
    public float AgentClimb { get; set; }
    public float CellSize { get; set; }
    public float CellHeight { get; set; }
    public int MinRegionArea { get; set; }

    public NavMeshBuildSettings()
    {
        AgentTypeID = 0;
        AgentRadius = 0.5f;
        AgentHeight = 2.0f;
        AgentSlope = 45.0f;
        AgentClimb = 0.4f;
        CellSize = 0.25f;
        CellHeight = 0.1f;
        MinRegionArea = 1;
    }

    public static NavMeshBuildSettings Default => new();
}

/// <summary>Area mask and per-area traversal cost multipliers used by <see cref="NavMesh"/> queries.</summary>
public sealed class NavMeshQueryFilter
{
    private readonly float[] _areaCosts = CreateDefaultCosts();

    public int AgentTypeID { get; set; }
    public int AreaMask { get; set; } = NavMesh.AllAreas;

    public float GetAreaCost(int areaIndex) =>
        areaIndex is >= 0 and < 32 ? _areaCosts[areaIndex] : 1f;

    public void SetAreaCost(int areaIndex, float cost)
    {
        if (areaIndex is < 0 or >= 32)
        {
            throw new ArgumentOutOfRangeException(nameof(areaIndex), "Area index must be in [0, 31].");
        }

        _areaCosts[areaIndex] = MathF.Max(0.01f, cost);
    }

    internal static float[] CreateDefaultCosts()
    {
        float[] costs = new float[32];
        Array.Fill(costs, 1f);
        costs[NavMesh.NotWalkable] = float.PositiveInfinity;
        costs[NavMesh.Jump] = 2f;
        return costs;
    }
}

/// <summary>Result of a spatial query (<see cref="NavMesh.SamplePosition"/>, <see cref="NavMesh.Raycast"/>, <see cref="NavMesh.FindClosestEdge"/>).</summary>
public struct NavMeshHit
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public float Distance { get; set; }
    public int Mask { get; set; }
    public bool Hit { get; set; }
    public int PolygonIndex { get; set; }
}

/// <summary>Ordered corner waypoints produced by <see cref="NavMesh.CalculatePath"/>.</summary>
public sealed class NavMeshPath
{
    private readonly List<Vector3> _corners = [];

    public NavMeshPathStatus Status { get; internal set; } = NavMeshPathStatus.PathInvalid;

    public Vector3[] Corners => [.. _corners];

    public IReadOnlyList<Vector3> CornerList => _corners;

    public int GetCornersNonAlloc(Span<Vector3> results)
    {
        int count = Math.Min(results.Length, _corners.Count);
        for (int i = 0; i < count; i++)
        {
            results[i] = _corners[i];
        }

        return count;
    }

    public void ClearCorners()
    {
        _corners.Clear();
        Status = NavMeshPathStatus.PathInvalid;
    }

    internal void SetCorners(IEnumerable<Vector3> corners, NavMeshPathStatus status)
    {
        _corners.Clear();
        _corners.AddRange(corners);
        Status = status;
    }
}

/// <summary>Exported triangle soup of the baked navigation mesh (Unity's <c>NavMesh.CalculateTriangulation</c>).</summary>
public readonly record struct NavMeshTriangulation(Vector3[] Vertices, int[] Indices, int[] Areas);

/// <summary>One triangle fed into <see cref="NavMeshBuilder"/>.</summary>
public readonly record struct NavMeshBuildSourceTriangle(Vector3 A, Vector3 B, Vector3 C, int Area = NavMesh.Walkable);

/// <summary>Oriented box or vertical capsule obstacle volume used to carve impassable cells out of a NavMesh.</summary>
public readonly record struct NavMeshCarveVolume(
    bool IsCapsule,
    Vector3 Center,
    Quaternion Rotation,
    Vector3 HalfExtents,
    float Radius,
    float Height);

/// <summary>One convex polygon in a <see cref="NavMeshData"/> navigation graph.</summary>
public sealed class NavMeshPolygon
{
    /// <summary>Indices into <see cref="NavMeshData.Vertices"/> in clockwise order when viewed from +Y.</summary>
    public required int[] VertexIndices { get; init; }

    /// <summary>Neighbor polygon index across each edge <c>i -> (i + 1) % N</c>, or <c>-1</c> for boundary edges.</summary>
    public required int[] Neighbors { get; init; }

    /// <summary>Area index (<c>0..31</c>); bitmask is <c>1 &lt;&lt; Area</c>.</summary>
    public int Area { get; init; } = NavMesh.Walkable;

    public Vector3 Center { get; init; }
    public Vector3 Normal { get; init; } = Vector3.UnitY;
    public BoundingBox Bounds { get; init; }
}

/// <summary>Baked convex polygon navigation graph produced by <see cref="NavMeshBuilder"/>.</summary>
public sealed class NavMeshData
{
    public Vector3[] Vertices { get; }
    public IReadOnlyList<NavMeshPolygon> Polygons { get; }
    public BoundingBox Bounds { get; }
    public NavMeshBuildSettings BuildSettings { get; }

    public NavMeshData(
        Vector3[] vertices,
        IReadOnlyList<NavMeshPolygon> polygons,
        BoundingBox bounds,
        NavMeshBuildSettings settings)
    {
        Vertices = vertices ?? [];
        Polygons = polygons ?? [];
        Bounds = bounds;
        BuildSettings = settings;
    }

    public bool IsEmpty => Polygons.Count == 0 || Vertices.Length == 0;

    public NavMeshTriangulation ToTriangulation()
    {
        if (IsEmpty)
        {
            return new NavMeshTriangulation([], [], []);
        }

        List<int> indices = [];
        List<int> areas = [];

        foreach (NavMeshPolygon poly in Polygons)
        {
            int n = poly.VertexIndices.Length;
            if (n < 3)
            {
                continue;
            }

            int v0 = poly.VertexIndices[0];
            for (int i = 1; i < n - 1; i++)
            {
                indices.Add(v0);
                indices.Add(poly.VertexIndices[i]);
                indices.Add(poly.VertexIndices[i + 1]);
                areas.Add(poly.Area);
            }
        }

        return new NavMeshTriangulation([.. Vertices], [.. indices], [.. areas]);
    }
}
