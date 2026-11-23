using AwesomeAssertions;
using Cherris.Components;
using Cherris.Core;
using System.Numerics;
using Xunit;

namespace CherrisEditorWeb.Tests;

public class SceneSnapshotBuilderTests
{
    [Fact]
    public void Build_ReturnsAnEmptySnapshot_WhenThereIsNoScene()
    {
        DateTimeOffset capturedAt = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

        SceneSnapshot snapshot = SceneSnapshotBuilder.Build(null, 42, capturedAt);

        snapshot.SceneName.Should().BeNull();
        snapshot.Nodes.Should().BeEmpty();
        snapshot.Frame.Should().Be(42);
        snapshot.CapturedAt.Should().Be(capturedAt);
    }

    [Fact]
    public void Build_CopiesObjectsComponentsAndTransform()
    {
        GameObject cameraObject = new("Camera");
        cameraObject.Transform.Position = new Vector3(1f, 2f, 3f);
        cameraObject.Transform.Scale = new Vector3(2f, 2f, 2f);
        cameraObject.AddComponent(new Camera());

        GameObject lightObject = new("Sun");
        lightObject.AddComponent(new Light { Type = LightType.Directional });

        Scene scene = new(string.Empty, [cameraObject, lightObject]);

        SceneSnapshot snapshot = SceneSnapshotBuilder.Build(scene, 15, DateTimeOffset.UtcNow);

        snapshot.SceneName.Should().Be("Untitled Scene");
        snapshot.Frame.Should().Be(15);
        snapshot.Nodes.Should().HaveCount(2);

        SceneNodeSnapshot camera = snapshot.Nodes[0];
        camera.Id.Should().Be(cameraObject.Id);
        camera.Name.Should().Be("Camera");
        camera.Position.Should().Be(new Vector3(1f, 2f, 3f));
        camera.Scale.Should().Be(new Vector3(2f, 2f, 2f));
        camera.Components.Should().ContainSingle().Which.TypeName.Should().Be(nameof(Camera));

        snapshot.Nodes[1].Components.Should().ContainSingle().Which.TypeName.Should().Be(nameof(Light));
    }

    [Fact]
    public void Build_CopiesAClickableSnapshotOfEachComponentsProperties()
    {
        GameObject sun = new("Sun");
        Light light = sun.AddComponent(new Light { Intensity = 2f, Type = LightType.Point });
        Scene scene = new(string.Empty, [sun]);

        SceneSnapshot snapshot = SceneSnapshotBuilder.Build(scene, 1, DateTimeOffset.UtcNow);

        ComponentSnapshot component = snapshot.Nodes[0].Components.Single();
        component.TypeName.Should().Be(nameof(Light));
        component.Properties.Single(property => property.Name == nameof(Light.Intensity)).Text.Should().Be("2");
        component.Properties.Single(property => property.Name == nameof(Light.Type)).Text.Should().Be("Point");

        // The snapshot is a copy: editing the live component afterwards must not rewrite what a panel holds.
        light.Intensity = 9f;
        component.Properties.Single(property => property.Name == nameof(Light.Intensity)).Text.Should().Be("2");
    }

    [Fact]
    public void Build_CapturesTheHierarchyParent()
    {
        GameObject parentObject = new("Body");
        GameObject childObject = new("Wheel");
        childObject.Transform.Parent = parentObject.Transform;

        Scene scene = new(string.Empty, [parentObject, childObject]);

        SceneSnapshot snapshot = SceneSnapshotBuilder.Build(scene, 3, DateTimeOffset.UtcNow);

        snapshot.Nodes.Should().HaveCount(2);
        snapshot.Nodes[0].ParentId.Should().BeNull();
        snapshot.Nodes[1].ParentId.Should().Be(parentObject.Id);

        IReadOnlyList<SceneTreeNode> roots = SceneTree.Build(snapshot.Nodes);

        roots.Should().ContainSingle();
        roots[0].Node.Id.Should().Be(parentObject.Id);
        roots[0].Children.Should().ContainSingle().Which.Node.Id.Should().Be(childObject.Id);
    }

    [Fact]
    public void Build_ReflectsLaterSceneChanges_WithoutTouchingAnEarlierSnapshot()
    {
        GameObject cube = new("Cube");
        cube.Transform.Position = Vector3.Zero;

        Scene scene = new("Assets/scene.json", [cube]);

        SceneSnapshot first = SceneSnapshotBuilder.Build(scene, 1, DateTimeOffset.UtcNow);

        cube.Transform.Position = new Vector3(0f, 5f, 0f);
        cube.AddComponent(new Light());

        SceneSnapshot second = SceneSnapshotBuilder.Build(scene, 2, DateTimeOffset.UtcNow);

        // Snapshots are copies: an older one must not change under a UI that is still reading it.
        first.Nodes[0].Position.Should().Be(Vector3.Zero);
        first.Nodes[0].Components.Should().BeEmpty();

        second.Nodes[0].Position.Should().Be(new Vector3(0f, 5f, 0f));
        second.Nodes[0].Components.Should().ContainSingle().Which.TypeName.Should().Be(nameof(Light));
        second.SceneName.Should().Be("scene.json");
    }
}
