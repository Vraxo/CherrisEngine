using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using Overcore.Navigation;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Navigation;

/// <summary>
/// Covers runtime <see cref="NavMesh"/> baking (<see cref="NavMeshSurface"/> / <see cref="NavMeshBuilder"/>),
/// spatial queries (<see cref="NavMesh.SamplePosition(Vector3, out NavMeshHit, float, int)"/>,
/// <see cref="NavMesh.Raycast(Vector3, Vector3, out NavMeshHit, int)"/>,
/// <see cref="NavMesh.FindClosestEdge(Vector3, out NavMeshHit, int)"/>), A* + Funnel pathfinding around obstacles,
/// dynamic <see cref="NavMeshObstacle"/> carving, and <see cref="NavMeshAgent"/> steering &amp; crowd avoidance.
/// </summary>
public sealed class NavigationTests : IDisposable
{
    public NavigationTests() => NavMesh.RemoveAllNavMeshData();

    public void Dispose() => NavMesh.RemoveAllNavMeshData();

    private static GameObject CreateGroundPlane(string name = "Ground", float sizeX = 20f, float sizeZ = 20f, float y = 0f)
    {
        GameObject ground = new(name);
        ground.Transform.WorldPosition = new Vector3(0f, y - 0.5f, 0f);
        ground.AddComponent(new BoxCollider { Size = new Vector3(sizeX, 1f, sizeZ) });
        return ground;
    }

    [Fact]
    public void BuildNavMesh_OnFlatFloor_ProducesWalkableSurfaceAndAnswersQueries()
    {
        GameObject ground = CreateGroundPlane(sizeX: 16f, sizeZ: 16f);
        NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
        surface.AgentRadius = 0.4f;
        surface.CellSize = 0.25f;

        NavMeshData data = surface.BuildNavMesh([ground]);
        data.IsEmpty.Should().BeFalse();
        data.Polygons.Should().NotBeEmpty();

        NavMeshTriangulation tris = NavMesh.CalculateTriangulation();
        tris.Vertices.Should().NotBeEmpty();
        (tris.Indices.Length % 3).Should().Be(0);

        NavMesh.SamplePosition(new Vector3(0f, 0.2f, 0f), out NavMeshHit sample, 2.0f).Should().BeTrue();
        sample.Position.Y.Should().BeApproximately(0f, 0.2f);

        // Straight line across unobstructed center should NOT hit any boundary.
        NavMesh.Raycast(new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f), out NavMeshHit rayHit).Should().BeFalse();
        rayHit.Hit.Should().BeFalse();

        // Raycast past the floor edge MUST hit the eroded boundary.
        NavMesh.Raycast(new Vector3(0f, 0f, 0f), new Vector3(15f, 0f, 0f), out NavMeshHit edgeRayHit).Should().BeTrue();
        edgeRayHit.Hit.Should().BeTrue();
        edgeRayHit.Position.X.Should().BeGreaterThan(6f).And.BeLessThan(8.5f);

