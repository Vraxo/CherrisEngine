# Navigation (`NavMesh` & `NavMeshAgent`)

Overcore includes a built-in Recast/Detour-inspired navigation mesh baker, spatial query engine, dynamic obstacle carver, and autonomous crowd-steering agent.

- **Query & bake types** live in `Overcore.Navigation` (`NavMesh`, `NavMeshBuilder`, `NavMeshData`, `NavMeshPath`, `NavMeshHit`, `NavMeshQueryFilter`, `NavMeshBuildSettings`, `NavMeshTriangulation`).
- **Scene components** live in `Overcore.Components` (`NavMeshSurface`, `NavMeshAgent`, `NavMeshObstacle`) alongside `Collider`, `RigidBody`, and `Animator`.

## Baking a `NavMeshSurface`

Attach `NavMeshSurface` to any `GameObject` (for example your floor or level root). By default `BakeOnAwake` is `true`, or you can click **Bake NavMesh** in the Inspector / call `surface.BuildNavMesh()` from code:

1. **Geometry collection**: Reads triangles from `MeshRenderer`s, `Collider`s (`BoxCollider`, `SphereCollider`, `CapsuleCollider`), or `Both`, filtered by `CollectObjects` (`All`, `Children`, `Volume`) and `LayerMask`.
2. **Heightfield voxelization & filtering**: Rasterizes solid spans at `CellSize` / `CellHeight`, filters walkable top surfaces by `AgentSlope` and `AgentHeight` headroom, carves active `NavMeshObstacle` volumes, and erodes boundaries by `AgentRadius`.
3. **Convex region merging & portal graph**: Merges open spans across `AgentClimb` step heights into convex `NavMeshPolygon`s with T-junction-free shared portal edges.

## Pathfinding & Spatial Queries (`NavMesh`)

```csharp
using System.Numerics;
using Overcore.Navigation;

NavMeshPath path = new();
if (NavMesh.CalculatePath(startPos, targetPos, NavMesh.AllAreas, path)
    && path.Status == NavMeshPathStatus.PathComplete)
{
    foreach (Vector3 corner in path.CornerList)
    {
        // A* over convex polygons + Simple Stupid Funnel 3D string-pulling
    }
}

if (NavMesh.SamplePosition(queryPoint, out NavMeshHit nearest, maxDistance: 2.0f))
{
    Vector3 onMesh = nearest.Position;
}

bool blocked = NavMesh.Raycast(origin, goal, out NavMeshHit wallHit);
bool hasBoundary = NavMesh.FindClosestEdge(origin, out NavMeshHit borderHit);
```

Custom area masks and per-area traversal cost multipliers (0..31) are supported via `NavMeshQueryFilter`.

## `NavMeshAgent` & `NavMeshObstacle`

- **`NavMeshAgent`**: Configures `Speed`, `AngularSpeed`, `Acceleration`, `StoppingDistance`, `AutoBraking`, `AutoRepath`, `Radius`, `Height`, `BaseOffset`, `ObstacleAvoidanceType`, and `AvoidancePriority` (`0` highest .. `99` lowest). Call `agent.SetDestination(worldTarget)`, `agent.Warp(pos)`, `agent.Move(offset)`, or `agent.ResetPath()`. Agents steer smoothly along string-pulled corners, slide along boundary edges, replan automatically when the `NavMesh` version changes, and avoid neighboring agents via priority-weighted reciprocal velocity separation.
- **`NavMeshObstacle`**: Supports `Box` and `Capsule` shapes. When `Carving = true`, moving past `CarvingMoveThreshold` (after `TimeToStationary`) automatically re-carves active `NavMeshSurface`s in the scene; when `Carving = false`, agents steer around the obstacle locally.
- **Viewport Gizmos**: Selecting a `NavMeshSurface` renders its walkable polygons and boundary edges; selecting a `NavMeshAgent` renders its capsule and active corner path; selecting a `NavMeshObstacle` renders its carve volume.
