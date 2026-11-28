using AwesomeAssertions;
using Overcore.Core;
using Overcore.Core.Physics;
using Overcore.Serialization;
using Overcore.Terrain;
using System.Numerics;
using Xunit;
using TerrainComponent = Overcore.Components.Terrain;

namespace Overcore.Tests.Terrain;

/// <summary>
/// Covers terrain holes end to end: the cell grid in the data, the mesh and raycast that respect it, the
/// brush that cuts it, and the scene file it survives.
/// </summary>
public class TerrainHolesTests
{
    [Fact]
    public void Holes_RoundTripThroughThePackedForm()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));
        data.SetHole(3, 4, true);
        data.SetHole(9, 9, true);
        data.SetHole(3, 4, true); // again: no change, no double count

        data.HoleCount.Should().Be(2);
        data.IsHoleAt(0.35f, 0.45f).Should().BeTrue();
        data.IsHoleAt(0.05f, 0.05f).Should().BeFalse();

        TerrainData copy = new(11, new Vector3(10f, 10f, 10f));
        copy.SetHolesPacked(data.GetHolesPacked());

        copy.HoleCount.Should().Be(2);
        copy.IsHole(3, 4).Should().BeTrue();
        copy.IsHole(9, 9).Should().BeTrue();
        copy.HolesVersion.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Mesher_DropsHoleCells()
    {
        TerrainData data = new(5, new Vector3(4f, 2f, 4f));
        int whole = TerrainMesher.Build(data).Indices.Length;

        data.SetHole(1, 1, true);
        data.SetHole(2, 1, true);

        TerrainMesher.Build(data).Indices.Length.Should().Be(whole - (2 * 6), "two cells, two triangles each");
        TerrainMesher.BuildTriangles(data).Should().HaveCount((whole / 3) - 4);
    }

    [Fact]
    public void Raycast_PassesThroughAHole_AndHitsSolidGround()
    {
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));
        data.SetHole(5, 5, true);

        TerrainRaycast.TryHitLocal(data, new Vector3(5.5f, 10f, 5.5f), -Vector3.UnitY, out _, out _)
            .Should().BeFalse("the middle cell is not there");

        TerrainRaycast.TryHitLocal(data, new Vector3(1f, 10f, 1f), -Vector3.UnitY, out Vector3 point, out _)
            .Should().BeTrue();
        point.Y.Should().Be(0f);
    }

    [Fact]
    public void HoleBrush_CutsAndFills()
    {
        TerrainData data = new(21, new Vector3(20f, 10f, 20f));

        int cut = TerrainBrush.ApplyHoles(data, new Vector2(0.5f, 0.5f), 0.2f, hole: true);
        cut.Should().BeGreaterThan(0);
        data.IsHoleAt(0.5f, 0.5f).Should().BeTrue();
        data.IsHoleAt(0f, 0f).Should().BeFalse();

        int filled = TerrainBrush.ApplyHoles(data, new Vector2(0.5f, 0.5f), 0.2f, hole: false);
        filled.Should().Be(cut);
        data.HoleCount.Should().Be(0);
    }

    [Fact]
    public void ComponentApplyHoles_RebuildsTheMesh_AndSerialises()
    {
        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        terrain.TerrainData = new TerrainData(11, new Vector3(10f, 10f, 10f));
        terrain.Rebuild();
        int whole = terrain.Mesh!.Indices.Length;

        terrain.ApplyHoles(new Vector2(0.5f, 0.5f), 0.2f, hole: true);

        terrain.Mesh!.Indices.Length.Should().BeLessThan(whole);
        terrain.TerrainData.HoleCount.Should().BeGreaterThan(0);

        string yaml = new SceneSerializer().SerializeSceneToString([ground]);
        yaml.Should().Contain("Holes");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        GameObject loaded = loader.LoadSceneFromYaml(yaml).Single();
        TerrainComponent loadedTerrain = loaded.GetComponent<TerrainComponent>()!;

        loadedTerrain.TerrainData.HoleCount.Should().Be(terrain.TerrainData.HoleCount);
        loadedTerrain.TerrainData.IsHoleAt(0.5f, 0.5f).Should().BeTrue();
    }

    [Fact]
    public void PhysicsRaycast_FallsThroughAHole()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        TerrainData data = new(11, new Vector3(10f, 10f, 10f));
        data.SetHole(5, 5, true);
        terrain.TerrainData = data;
        ground.AddComponent<Overcore.Components.TerrainCollider>();

        Scene scene = new(string.Empty, [ground]);
        scene.Start(physics, audio);

        Physics.Raycast(new Vector3(5.5f, 10f, 5.5f), -Vector3.UnitY, out _)
            .Should().BeFalse("there is no ground in the middle cell");
        Physics.Raycast(new Vector3(1f, 10f, 1f), -Vector3.UnitY, out RaycastHit hit)
            .Should().BeTrue();
        hit.Point.Y.Should().BeApproximately(0f, 0.01f);
    }
}
