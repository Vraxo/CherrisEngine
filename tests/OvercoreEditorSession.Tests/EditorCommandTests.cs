using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using OvercoreEditorSession.Host;
using System.Numerics;
using Xunit;

namespace OvercoreEditorSession.Tests;

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
    public void AddComponent_TakesAScriptTheSessionCompiled()
    {
        Harness scene = new();
        scene.Dispatcher.ScriptComponents = [new ComponentCatalog.Entry("Counter", typeof(CounterScript))];

        string? line = scene.Run(new AddComponentCommand(scene.Target.Id, "Counter"));

        line.Should().Contain("Counter");
        scene.Target.Components.Should().Contain(component => component is CounterScript);
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
    public void SetComponentEnabled_SwitchesItOff_AndUndoSwitchesItBackOn()
    {
        Harness scene = new();
        Light light = scene.Target.AddComponent(new Light());

        scene.Run(new SetComponentEnabledCommand(scene.Target.Id, nameof(Light), false)).Should().Contain("disabled");
        light.Enabled.Should().BeFalse();
        scene.Live.Lights.Should().BeEmpty("a light that is off does not light");
        scene.Live.IsDirty.Should().BeTrue();

        scene.Run(new UndoCommand()).Should().Contain("enabled");
        light.Enabled.Should().BeTrue();
        scene.Live.Lights.Should().Contain(light, "the renderer's list of lights follows the toggle back");

        scene.Run(new RedoCommand()).Should().Contain("disabled");
        light.Enabled.Should().BeFalse();
    }

    [Fact]
    public void SetComponentEnabled_ToTheStateItIsIn_RecordsNothing()
    {
        Harness scene = new();
        scene.Target.AddComponent(new Light());

        scene.Run(new SetComponentEnabledCommand(scene.Target.Id, nameof(Light), true)).Should().BeNull();
        scene.History.CanUndo.Should().BeFalse("a toggle that changes nothing is not a step to take back");
    }

    [Fact]
    public void SetComponentEnabled_WithoutTheComponent_SaysSo()
    {
        Harness scene = new();

        scene.Run(new SetComponentEnabledCommand(scene.Target.Id, nameof(Light), false))
            .Should().Contain("could not disable Light");

        scene.History.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void History_LabelsAToggle_WithWhatItDid()
    {
        Harness scene = new();
        scene.Target.AddComponent(new Light());

        scene.Run(new SetComponentEnabledCommand(scene.Target.Id, nameof(Light), false));

        scene.History.UndoLabel.Should().Be("disable Light");
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

        Vector3 euler = Overcore.EngineMath.ToEulerAngles(scene.Target.Transform.Rotation) * (180f / MathF.PI);
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

        scene.Run(new RedoCommand());
        scene.Target.Name.Should().Be("Crate", "Redo gives back the name Undo took");
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
    public void AddCSharpScript_WritesCompilesAndAttachesInThatOrder()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        bool compiledAfterTheFileExisted = false;
        int compiles = 0;

        scene.Dispatcher.ReloadScripts = () =>
        {
            compiles++;
            compiledAfterTheFileExisted = File.Exists(Path.Combine(project.Root, "Scripts", "CounterScript.cs"));

            return ComponentCatalog.ForScriptTypes([typeof(CounterScript)]);
        };

        scene.Run(new AddCSharpScriptCommand(scene.Target.Id, "CounterScript"))
            .Should().Contain("added Counter Script to Cube");

        compiles.Should().Be(1);
        compiledAfterTheFileExisted.Should().BeTrue("the file has to be on disk before the compile that sees it");

        scene.Target.GetComponent<CounterScript>().Should().NotBeNull();
        File.Exists(Path.Combine(project.Root, "Scripts", "CounterScript.cs")).Should().BeTrue();
        scene.Live.IsDirty.Should().BeTrue();

        // Undo takes the component off and leaves the file, the same trade the Lua command makes.
        scene.Run(new UndoCommand()).Should().Contain("removed Counter Script");
        scene.Target.GetComponent<CounterScript>().Should().BeNull();
        File.Exists(Path.Combine(project.Root, "Scripts", "CounterScript.cs")).Should().BeTrue();
    }

    [Fact]
    public void AddCSharpScript_ResolvesTheClassByName_NotTheMenuLabel()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        // The menu calls CounterScript "Counter Script"; the file, the class and the scene all say CounterScript.
        scene.Dispatcher.ReloadScripts = () => ComponentCatalog.ForScriptTypes([typeof(CounterScript)]);

        scene.Run(new AddCSharpScriptCommand(scene.Target.Id, "CounterScript")).Should().Contain("added");
        scene.Target.GetComponent<CounterScript>().Should().NotBeNull();
    }

    [Fact]
    public void AddCSharpScript_SaysWhenTheClassIsNotNamedAfterTheFile()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Dispatcher.ReloadScripts = () => ComponentCatalog.ForScriptTypes([typeof(CounterScript)]);

        scene.Run(new AddCSharpScriptCommand(scene.Target.Id, "Player"))
            .Should().Contain("declares no class named 'Player'");

        scene.Target.GetComponent<CounterScript>().Should().BeNull();
        File.Exists(Path.Combine(project.Root, "Scripts", "Player.cs")).Should().BeTrue("the file is not thrown away");
        scene.History.CanUndo.Should().BeFalse("nothing was attached");
    }

    [Fact]
    public void AddCSharpScript_WithoutACompiler_SaysWhatItWrote()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Run(new AddCSharpScriptCommand(scene.Target.Id, "Player"))
            .Should().Contain("no script compiler");
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

    [Fact]
    public void CreateGameObject_AddsAnEmptyObjectWithTheGivenId()
    {
        Harness scene = new();
        Guid objectId = Guid.NewGuid();

        string? line = scene.Run(new CreateGameObjectCommand(objectId, "GameObject"));

        line.Should().Contain("created GameObject");
        scene.Live.GameObjects.Should().ContainSingle(gameObject => gameObject.Id == objectId)
            .Which.Name.Should().Be("GameObject");
        scene.History.CanUndo.Should().BeTrue("creating the object is one step the author can take back");
    }

    [Fact]
    public void CreateGameObject_TakesTheObjectBackAndPutsItBack()
    {
        Harness scene = new();
        Guid objectId = Guid.NewGuid();
        scene.Run(new CreateGameObjectCommand(objectId, "GameObject"));

        scene.Run(new UndoCommand());
        scene.Live.GameObjects.Should().NotContain(gameObject => gameObject.Id == objectId);

        scene.Run(new RedoCommand());
        scene.Live.GameObjects.Should().ContainSingle(gameObject => gameObject.Id == objectId)
            .Which.Name.Should().Be("GameObject", "Redo puts back the object Undo took, not a copy");
    }

    [Fact]
    public void DeleteGameObject_TakesTheSubtreeOut_AndUndoPutsItBackWhereItWas()
    {
        Harness scene = new();

        // Parent -> [Before, Cube -> Arm -> Hand, After]: the delete must take Cube's subtree and nothing
        // else, and Undo must put every object back at its old index, the root among its old siblings.
        GameObject parent = new("Parent");
        GameObject before = new("Before");
        GameObject after = new("After");
        GameObject arm = new("Arm");
        GameObject hand = new("Hand");

        scene.Live.AddGameObject(parent);
        scene.Live.AddGameObject(before);
        scene.Live.AddGameObject(after);
        scene.Live.AddGameObject(arm);
        scene.Live.AddGameObject(hand);

        before.Transform.Parent = parent.Transform;
        scene.Target.Transform.Parent = parent.Transform;
        after.Transform.Parent = parent.Transform;
        arm.Transform.Parent = scene.Target.Transform;
        hand.Transform.Parent = arm.Transform;
        Light light = arm.AddComponent(new Light());
        scene.Live.FindMainComponents();

        GameObject[] wasThere = [.. scene.Live.GameObjects];

        scene.Run(new DeleteGameObjectCommand(scene.Target.Id, "Cube")).Should().Contain("deleted Cube");
        scene.Live.GameObjects.Should().Equal(new[] { parent, before, after },
            "the subtree leaves together and nothing with it");
        scene.Live.Lights.Should().BeEmpty("the light left with its object");
        scene.Target.Transform.Parent.Should().BeNull();

        scene.Run(new UndoCommand());
        scene.Live.GameObjects.Should().Equal(wasThere, "every object comes back at the index it left");
        scene.Live.GameObjects.Should().ContainSingle(gameObject => gameObject.Name == "Arm")
            .Which.Should().BeSameAs(arm, "Undo puts back the object the delete took, not a copy");
        parent.Transform.Children.Should().Equal(before.Transform, scene.Target.Transform, after.Transform);
        scene.Live.Lights.Should().ContainSingle().Which.Should().BeSameAs(light);

        scene.Run(new RedoCommand());
        scene.Live.GameObjects.Should().Equal(new[] { parent, before, after });
    }

    [Fact]
    public void DeleteGameObject_ReportsAnObjectThatIsGone()
    {
        Harness scene = new();

        scene.Run(new DeleteGameObjectCommand(Guid.NewGuid(), "Ghost"))
            .Should().Contain("could not delete Ghost");

        scene.History.CanUndo.Should().BeFalse("a delete that took nothing is not a step to take back");
    }

    [Fact]
    public void CreatePrefabFromSelection_WritesTheAsset_AndLinksTheObjects()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);
        GameObject arm = new("Arm");
        arm.Transform.Parent = scene.Target.Transform;
        scene.Live.AddGameObject(arm);

        string path = Path.Combine(project.Root, "Cube.prefab");
        scene.Run(new CreatePrefabFromSelectionCommand(scene.Target.Id, path)).Should().Contain("created prefab Cube");

        File.Exists(path).Should().BeTrue("the asset is written");
        File.Exists(path + ".meta").Should().BeTrue("the asset gets its guid sidecar");
        scene.Target.PrefabSource.Should().NotBeNull("the saved objects are the prefab's first instance");
        scene.Target.PrefabSource!.FileId.Should().Be(1);
        arm.PrefabSource!.FileId.Should().Be(2, "the subtree keeps the ids the file gave it");

        scene.Run(new UndoCommand()).Should().Contain("unlinked");
        scene.Target.PrefabSource.Should().BeNull();
        arm.PrefabSource.Should().BeNull();
        File.Exists(path).Should().BeTrue("the asset stays: it is a project file now");

        scene.Run(new RedoCommand());
        scene.Target.PrefabSource.Should().NotBeNull();
        arm.PrefabSource!.FileId.Should().Be(2, "Redo links the same objects at the same ids");
    }

    [Fact]
    public void CreatePrefabFromSelection_ReportsAnObjectThatIsGone()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        scene.Run(new CreatePrefabFromSelectionCommand(Guid.NewGuid(), Path.Combine(project.Root, "Ghost.prefab")))
            .Should().Contain("could not create prefab");

        scene.History.CanUndo.Should().BeFalse("a save that wrote nothing is not a step to take back");
    }

    [Fact]
    public void InstantiatePrefab_AddsTheSubtree_AndUndoTakesItBack()
    {
        using TemporaryProject project = new();
        Harness scene = new(project.Root);

        GameObject instance = new("Rope");
        GameObject knot = new("Knot");
        knot.Transform.Parent = instance.Transform;
        instance.PrefabSource = new PrefabSource("abc123", 1);
        knot.PrefabSource = new PrefabSource("abc123", 2);
        scene.Dispatcher.LoadPrefabAsset = _ => [instance];

        scene.Run(new InstantiatePrefabCommand("Rope.prefab")).Should().Contain("instantiated Rope");
        scene.Live.GameObjects.Should().Contain(instance, "the instance arrives at the scene root");
        scene.Live.GameObjects.Should().Contain(knot, "the whole subtree arrives with it");
        instance.PrefabSource.Should().NotBeNull("every object keeps its link to the asset");

        scene.Run(new UndoCommand());
        scene.Live.GameObjects.Should().NotContain(instance).And.NotContain(knot);

        scene.Run(new RedoCommand());
        scene.Live.GameObjects.Should().Contain(instance, "Redo puts the same objects back, not new ones");
        scene.Live.GameObjects.Should().Contain(knot);
    }

    [Fact]
    public void InstantiatePrefab_WithoutALoader_SaysSo()
    {
        Harness scene = new();

        scene.Run(new InstantiatePrefabCommand("Rope.prefab")).Should().Contain("cannot read prefabs");
        scene.History.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void InstantiatePrefab_AnUnreadablePrefab_SaysSo()
    {
        Harness scene = new();
        scene.Dispatcher.LoadPrefabAsset = _ => [];

        scene.Run(new InstantiatePrefabCommand("Dust.prefab")).Should().Contain("could not be read");
        scene.History.CanUndo.Should().BeFalse("nothing arrived, so there is nothing to take back");
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

    /// <summary>A component that only exists because a project compiled it, for the name-resolution test.</summary>
    public sealed class CounterScript : Script
    {
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
