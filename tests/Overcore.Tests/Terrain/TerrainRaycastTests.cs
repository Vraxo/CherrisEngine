using AwesomeAssertions;
using Overcore.Core;
using Overcore.Terrain;
using System.Numerics;
using Xunit;
using TerrainComponent = Overcore.Components.Terrain;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers <see cref="TerrainRaycast"/> and the component's world-space picking: the arithmetic the
/// editor's brush and the physics raycast share.
/// </summary>
public class TerrainRaycastTests
{
    [Fact]
    public void LocalRay_MeetsFlatGroundStraightDown()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));

        bool hit = TerrainRaycast.TryHitLocal(
            data, new Vector3(5f, 10f, 5f), -Vector3.UnitY, out Vector3 point, out float parameter);

        hit.Should().BeTrue();
        point.Should().Be(new Vector3(5f, 0f, 5f));
        parameter.Should().BeApproximately(10f, 1e-4f);
    }

    [Fact]
    public void LocalRay_MeetsSlopedGroundAtItsHeight()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));
        for (int z = 0; z < 11; z++)
        {
            for (int x = 0; x < 11; x++)
            {
                data.SetHeight(x, z, x);
            }
        }

        TerrainRaycast.TryHitLocal(
            data, new Vector3(5f, 20f, 5f), -Vector3.UnitY, out Vector3 point, out _).Should().BeTrue();
        point.Y.Should().BeApproximately(5f, 1e-3f);
    }

    [Fact]
    public void LocalRay_PointingAwayFromTheFootprint_Misses()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));

        TerrainRaycast.TryHitLocal(
            data, new Vector3(-5f, 10f, 5f), -Vector3.UnitX, out _, out _).Should().BeFalse();

        // Parallel above the ground, never descending.
        TerrainRaycast.TryHitLocal(
            data, new Vector3(5f, 10f, 5f), Vector3.UnitX, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void LocalRay_EntersThroughTheSide_AndMeetsTheGround()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));

        bool hit = TerrainRaycast.TryHitLocal(
            data, new Vector3(5f, 5f, -10f), Vector3.Normalize(new Vector3(0f, -0.5f, 1f)),
            out Vector3 point, out _);

        hit.Should().BeTrue();
        point.Y.Should().BeApproximately(0f, 1e-3f);
        point.Z.Should().BeInRange(0f, 10f);
    }

    [Fact]
    public void ComponentTryHit_AnswersInWorldSpace_WithTheBrushUv()
    {
        GameObject ground = new("Ground");
        ground.Transform.Position = new Vector3(10f, 0f, 10f);
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        terrain.TerrainData = new TerrainData(11, new Vector3(10f, 10f, 10f));

        bool hit = terrain.TryHit(
            new Ray(new Vector3(15f, 10f, 15f), -Vector3.UnitY), out Vector3 point, out Vector2 uv);

        hit.Should().BeTrue();
        point.Should().Be(new Vector3(15f, 0f, 15f));
        uv.Should().Be(new Vector2(0.5f, 0.5f));

        terrain.TryHit(new Ray(new Vector3(-20f, 10f, 15f), -Vector3.UnitY), out _, out _)
            .Should().BeFalse("the ray never crosses this terrain's footprint");
    }
}
