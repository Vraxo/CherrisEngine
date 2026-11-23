using AwesomeAssertions;
using Cherris.Components;
using Cherris.Core;
using CherrisEditorWeb.Host;
using System.Numerics;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers the path the inspector's controls take: the shared catalog, the queue the UI thread hands work over
/// on, the command the render thread executes, and the history that takes it back.
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
        Harness scene = new();
        EditorCommandQueue queue = new();
        List<string> log = [];

        queue.Enqueue(new RecordingCommand("first", log));
        queue.Enqueue(new RecordingCommand("second", log));

        queue.Count.Should().Be(2);
        queue.Drain(command => command.Execute(scene.Context()), log.Add).Should().Be(2);
        queue.Count.Should().Be(0);

        log.Should().Equal("first", "second");
    }

    [Fact]
    public void Queue_SurvivesACommandThatThrows()
    {
        Harness scene = new();
        EditorCommandQueue queue = new();
        List<string> log = [];

        queue.Enqueue(new ThrowingCommand());
        queue.Enqueue(new RecordingCommand("after", log));

        queue.Drain(command => command.Execute(scene.Context()), log.Add).Should().Be(2);

        log.Should().HaveCount(2);
        log[0].Should().Contain("throw").And.Contain("boom");
        log[1].Should().Be("after", "the command after the broken one still runs");
    }

    [Fact]
    public void AddComponent_PutsTheComponentOnTheObject()
    {
        Harness scene = new();

        string? line = scene.Run(new AddComponentCommand(scene.Target.Id, "Light"));

        scene.Target.GetComponent<Light>().Should().NotBeNull();
        scene.Live.IsDirty.Should().BeTrue("an unsaved edit is what tells the user to save");
        line.Should().Contain("added Light to Cube");
    }

    [Fact]
    public void AddComponent_RefusesASecondOneOfTheSameType()
    {
        Harness scene = new();
        scene.Target.AddComponent(new Light());

        scene.Run(new AddComponentCommand(scene.Target.Id, "Light")).Should().Contain("already has");

        scene.Target.GetComponents<Light>().Should().ContainSingle();
    }

    [Fact]
    public void AddComponent_ReportsAnObjectThatIsGone()
    {
        Harness scene = new();

        scene.Run(new AddComponentCommand(Guid.NewGuid(), "Camera")).Should().Contain("no longer in the scene");
    }

    [Fact]
    public void AddComponent_ReportsAnUnknownComponent()
    {
        Harness scene = new();

        scene.Run(new AddComponentCommand(scene.Target.Id, "Rocket Booster")).Should().Contain("Rocket Booster");
        scene.Target.Components.Should().BeEmpty();
    }

    [Fact]
    public void AddComponent_MakesANewCameraTheMainOne()
    {
        Harness scene = new();
        scene.Live.MainCamera.Should().BeNull();

        scene.Run(new AddComponentCommand(scene.Target.Id, "Camera"));

        scene.Live.MainCamera.Should().NotBeNull();
    }

    [Fact]
    public void AddComponent_UndoRemovesItAgain()
    {
        Harness scene = new();

        scene.Run(new AddComponentCommand(scene.Target.Id, "Light"));
        scene.Target.GetComponent<Light>().Should().NotBeNull();

        scene.Run(new UndoCommand());
        scene.Target.GetComponent<Light>().Should().BeNull();

        scene.Run(new RedoCommand());
        scene.Target.GetComponent<Light>().Should().NotBeNull();
    }

    [Fact]
    public void SetProperty_WritesTheValue_AndUndoRestoresIt()
    {
        Harness scene = new();
        Light light = scene.Target.AddComponent(new Light());

        scene.Run(new SetPropertyCommand(scene.Target.Id, nameof(Light), nameof(Light.Intensity), "2.5"));
        light.Intensity.Should().Be(2.5f);

        scene.Run(new UndoCommand());
        light.Intensity.Should().Be(1f, "the command kept the value it replaced");

        scene.Run(new RedoCommand());
        light.Intensity.Should().Be(2.5f);
    }

    [Fact]
    public void SetProperty_ReportsBadText_AndRecordsNothing()
    {
        Harness scene = new();
        Light light = scene.Target.AddComponent(new Light());

        scene.Run(new SetPropertyCommand(scene.Target.Id, nameof(Light), nameof(Light.Intensity), "bright"))
            .Should().Contain("Intensity");

        light.Intensity.Should().Be(1f);
        scene.History.CanUndo.Should().BeFalse("a rejected edit is not a change to take back");
    }

    [Fact]
    public void SetProperty_ClampsToTheRangeThePropertyDeclares()
    {
        Harness scene = new();
        AudioSource source = scene.Target.AddComponent(new AudioSource { Volume = 0.5f });

        scene.Run(new SetPropertyCommand(scene.Target.Id, nameof(AudioSource), nameof(AudioSource.Volume), "5"));

        source.Volume.Should().Be(1f, "Volume is declared [Range(0, 1)]");
    }

    [Fact]
    public void SetTransform_WritesPosition_AndEulerRotation()
    {
        Harness scene = new();

        scene.Run(new SetTransformCommand(scene.Target.Id, TransformField.Position, new Vector3(1f, 2f, 3f)));
        scene.Target.Transform.Position.Should().Be(new Vector3(1f, 2f, 3f));

        scene.Run(new SetTransformCommand(scene.Target.Id, TransformField.Rotation, new Vector3(0f, 90f, 0f)));

        Vector3 euler = Cherris.EngineMath.ToEulerAngles(scene.Target.Transform.Rotation) * (180f / MathF.PI);
        euler.Y.Should().BeApproximately(90f, 0.1f);

        scene.Run(new UndoCommand());
        scene.Target.Transform.Rotation.Should().Be(Quaternion.Identity);
    }

    [Fact]
    public void Rename_ChangesTheName_AndUndoTakesItBack()
    {
        Harness scene = new();

        scene.Run(new RenameObjectCommand(scene.Target.Id, "Crate"));
        scene.Target.Name.Should().Be("Crate");

        scene.Run(new UndoCommand());
        scene.Target.Name.Should().Be("Cube");
    }

    [Fact]
    public void Rename_RejectsAnEmptyName()
    {
        Harness scene = new();

        scene.Run(new RenameObjectCommand(scene.Target.Id, "   ")).Should().Contain("cannot be empty");
        scene.Target.Name.Should().Be("Cube");
    }

    [Fact]
    public void History_DropsTheRedoBranchWhenANewEditLands()
    {
        Harness scene = new();
        Light light = scene.Target.AddComponent(new Light());

        scene.Run(new SetPropertyCommand(scene.Target.Id, nameof(Light), nameof(Light.Intensity), "2"));
        scene.Run(new UndoCommand());
        scene.History.CanRedo.Should().BeTrue();

        scene.Run(new SetPropertyCommand(scene.Target.Id, nameof(Light), nameof(Light.Intensity), "3"));

        scene.History.CanRedo.Should().BeFalse("the redo branch belonged to the edit that was replaced");
        light.Intensity.Should().Be(3f);
    }

    [Fact]
    public void History_ReportsNothingToUndoOrRedo()
    {
        Harness scene = new();

        scene.Run(new UndoCommand()).Should().Contain("nothing to undo");
        scene.Run(new RedoCommand()).Should().Contain("nothing to redo");
    }

    [Fact]
    public void History_LabelsWhatTheMenuWouldTakeBack()
    {
        Harness scene = new();

        scene.Run(new AddComponentCommand(scene.Target.Id, "Light"));

        scene.History.CanUndo.Should().BeTrue();
        scene.History.UndoLabel.Should().Be("add Light");
    }

    /// <summary>A scene, an object in it, and one dispatcher whose history the commands record into.</summary>
    private sealed class Harness
    {
        public Harness()
        {
            Target = new GameObject("Cube");
            Target.Transform.Position = new Vector3(0f, 1f, 0f);
            Live = new Scene(string.Empty, [Target]);
        }

        public Scene Live { get; }

        public GameObject Target { get; }

        public EditorCommandDispatcher Dispatcher { get; } = new();

        public EditorHistory History => Dispatcher.History;

        public EditorCommandContext Context() => new(Live, History);

        /// <summary>Queues one command, runs it, and returns the line it logged.</summary>
        public string? Run(IEditorCommand command)
        {
            string? line = null;

            Dispatcher.Enqueue(command);
            Dispatcher.Drain(Live, result => line = result);

            return line;
        }
    }

    private sealed class RecordingCommand(string line, List<string> log) : IEditorCommand
    {
        public string Describe => line;

        public string? Execute(EditorCommandContext context)
        {
            log.Add(line);
            return null;
        }
    }

    private sealed class ThrowingCommand : IEditorCommand
    {
        public string Describe => "throw";

        public string? Execute(EditorCommandContext context) => throw new InvalidOperationException("boom");
    }
}
