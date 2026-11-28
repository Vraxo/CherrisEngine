using AwesomeAssertions;
using Overcore.Core;
using Overcore.Core.Physics;
using Overcore.Terrain;
using System.Numerics;
using Xunit;
using TerrainComponent = Overcore.Components.Terrain;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers tree trunks as solid things: the prism the physics mesh wears, the cylinder the analytic
/// queries test, and a body coming to rest on top of one.
/// </summary>
public class TerrainTrunkTests
{
    private static TerrainData Forest(float trunkRadius = 0.25f, float trunkHeight = 2f)
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));
        data.AddTreePrototype(new TerrainTreePrototype("Pine", "Cube", Vector3.One)
        {
            TrunkRadius = trunkRadius,
            TrunkHeight = trunkHeight
        });
        data.AddTree(0, new Vector2(0.5f, 0.5f));
        return data;
    }

    [Fact]
    public void TrunkTriangles_FormAnEightSidedPrismPerTree()
    {
        TerrainForestMesher.BuildTrunkTriangles(Forest()).Should().HaveCount(24,
            "eight sides of two triangles, and an eight-triangle cap");

        TerrainForestMesher.BuildTrunkTriangles(Forest(trunkRadius: 0f)).Should().BeEmpty(
            "a prototype with no trunk grows a ghost");

        TerrainTrunk trunk = TerrainForestMesher.EnumerateTrunks(Forest()).Single();
        trunk.Base.Should().Be(new Vector3(5f, -0.05f, 5f));
        trunk.Top.Y.Should().BeApproximately(1.95f, 1e-3f);
        trunk.Radius.Should().Be(0.25f);
    }

    [Fact]
    public void AnalyticRaycast_HitsTheTrunkTop_BesideItTheGround()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        terrain.TerrainData = Forest();
        ground.AddComponent<Overcore.Components.TerrainCollider>();

        Scene scene = new(string.Empty, [ground]);
        scene.Start(physics, audio);

        Physics.Raycast(new Vector3(5f, 10f, 5f), -Vector3.UnitY, out RaycastHit trunkHit)
            .Should().BeTrue();
        trunkHit.Point.Y.Should().BeApproximately(1.95f, 0.01f, "the ray lands on the trunk, not through it");

        Physics.Raycast(new Vector3(5f, 1f, 10f), -Vector3.UnitZ, out RaycastHit sideHit)
            .Should().BeTrue();
        sideHit.Point.Z.Should().BeApproximately(5.25f, 0.01f, "sideways, the bark is a quarter metre out");

        Physics.Raycast(new Vector3(1f, 10f, 1f), -Vector3.UnitY, out RaycastHit groundHit)
            .Should().BeTrue();
        groundHit.Point.Y.Should().BeApproximately(0f, 0.01f);

        Physics.OverlapSphere(new Vector3(5f, 1f, 5f), 0.1f).Should().NotBeEmpty(
            "the middle of the trunk is solid to an overlap question");
        Physics.OverlapSphere(new Vector3(5f, 5f, 5f), 0.5f).Should().BeEmpty(
            "the canopy is not: five metres up there is nothing to meet");
    }

    [Fact]
    public void ABallDroppedOnATree_RestsOnItsTrunk()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        terrain.TerrainData = Forest();
        ground.AddComponent<Overcore.Components.TerrainCollider>();

        GameObject ball = new("Ball");
        ball.Transform.WorldPosition = new Vector3(5f, 6f, 5f);
        ball.AddComponent(new SphereCollider { Radius = 0.5f });
        ball.AddComponent(new RigidBody { Mass = 1f });

        Scene scene = new(string.Empty, [ground, ball]);
        scene.Start(physics, audio);

        for (int step = 0; step < 240; step++)
        {
            physics.Update(1f / 60f);
        }

        ball.Transform.WorldPosition.Y.Should().BeApproximately(2.45f, 0.15f,
            "the trunk's top at 1.95, plus the ball's radius: the ball sits in the tree");
    }
}
