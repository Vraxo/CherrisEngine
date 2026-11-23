using AwesomeAssertions;
using Cherris.Components;
using Cherris.Core;
using CherrisEditorWeb.Host;
using System.Numerics;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers the path the inspector's Add Component menu takes: the shared catalog the menu lists, the queue the
/// UI thread hands work over on, and the command the render thread executes against the live scene.
/// </summary>
public class EditorCommandTests
{
    [Fact]
    public void Catalog_ListsCreatableComponentsWithUniqueNames()
    {
        ComponentCatalog.BuiltIn.Should().NotBeEmpty();
        ComponentCatalog.BuiltIn.Select(entry => entry.DisplayName)
            .Should().OnlyHaveUniqueItems("the menu is a list of names, and a command quotes one back");
        ComponentCatalog.BuiltIn.Should()
            .OnlyContain(entry => typeof(Component).IsAssignableFrom(entry.ComponentType));

        foreach (ComponentCatalog.Entry entry in ComponentCatalog.BuiltIn)
        {
            entry.Create().Should().BeOfType(entry.ComponentType);
        }
    }

    [Fact]
    public void Catalog_FindsByName_AndRejectsAnythingElse()
    {
        ComponentCatalog.Find("Camera")!.ComponentType.Should().Be(typeof(Camera));
        ComponentCatalog.Find("camera").Should().BeNull("the menu shows exact names");
        ComponentCatalog.Find("Nope").Should().BeNull();
        ComponentCatalog.Find(null).Should().BeNull();
        ComponentCatalog.Find("  ").Should().BeNull();
    }

    [Fact]
    public void Queue_RunsCommandsOldestFirst()
    {
        EditorCommandQueue queue = new();
        List<string> log = [];

        queue.Enqueue(new RecordingCommand("first", log));
        queue.Enqueue(new RecordingCommand("second", log));

        queue.Count.Should().Be(2);
        queue.Drain(command => command.Execute(new Scene(string.Empty, [])), log.Add).Should().Be(2);
        queue.Count.Should().Be(0);

        log.Should().Equal("first", "second");
    }

    [Fact]
    public void Queue_SurvivesACommandThatThrows()
    {
        EditorCommandQueue queue = new();
        List<string> log = [];

        queue.Enqueue(new ThrowingCommand());
        queue.Enqueue(new RecordingCommand("after", log));

        queue.Drain(command => command.Execute(new Scene(string.Empty, [])), log.Add).Should().Be(2);

        log.Should().HaveCount(2);
        log[0].Should().Contain("throw").And.Contain("boom");
        log[1].Should().Be("after", "the command after the broken one still runs");
    }

    [Fact]
    public void AddComponent_PutsTheComponentOnTheObject()
    {
        (Scene scene, GameObject target) = SceneWithObject();

        string? line = new AddComponentCommand(target.Id, "Light").Execute(scene);

        target.GetComponent<Light>().Should().NotBeNull();
        scene.IsDirty.Should().BeTrue("an unsaved edit is what tells the user to save");
        line.Should().Contain("added Light to Cube");
    }

    [Fact]
    public void AddComponent_RefusesASecondOneOfTheSameType()
    {
        (Scene scene, GameObject target) = SceneWithObject();
        target.AddComponent(new Light());

        string? line = new AddComponentCommand(target.Id, "Light").Execute(scene);

        target.GetComponents<Light>().Should().ContainSingle();
        line.Should().Contain("already has");
        scene.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void AddComponent_ReportsAnObjectThatIsGone()
    {
        Scene scene = new(string.Empty, []);

        string? line = new AddComponentCommand(Guid.NewGuid(), "Camera").Execute(scene);

        line.Should().Contain("no longer in the scene");
    }

    [Fact]
    public void AddComponent_ReportsAnUnknownComponent()
    {
        (Scene scene, GameObject target) = SceneWithObject();

        string? line = new AddComponentCommand(target.Id, "Rocket Booster").Execute(scene);

        target.Components.Should().BeEmpty();
        line.Should().Contain("Rocket Booster");
    }

    [Fact]
    public void AddComponent_MakesANewCameraTheMainOne()
    {
        (Scene scene, GameObject target) = SceneWithObject();
        scene.MainCamera.Should().BeNull();

        new AddComponentCommand(target.Id, "Camera").Execute(scene);

        scene.MainCamera.Should().NotBeNull();
    }

    private static (Scene Scene, GameObject Target) SceneWithObject()
    {
        GameObject target = new("Cube");
        target.Transform.Position = new Vector3(0f, 1f, 0f);
        Scene scene = new(string.Empty, [target]);

        return (scene, target);
    }

    private sealed class RecordingCommand(string line, List<string> log) : IEditorCommand
    {
        public string Describe => line;

        public string? Execute(Scene scene)
        {
            log.Add(line);
            return null;
        }
    }

    private sealed class ThrowingCommand : IEditorCommand
    {
        public string Describe => "throw";

        public string? Execute(Scene scene) => throw new InvalidOperationException("boom");
    }
}
