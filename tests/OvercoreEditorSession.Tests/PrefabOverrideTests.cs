using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using OvercoreEditorSession.Host;
using System.Numerics;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// The prefab override model: what has drifted from an instance's asset, named as (path, source text)
/// pairs, put back by Revert and pushed into the asset by Apply — each with honest undo.
/// </summary>
public class PrefabOverrideTests
{
    [Fact]
    public void Diff_OneChangedValue_NamesThePathAndTheSourcesText()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, ComponentPropertiesProbe camera) = scene.MakeLamp();

        string? before = ComponentProperties.Read(camera.Component).Single(p => p.Name == camera.Property).Text;
        ComponentProperties.TryApply(camera.Component, camera.Property, "30", out _).Should().BeTrue();

        IReadOnlyList<PrefabOverrideSnapshot> overrides = scene.Diff(lamp);

        overrides.Should().ContainSingle("one value drifted");
        overrides[0].PropertyPath.Should().Be(PrefabPaths.Property("Camera", camera.Property));
        overrides[0].SourceText.Should().Be(before, "the diff names what the prefab holds, not the change");

        // A second drift adds a second line, and its own revert clears it again.
        lamp.Name = "Beacon";
        scene.Diff(lamp).Should().HaveCount(2, "the name drifted too");

        lamp.Name = "Lamp";
        scene.Diff(lamp).Should().ContainSingle("the name is back, so only the value is left");
    }

    [Fact]
    public void Revert_PutsTheValueBack_AndUndoPutsTheChangeBack()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, ComponentPropertiesProbe camera) = scene.MakeLamp();

        string? sourceText = ComponentProperties.Read(camera.Component).Single(p => p.Name == camera.Property).Text;
        ComponentProperties.TryApply(camera.Component, camera.Property, "30", out _);
        string path = PrefabPaths.Property("Camera", camera.Property);

        scene.Run(new RevertPrefabOverrideCommand(lamp.Id, path)).Should().Be($"[editor] reverted {path}.");
        ComponentProperties.Read(camera.Component).Single(p => p.Name == camera.Property).Text
            .Should().Be(sourceText, "the value is the prefab's again");

        scene.Run(new UndoCommand()).Should().Contain("restored");
        ComponentProperties.Read(camera.Component).Single(p => p.Name == camera.Property).Text
            .Should().Be("30", "taking the step back brings the change back");

        scene.Run(new RedoCommand()).Should().Contain("reverted");
        ComponentProperties.Read(camera.Component).Single(p => p.Name == camera.Property).Text
            .Should().Be(sourceText);
    }

    [Fact]
    public void Revert_NothingDrifted_SaysSoAndRecordsNothing()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, ComponentPropertiesProbe camera) = scene.MakeLamp();

        scene.Run(new RevertPrefabOverrideCommand(lamp.Id, PrefabPaths.Property("Camera", camera.Property)))
            .Should().Contain("it is not overridden");

        scene.Run(new UndoCommand()).Should().Contain("unlinked",
            "the failed revert pushed nothing on the history — one undo still takes back the save");
    }

    [Fact]
    public void RevertAnUnlinkedObjectOrAGoneObject_SaysSo()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, ComponentPropertiesProbe camera) = scene.MakeLamp();
        GameObject loose = new("Loose");
        scene.Live.AddGameObject(loose);

        scene.Run(new RevertPrefabOverrideCommand(loose.Id, PrefabPaths.Property("Camera", camera.Property)))
            .Should().Contain("not linked to a prefab");
        scene.Run(new RevertPrefabOverrideCommand(Guid.NewGuid(), PrefabPaths.Name))
            .Should().Contain("the object is gone");
    }

    [Fact]
    public void RevertAll_PutsEveryDriftedValueBack_AsOneStep()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, ComponentPropertiesProbe camera) = scene.MakeLamp();

        string? sourceText = ComponentProperties.Read(camera.Component).Single(p => p.Name == camera.Property).Text;
        ComponentProperties.TryApply(camera.Component, camera.Property, "30", out _);
        lamp.Name = "Beacon";
        lamp.Transform.Position = new Vector3(1f, 2f, 3f);

        scene.Run(new RevertAllPrefabOverridesCommand(lamp.Id, lamp.Name))
            .Should().Be("[editor] reverted 3 override(s) on Beacon.");

        lamp.Name.Should().Be("Lamp");
        lamp.Transform.Position.Should().Be(Vector3.Zero);
        ComponentProperties.Read(camera.Component).Single(p => p.Name == camera.Property).Text
            .Should().Be(sourceText);

        scene.Run(new UndoCommand()).Should().Contain("restored");
        lamp.Name.Should().Be("Beacon", "one undo takes the whole step back");
        lamp.Transform.Position.Should().Be(new Vector3(1f, 2f, 3f));

        scene.Run(new RedoCommand());
        lamp.Name.Should().Be("Lamp");
    }

    [Fact]
    public void RevertAll_WithNothingDrifted_RecordsNothing()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, _) = scene.MakeLamp();

        scene.Run(new RevertAllPrefabOverridesCommand(lamp.Id, lamp.Name)).Should().BeNull();

        scene.Run(new UndoCommand()).Should().Contain("unlinked",
            "the empty revert-all pushed nothing on the history");
    }

    [Fact]
    public void Apply_WritesTheValueIntoTheAsset_AndUndoPutsTheAssetBack()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, ComponentPropertiesProbe camera) = scene.MakeLamp();
        string assetPath = Path.Combine(project.Root, "Lamp.prefab");
        byte[] before = File.ReadAllBytes(assetPath);

        ComponentProperties.TryApply(camera.Component, camera.Property, "30", out _);
        string path = PrefabPaths.Property("Camera", camera.Property);

        scene.Run(new ApplyPrefabOverrideCommand(lamp.Id, path))
            .Should().Be($"[editor] applied {path} to Lamp.prefab.");

        ComponentProperties.Read(scene.Load(assetPath)[0].GetComponent<Camera>()!)
            .Single(p => p.Name == camera.Property).Text.Should().Be("30", "the asset moved to the instance");

        scene.Run(new UndoCommand()).Should().Contain("undid");
        File.ReadAllBytes(assetPath).Should().Equal(before, "Undo puts the asset back byte for byte");

        scene.Run(new RedoCommand()).Should().Contain("applied");
        ComponentProperties.Read(scene.Load(assetPath)[0].GetComponent<Camera>()!)
            .Single(p => p.Name == camera.Property).Text.Should().Be("30");
    }

    [Fact]
    public void ApplyAll_WritesEveryDriftedValue_InOneStep()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, ComponentPropertiesProbe camera) = scene.MakeLamp();
        string assetPath = Path.Combine(project.Root, "Lamp.prefab");
        byte[] before = File.ReadAllBytes(assetPath);

        ComponentProperties.TryApply(camera.Component, camera.Property, "30", out _);
        lamp.Name = "Beacon";

        scene.Run(new ApplyAllPrefabOverridesCommand(lamp.Id, lamp.Name))
            .Should().Be("[editor] applied 2 override(s) on Beacon.");

        GameObject reloaded = scene.Load(assetPath)[0];
        reloaded.Name.Should().Be("Beacon");
        ComponentProperties.Read(reloaded.GetComponent<Camera>()!)
            .Single(p => p.Name == camera.Property).Text.Should().Be("30");

        scene.Run(new UndoCommand()).Should().Contain("undid");
        File.ReadAllBytes(assetPath).Should().Equal(before);
    }

    [Fact]
    public void Apply_WithoutAnAssetToWrite_SaysSo()
    {
        using TemporaryProject project = new();
        Fixture scene = new(project);
        (GameObject lamp, ComponentPropertiesProbe camera) = scene.MakeLamp();

        ComponentProperties.TryApply(camera.Component, camera.Property, "30", out _);
        File.Delete(Path.Combine(project.Root, "Lamp.prefab"));
        File.Delete(Path.Combine(project.Root, "Lamp.prefab.meta"));

        scene.Run(new ApplyPrefabOverrideCommand(lamp.Id, PrefabPaths.Property("Camera", camera.Property)))
            .Should().Contain("the prefab asset is gone");

        scene.Run(new UndoCommand()).Should().Contain("unlinked",
            "the failed apply pushed nothing on the history");
    }

    /// <summary>A name + the one property the tests change on the lamp's camera.</summary>
    /// <param name="Component">The camera.</param>
    /// <param name="Property">Its fov property's display name.</param>
    public sealed record ComponentPropertiesProbe(Component Component, string Property);

    /// <summary>A project, a scene, and the real prefab plumbing — asset on disk, loader for the diff.</summary>
    private sealed class Fixture
    {
        private readonly TemporaryProject _project;

        public Fixture(TemporaryProject project)
        {
            _project = project;
            Live = new Scene(string.Empty, []);
            Dispatcher = new EditorCommandDispatcher(project.Root)
            {
                LoadPrefabAsset = Load
            };
        }

        public Scene Live { get; }

        public EditorCommandDispatcher Dispatcher { get; }

        public EditorHistory History => Dispatcher.History;

        /// <summary>A prefab asset written from a lamp, back in the scene as its first instance.</summary>
        /// <returns>The instance and its camera, probed for the fov property's real name.</returns>
        public (GameObject Lamp, ComponentPropertiesProbe Camera) MakeLamp()
        {
            GameObject lamp = new("Lamp");
            Camera camera = new();
            lamp.AddComponent(camera);
            Live.AddGameObject(lamp);

            string path = Path.Combine(_project.Root, "Lamp.prefab");
            Run(new CreatePrefabFromSelectionCommand(lamp.Id, path))
                .Should().Contain("created prefab Lamp", "the lamp is saved and linked");

            // The one value the tests change: the camera's fov, whatever the inspector calls it.
            string property = ComponentProperties.Read(camera).First().Name;

            return (lamp, new ComponentPropertiesProbe(camera, property));
        }

        /// <summary>The same diff the inspector shows.</summary>
        public IReadOnlyList<PrefabOverrideSnapshot> Diff(GameObject linked) =>
            new PrefabSourceIndex(Load, _project.Root).Diff(linked);

        /// <summary>An asset's objects, read fresh from disk like the session does.</summary>
        public List<GameObject> Load(string path)
        {
            SceneLoader loader = new(null!);
            ComponentDeserializer.RegisterFactories(loader, null!);
            return loader.LoadPrefab(path);
        }

        /// <summary>Queues one command, runs it, and returns the line it logged.</summary>
        public string? Run(IEditorCommand command)
        {
            string? line = null;

            Dispatcher.Enqueue(command);
            Dispatcher.Drain(Live, result => line = result);

            return line;
        }
    }
}
