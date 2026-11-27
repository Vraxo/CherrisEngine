using Overcore.Attributes;
using Overcore.Core;
using Overcore.Navigation;
using Overcore.Utils;
using System.Numerics;

namespace Overcore.Components;

/// <summary>
/// Declarative navigation mesh baker component — Unity's AI Navigation <c>NavMeshSurface</c>.
/// Gathers render meshes and/or physics colliders in the scene, carves active <see cref="NavMeshObstacle"/>s,
/// builds a <see cref="NavMeshData"/> graph, and registers it with <see cref="NavMesh"/>.
/// </summary>
public class NavMeshSurface : Component
{
    private NavMeshDataInstance _instance;

    public int AgentTypeID { get; set; } = 0;

    [Range(0.05f, 10f, 0.05f)]
    public float AgentRadius { get; set; } = 0.5f;

    [Range(0.2f, 20f, 0.1f)]
    public float AgentHeight { get; set; } = 2.0f;

    [Range(0f, 89f, 1f)]
    public float AgentSlope { get; set; } = 45.0f;

    [Range(0f, 10f, 0.05f)]
    public float AgentClimb { get; set; } = 0.4f;

    [Range(0.05f, 5f, 0.05f)]
    public float CellSize { get; set; } = 0.25f;

    [Range(0.02f, 2f, 0.02f)]
    public float CellHeight { get; set; } = 0.1f;

    public NavMeshCollectObjects CollectObjects { get; set; } = NavMeshCollectObjects.All;

    public NavMeshCollectGeometry UseGeometry { get; set; } = NavMeshCollectGeometry.Both;

    public LayerMask LayerMask { get; set; } = LayerMask.Everything;

    [Range(0f, 31f, 1f)]
    public int DefaultArea { get; set; } = NavMesh.Walkable;

    /// <summary>Local centre of the bake volume when <see cref="CollectObjects"/> is <see cref="NavMeshCollectObjects.Volume"/>.</summary>
    public Vector3 VolumeCenter { get; set; } = Vector3.Zero;

    /// <summary>World-aligned size of the bake volume when <see cref="CollectObjects"/> is <see cref="NavMeshCollectObjects.Volume"/>.</summary>
    public Vector3 VolumeSize { get; set; } = new(20f, 10f, 20f);

    /// <summary>Automatically builds the NavMesh when the component wakes up in a running scene.</summary>
    public bool BakeOnAwake { get; set; } = true;

    /// <summary>The most recently baked <see cref="NavMeshData"/>, or null before <see cref="BuildNavMesh"/> has run.</summary>
    [HideInInspector]
    public NavMeshData? NavMeshData { get; private set; }

    public NavMeshBuildSettings GetBuildSettings() => new()
    {
        AgentTypeID = AgentTypeID,
        AgentRadius = MathF.Max(0.01f, AgentRadius),
        AgentHeight = MathF.Max(0.1f, AgentHeight),
        AgentSlope = Math.Clamp(AgentSlope, 0f, 89f),
        AgentClimb = MathF.Max(0f, AgentClimb),
        CellSize = MathF.Max(0.05f, CellSize),
        CellHeight = MathF.Max(0.02f, CellHeight),
        MinRegionArea = 1
    };

    public override void Awake()
    {
        if (BakeOnAwake && Enabled)
        {
            BuildNavMesh();
        }
    }

    public override void OnEnable()
    {
        if (NavMeshData is not null && !_instance.Valid)
        {
            _instance = NavMesh.AddNavMeshData(NavMeshData);
        }
    }

    public override void OnDisable()
    {
        RemoveData();
    }

    public override void OnDestroy()
    {
        RemoveData();
    }

    /// <summary>Bakes the navigation mesh from the owning scene (or provided objects) and registers it with <see cref="NavMesh"/>.</summary>
    public NavMeshData BuildNavMesh(IEnumerable<GameObject>? explicitObjects = null)
    {
        IEnumerable<GameObject> sourceObjects = explicitObjects ?? GatherCandidateObjects();
        List<GameObject> materialized = [.. sourceObjects];

        BoundingBox? clipVolume = null;
        if (CollectObjects == NavMeshCollectObjects.Volume && GameObject is not null)
        {
            Vector3 worldCenter = Transform.WorldPosition + VolumeCenter;
            Vector3 half = Vector3.Abs(VolumeSize) * 0.5f;
            clipVolume = new BoundingBox
            {
                Min = worldCenter - half,
                Max = worldCenter + half
            };
        }

        // Gather active carving obstacles in the scene.
        IEnumerable<GameObject> obstaclePool = GameObject?.scene?.GameObjects
            ?? Scene.Active?.GameObjects
            ?? materialized;

        List<NavMeshCarveVolume> carves = [];
        foreach (GameObject go in obstaclePool)
        {
            if (!go.ActiveInHierarchy)
            {
                continue;
            }

            foreach (NavMeshObstacle obstacle in go.GetComponents<NavMeshObstacle>())
            {
                if (obstacle.Enabled && obstacle.Carving)
                {
                    carves.Add(obstacle.GetWorldCarveVolume());
                    obstacle.MarkCarvedSnapshot();
                }
            }
        }

        NavMeshData built = NavMeshBuilder.BuildFromGameObjects(
            materialized,
            GetBuildSettings(),
            UseGeometry,
            LayerMask,
            Math.Clamp(DefaultArea, 0, 31),
            clipVolume,
            carves);

        NavMeshData = built;
        if (Enabled)
        {
            _instance = NavMesh.ReplaceNavMeshData(_instance, built);
        }

        return built;
    }

    /// <summary>Unregisters this surface's <see cref="NavMeshData"/> from <see cref="NavMesh"/>.</summary>
    public void RemoveData()
    {
        if (_instance.Valid)
        {
            NavMesh.RemoveNavMeshData(_instance);
            _instance = default;
        }
    }

    private IEnumerable<GameObject> GatherCandidateObjects()
    {
        if (CollectObjects == NavMeshCollectObjects.Children && GameObject is not null)
        {
            List<GameObject> subtree = [];
            CollectHierarchy(GameObject, subtree);
            return subtree;
        }

        if (GameObject?.scene is { } ownScene)
        {
            return ownScene.GameObjects;
        }

        if (Scene.Active is { } activeScene)
        {
            return activeScene.GameObjects;
        }

        return GameObject is not null ? [GameObject] : [];
    }

    private static void CollectHierarchy(GameObject root, List<GameObject> into)
    {
        into.Add(root);
        foreach (Transform child in root.Transform.Children)
        {
            CollectHierarchy(child.GameObject, into);
        }
    }

    internal override void OnInstantiated()
    {
        base.OnInstantiated();
        _instance = default;
        NavMeshData = null;
    }
}