        NavMesh.FindClosestEdge(new Vector3(0f, 0f, 0f), out NavMeshHit closestEdge).Should().BeTrue();
        closestEdge.Distance.Should().BeGreaterThan(6f);
    }

    [Fact]
    public void CalculatePath_RoutesAroundCentralWallWithFunnelStringPulling()
    {
        GameObject ground = CreateGroundPlane(sizeX: 20f, sizeZ: 20f);
        GameObject wall = new("CenterWall");
        wall.Transform.WorldPosition = new Vector3(0f, 1.0f, 0f);
        wall.AddComponent(new BoxCollider { Size = new Vector3(1.5f, 2.5f, 8.0f) });

        NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
        surface.AgentRadius = 0.4f;
        surface.CellSize = 0.25f;
        surface.BuildNavMesh([ground, wall]);

        Vector3 start = new(-5f, 0f, 0f);
        Vector3 goal = new(5f, 0f, 0f);

        // Direct line through X=0, Z=0 is blocked by the wall.
        NavMesh.Raycast(start, goal, out NavMeshHit blocked).Should().BeTrue();
        blocked.Position.X.Should().BeLessThan(0f);

        NavMeshPath path = new();
        NavMesh.CalculatePath(start, goal, NavMesh.AllAreas, path).Should().BeTrue();
        path.Status.Should().Be(NavMeshPathStatus.PathComplete);
        path.CornerList.Count.Should().BeGreaterThanOrEqualTo(3, "path bends around the wall's end rather than passing straight through");

        // At least one intermediate corner must swing clear around Z > 3.8 or Z < -3.8.
        path.CornerList.Skip(1).SkipLast(1)
            .Any(c => MathF.Abs(c.Z) > 3.8f)
            .Should().BeTrue("funnel string-pulling hugs the outer corner of the wall");
    }

    [Fact]
    public void CarvingNavMeshObstacle_CutsImpassableHoleAndRebuildsWhenMoved()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = CreateGroundPlane(sizeX: 18f, sizeZ: 18f);
        NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
        surface.AgentRadius = 0.35f;
        surface.CellSize = 0.25f;

        GameObject crate = new("CarvingCrate");
        crate.Transform.WorldPosition = new Vector3(0f, 0.5f, 0f);
        NavMeshObstacle obstacle = crate.AddComponent<NavMeshObstacle>();
        obstacle.Carving = true;
        obstacle.Size = new Vector3(3.0f, 2.0f, 3.0f);
        obstacle.CarvingMoveThreshold = 0.2f;

        Scene scene = new(string.Empty, [ground, crate]);
        scene.Start(physics, audio);

        // Center (0,0,0) is carved out, so raycast from (-4,0,0) to (4,0,0) hits the carved hole.
        NavMesh.Raycast(new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f), out _).Should().BeTrue();

        // Move the obstacle out of the way and tick Scene.Update so dynamic re-carve runs.
        crate.Transform.WorldPosition = new Vector3(0f, 0.5f, 6f);
        scene.Update(0.05f);

        // Now the center corridor is open again, and Z=6 is carved instead.
        NavMesh.Raycast(new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f), out _).Should().BeFalse();
        NavMesh.Raycast(new Vector3(-4f, 0f, 6f), new Vector3(4f, 0f, 6f), out _).Should().BeTrue();
    }

    [Fact]
    public void NavMeshAgent_SteersToDestinationAndBrakesAtStoppingDistance()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = CreateGroundPlane(sizeX: 20f, sizeZ: 20f);
        NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
        surface.CellSize = 0.25f;

        GameObject runner = new("Runner");
        runner.Transform.WorldPosition = new Vector3(-5f, 0f, 0f);
        NavMeshAgent agent = runner.AddComponent<NavMeshAgent>();
        agent.Speed = 5.0f;
        agent.Acceleration = 20.0f;
        agent.StoppingDistance = 0.2f;

        Scene scene = new(string.Empty, [ground, runner]);
        scene.Start(physics, audio);

        Vector3 target = new(4f, 0f, 0f);
        agent.SetDestination(target).Should().BeTrue();
        agent.HasPath.Should().BeTrue();

        for (int i = 0; i < 180; i++)
        {
            scene.Update(1f / 60f);
        }

        Vector3.Distance(runner.Transform.WorldPosition, target).Should().BeLessThan(0.35f);
        agent.RemainingDistance.Should().BeLessThanOrEqualTo(0.25f);
    }

    [Fact]
    public void CrowdAvoidance_AllowsHeadOnAgentsToPassWithoutDeadlock()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = CreateGroundPlane(sizeX: 20f, sizeZ: 20f);
        ground.AddComponent<NavMeshSurface>().CellSize = 0.25f;

        GameObject left = new("AgentLeft");
        left.Transform.WorldPosition = new Vector3(-4f, 0f, 0f);
        NavMeshAgent agentA = left.AddComponent<NavMeshAgent>();
        agentA.Speed = 4.0f;
        agentA.Acceleration = 16.0f;
        agentA.Radius = 0.5f;

        GameObject right = new("AgentRight");
        right.Transform.WorldPosition = new Vector3(4f, 0f, 0f);
        NavMeshAgent agentB = right.AddComponent<NavMeshAgent>();
        agentB.Speed = 4.0f;
        agentB.Acceleration = 16.0f;
        agentB.Radius = 0.5f;

        Scene scene = new(string.Empty, [ground, left, right]);
        scene.Start(physics, audio);

        agentA.SetDestination(new Vector3(4f, 0f, 0f));
        agentB.SetDestination(new Vector3(-4f, 0f, 0f));

        float maxLateralDeviation = 0f;
        for (int i = 0; i < 210; i++)
        {
            scene.Update(1f / 60f);
            maxLateralDeviation = MathF.Max(maxLateralDeviation, MathF.Abs(left.Transform.WorldPosition.Z));
        }

        maxLateralDeviation.Should().BeGreaterThan(0.15f, "agents steer laterally to avoid colliding head-on");
        Vector3.Distance(left.Transform.WorldPosition, new Vector3(4f, 0f, 0f)).Should().BeLessThan(0.6f);
        Vector3.Distance(right.Transform.WorldPosition, new Vector3(-4f, 0f, 0f)).Should().BeLessThan(0.6f);
    }
}
