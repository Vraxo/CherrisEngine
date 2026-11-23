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
    public void SetProperty_FeedingTheSameCommandIsOneUndoStep()
    {
        Harness scene = new();
        Light light = scene.Target.AddComponent(new Light());
        SetPropertyCommand command = new(scene.Target.Id, nameof(Light), nameof(Light.Intensity), "2");

        scene.Run(command).Should().Contain("Intensity", "the first frame of a drag is the one worth logging");

        // What a drag does: the same command, a newer value, queued and run again on the next frame.
        string[] frames = ["3", "4", "5", "6"];

        foreach (string text in frames)
        {
            command.Text = text;
            scene.Run(command).Should().BeNull("every frame of one drag must not add a console line");
        }

        light.Intensity.Should().Be(6f, "the scene followed every frame");

        scene.Run(new UndoCommand());
        light.Intensity.Should().Be(1f, "undo goes back to where the drag started, not back one frame");
        scene.History.CanUndo.Should().BeFalse("the whole drag was a single step");

        scene.Run(new RedoCommand());
        light.Intensity.Should().Be(6f, "redo lands on the value the drag ended on");
    }

    [Fact]
    public void SetProperty_AFrameThatChangesNothingDoesNotRecord()
    {
        Harness scene = new();
        Light light = scene.Target.AddComponent(new Light());
        SetPropertyCommand command = new(scene.Target.Id, nameof(Light), nameof(Light.Intensity), "1");

        scene.Run(command).Should().BeNull("the text parses to the value it would replace");
        scene.History.CanUndo.Should().BeFalse("a frame that changes nothing is not a step to take back");

        command.Text = "2";
        scene.Run(command).Should().Contain("Intensity");

        scene.Run(new UndoCommand());
        light.Intensity.Should().Be(1f, "the step starts from the value the drag found");
    }

    [Fact]
    public void SetTransform_FeedingTheSameCommandIsOneUndoStep()
    {
        Harness scene = new();
        SetTransformCommand command =
            new(scene.Target.Id, TransformField.Position, new Vector3(1f, 1f, 0f));

        scene.Run(command).Should().Contain("position");
        scene.Target.Transform.Position.Should().Be(new Vector3(1f, 1f, 0f));

        float[] frames = [2f, 3f, 4f];

        foreach (float x in frames)
        {
            command.Value = new Vector3(x, 1f, 0f);
            scene.Run(command).Should().BeNull("every frame of one drag must not add a console line");
        }

        scene.Target.Transform.Position.Should().Be(new Vector3(4f, 1f, 0f));

        scene.Run(new UndoCommand());
        scene.Target.Transform.Position.Should().Be(new Vector3(0f, 1f, 0f), "the drag started at 0, 1, 0");
        scene.History.CanUndo.Should().BeFalse("the whole drag was a single step");

        scene.Run(new RedoCommand());
        scene.Target.Transform.Position.Should().Be(new Vector3(4f, 1f, 0f));
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
    public void SetProperty_ResettingToTheDefaultIsAnOrdinaryStep()
    {
        Harness scene = new();
        Light light = scene.Target.AddComponent(new Light());

        scene.Run(new SetPropertyCommand(scene.Target.Id, nameof(Light), nameof(Light.Intensity), "4.5"));
        light.Intensity.Should().Be(4.5f);

        // What the reset button does: the same command with the value the type's default snapshot carries.
        ComponentProperties.TryReadDefaultText(typeof(Light), nameof(Light.Intensity), out string reset)
            .Should().BeTrue();

        scene.Run(new SetPropertyCommand(scene.Target.Id, nameof(Light), nameof(Light.Intensity), reset))
            .Should().Contain("Intensity");

        light.Intensity.Should().Be(1f, "a new Light starts at intensity 1");

        scene.Run(new UndoCommand());
        light.Intensity.Should().Be(4.5f, "a reset is nothing special to undo");
    }

    [Fact]
    public void SetTransform_ResettingARowIsAnOrdinaryStep()
    {
        Harness scene = new();

        scene.Target.Transform.Position.Should().Be(new Vector3(0f, 1f, 0f), "the harness starts it at y = 1");

        // What the row's reset button hands the command: the row as the inspector sees it, and its default.
        SceneNodeSnapshot node = new(
            scene.Target.Id, "Cube", scene.Target.Transform.Position, scene.Target.Transform.Rotation,
            scene.Target.Transform.Scale, []);

        TransformRow position = TransformRows.From(node)[0];

        position.CanReset.Should().BeTrue("the object is away from the origin");

        scene.Run(new SetTransformCommand(scene.Target.Id, position.Field, position.Default));

        scene.Target.Transform.Position.Should().Be(Vector3.Zero);

        scene.Run(new UndoCommand());
        scene.Target.Transform.Position.Should().Be(new Vector3(0f, 1f, 0f));
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

    [Fact]
    public void AddScript_WritesTheFileAndAttachesIt()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Run(new AddScriptCommand(scene.Target.Id, "Player"))
            .Should().Contain("added script Scripts/Player.lua to Cube");

        LanguageScript? script = scene.Target.GetComponent<LanguageScript>();
        script.Should().NotBeNull();
        script!.SourcePath.Should().Be("Scripts/Player.lua");
        script.Language.Should().Be("lua");

        File.Exists(Path.Combine(project.Root, "Scripts", "Player.lua")).Should().BeTrue();
        File.ReadAllText(Path.Combine(project.Root, "Scripts", "Player.lua"))
            .Should().Contain("function update(deltaTime)");
        scene.Live.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void AddScript_ForgivesTheExtension()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Run(new AddScriptCommand(scene.Target.Id, "Player.lua"));

        scene.Target.GetComponent<LanguageScript>()!.SourcePath.Should().Be("Scripts/Player.lua");
    }

    [Fact]
    public void AddScript_ReusesAScriptThatIsAlreadyThere()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        Directory.CreateDirectory(Path.Combine(project.Root, "Scripts"));
        string file = Path.Combine(project.Root, "Scripts", "Player.lua");
        File.WriteAllText(file, "-- written by hand");

        scene.Run(new AddScriptCommand(scene.Target.Id, "Player")).Should().Contain("Scripts/Player.lua");

        File.ReadAllText(file).Should().Be("-- written by hand");
        scene.Target.GetComponent<LanguageScript>().Should().NotBeNull();
    }

    [Fact]
    public void AddScript_UndoRemovesTheComponent_AndKeepsTheFile()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Run(new AddScriptCommand(scene.Target.Id, "Player"));

        scene.Run(new UndoCommand()).Should().Contain("the file is still there");
        scene.Target.GetComponent<LanguageScript>().Should().BeNull();
        File.Exists(Path.Combine(project.Root, "Scripts", "Player.lua")).Should().BeTrue();

        scene.Run(new RedoCommand());
        scene.Target.GetComponent<LanguageScript>().Should().NotBeNull();
    }

    [Fact]
    public void AddScript_RefusesTheSameScriptTwice()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Run(new AddScriptCommand(scene.Target.Id, "Player"));
        scene.Run(new AddScriptCommand(scene.Target.Id, "Player.lua")).Should().Contain("already has");

        scene.Target.GetComponents<LanguageScript>().Should().ContainSingle();
    }

    [Fact]
    public void AddScript_WithoutAProject_SaysSo()
    {
        Harness scene = new();

        scene.Run(new AddScriptCommand(scene.Target.Id, "Player")).Should().Contain("no project folder");
        scene.Target.GetComponent<LanguageScript>().Should().BeNull();
        scene.History.CanUndo.Should().BeFalse("nothing happened to take back");
    }

    [Fact]
    public void AddScript_RejectsANameThatCannotBeOne()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Run(new AddScriptCommand(scene.Target.Id, "my script")).Should().Contain("cannot be a script name");

        scene.Target.GetComponent<LanguageScript>().Should().BeNull();
        Directory.Exists(Path.Combine(project.Root, "Scripts")).Should().BeFalse("no file is written for a bad name");
    }

    [Fact]
    public void AddScript_ReportsAnObjectThatIsGone()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Run(new AddScriptCommand(Guid.NewGuid(), "Player")).Should().Contain("no longer in the scene");
    }

    /// <summary>A scene, an object in it, and one dispatcher whose history the commands record into.</summary>
    private sealed class Harness
    {
        /// <param name="projectRoot">A folder standing in for an open project, or null for the demo scene.</param>
        public Harness(string? projectRoot = null)
        {
            Target = new GameObject("Cube");
            Target.Transform.Position = new Vector3(0f, 1f, 0f);
            Live = new Scene(string.Empty, [Target]);
            Dispatcher = new EditorCommandDispatcher(projectRoot);
        }

        public Scene Live { get; }

        public GameObject Target { get; }

        public EditorCommandDispatcher Dispatcher { get; }

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
