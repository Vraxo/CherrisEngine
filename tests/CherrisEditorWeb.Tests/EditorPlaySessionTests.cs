using AwesomeAssertions;
using Cherris.Components;
using Cherris.Core;
using System.Numerics;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers the transport: what play, pause, stop and restart do to a scene that is already started, and what
/// stop puts back. The world only advances while the transport is playing, which is the whole point.
/// </summary>
public class EditorPlaySessionTests
{
    [Fact]
    public void NewSession_IsEditing_AndDoesNotTick()
    {
        (SceneManager scenes, Counter counter) = OpenCountingScene();
        EditorPlaySession play = new(scenes);

        play.State.Should().Be(EditorPlayState.Editing);
        play.IsRunning.Should().BeFalse();

        play.Tick(0.016f);

        counter.Ticks.Should().Be(0, "an edited scene stands still");
    }

    [Fact]
    public void Play_RunsTheScene_AndStop_EndsIt()
    {
        (SceneManager scenes, Counter counter) = OpenCountingScene();
        EditorPlaySession play = new(scenes);

        play.Play().Should().BeTrue();
        play.Tick(0.016f);
        play.Tick(0.016f);
        counter.Ticks.Should().Be(2);

        play.Stop().Should().BeTrue();
        play.State.Should().Be(EditorPlayState.Editing);

        play.Tick(0.016f);
        counter.Ticks.Should().Be(2, "a stopped scene does not tick");
    }

    [Fact]
    public void Pause_FreezesTheScene_AndPlayResumesIt()
    {
        (SceneManager scenes, Counter counter) = OpenCountingScene();
        EditorPlaySession play = new(scenes);

        play.Play().Should().BeTrue();
        play.Tick(0.016f);
        counter.Ticks.Should().Be(1);

        play.Pause().Should().BeTrue();
        play.State.Should().Be(EditorPlayState.Paused);
        play.Tick(0.016f);
        play.Tick(0.016f);
        counter.Ticks.Should().Be(1, "a paused scene does not tick");

        play.Play().Should().BeTrue();
        play.Tick(0.016f);
        counter.Ticks.Should().Be(2, "playing after a pause carries on");

        play.State.Should().Be(EditorPlayState.Playing);
    }

    [Fact]
    public void Play_Twice_IsIgnored()
    {
        (SceneManager scenes, _) = OpenCountingScene();
        EditorPlaySession play = new(scenes);

        play.Play().Should().BeTrue();
        play.Play().Should().BeFalse("it is already playing");
        play.State.Should().Be(EditorPlayState.Playing);
    }

    [Fact]
    public void PauseAndStop_WhileEditing_AreIgnored()
    {
        (SceneManager scenes, _) = OpenScene(new GameObject("Cube"));
        EditorPlaySession play = new(scenes);
        string before = Describe(scenes);

        play.Pause().Should().BeFalse();
        play.Stop().Should().BeFalse();
        play.State.Should().Be(EditorPlayState.Editing);
        Describe(scenes).Should().Be(before, "nothing ran, so nothing changed");
    }

    [Fact]
    public void Play_WithNoScene_IsRefused()
    {
        List<string> log = [];
        EditorPlaySession play = new(new SceneManager(), log.Add);

        play.Play().Should().BeFalse();
        play.State.Should().Be(EditorPlayState.Editing);
        log.Should().ContainSingle().Which.Should().Contain("no scene");
    }

    [Fact]
    public void Stop_PutsNamesTransformsAndPropertiesBack()
    {
        GameObject cube = new("Cube");
        cube.Transform.Position = new Vector3(1f, 2f, 3f);
        cube.Transform.Scale = new Vector3(2f);
        Light light = cube.AddComponent(new Light { Intensity = 2.5f });
        (SceneManager scenes, _) = OpenScene(cube);
        EditorPlaySession play = new(scenes);

        play.Play();

        // What a script, a physics step or a live edit can do while the scene runs.
        cube.Name = "Cube (running)";
        cube.Transform.Position = new Vector3(9f, 9f, 9f);
        cube.Transform.Scale = Vector3.One;
        light.Intensity = 7f;

        play.Stop();

        cube.Name.Should().Be("Cube");
        cube.Transform.Position.Should().Be(new Vector3(1f, 2f, 3f));
        cube.Transform.Scale.Should().Be(new Vector3(2f));
        light.Intensity.Should().Be(2.5f);
    }

    [Fact]
    public void Stop_RemovesObjectsThatPlayCreated()
    {
        GameObject cube = new("Cube");
        (SceneManager scenes, Scene scene) = OpenScene(cube);
        EditorPlaySession play = new(scenes);

        play.Play();

        scenes.AddGameObject(new GameObject("Spawned"));

        play.Stop();

        scene.GameObjects.Should().ContainSingle().Which.Should().BeSameAs(cube);
    }

    [Fact]
    public void Stop_PutsBackAnObjectThatPlayRemoved()
    {
        GameObject cube = new("Cube");
        (SceneManager scenes, Scene scene) = OpenScene(cube);
        EditorPlaySession play = new(scenes);

        play.Play();
        scenes.RemoveGameObject(cube);
        scene.GameObjects.Should().BeEmpty();

        play.Stop();

        scene.GameObjects.Should().ContainSingle().Which.Should().BeSameAs(cube);
        play.State.Should().Be(EditorPlayState.Editing);
    }

    [Fact]
    public void Stop_TakesAddedComponentsAway_AndPutsRemovedOnesBack()
    {
        GameObject cube = new("Cube");
        Light light = cube.AddComponent(new Light { Intensity = 3f });
        (SceneManager scenes, _) = OpenScene(cube);
        EditorPlaySession play = new(scenes);

        play.Play();

        cube.RemoveComponent(light);
        cube.AddComponent(new Camera());

        play.Stop();

        cube.Components.Should().ContainSingle().Which.Should().BeSameAs(light);
        cube.GetComponent<Camera>().Should().BeNull("play added it, so stop takes it away");
        light.Intensity.Should().Be(3f);
    }

    [Fact]
    public void Stop_PutsParentingBack()
    {
        GameObject parent = new("Parent");
        GameObject child = new("Child");
        child.Transform.Parent = parent.Transform;
        (SceneManager scenes, _) = OpenScene(parent, child);
        EditorPlaySession play = new(scenes);

        play.Play();

        child.Transform.Parent = null;
        parent.Transform.Position = new Vector3(5f, 0f, 0f);

        play.Stop();

        child.Transform.Parent.Should().BeSameAs(parent.Transform);
        parent.Transform.Position.Should().Be(Vector3.Zero);
        parent.Transform.Children.Should().ContainSingle().Which.Should().BeSameAs(child.Transform);
    }

    [Fact]
    public void Restart_GoesBackToTheAuthoredValues_AndKeepsPlaying()
    {
        GameObject cube = new("Cube");
        cube.Transform.Position = new Vector3(0f, 1f, 0f);
        (SceneManager scenes, _) = OpenScene(cube);
        EditorPlaySession play = new(scenes);

        play.Play();
        cube.Transform.Position = new Vector3(4f, 4f, 4f);

        play.Restart().Should().BeTrue();

        play.State.Should().Be(EditorPlayState.Playing, "restart plays again");
        cube.Transform.Position.Should().Be(new Vector3(0f, 1f, 0f));

        // The snapshot is still the authored one, so the next restart undoes the next run as well.
        cube.Transform.Position = new Vector3(-2f, 0f, 0f);
        play.Restart();
        cube.Transform.Position.Should().Be(new Vector3(0f, 1f, 0f));
    }

    [Fact]
    public void Restart_WhileEditing_IsJustAPlay()
    {
        (SceneManager scenes, _) = OpenCountingScene();
        EditorPlaySession play = new(scenes);

        play.Restart().Should().BeTrue();

        play.State.Should().Be(EditorPlayState.Playing);
        play.HasSnapshot.Should().BeTrue();
    }

    [Fact]
    public void Restart_WhilePaused_GoesBackAndPlaysAgain()
    {
        GameObject cube = new("Cube");
        (SceneManager scenes, _) = OpenScene(cube);
        EditorPlaySession play = new(scenes);

        play.Play();
        play.Pause();
        cube.Transform.Position = new Vector3(3f, 0f, 0f);

        play.Restart();

        play.State.Should().Be(EditorPlayState.Playing);
        cube.Transform.Position.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void Stop_DoesNotWriteASnapshotIntoAnotherScene()
    {
        GameObject first = new("First");
        GameObject second = new("Second");
        SceneManager scenes = new();
        scenes.OpenScene(new Scene(string.Empty, [first]));
        EditorPlaySession play = new(scenes);

        play.Play();
        first.Transform.Position = new Vector3(1f, 1f, 1f);

        // Opening another scene throws the snapshot away rather than writing it into the new scene.
        scenes.OpenScene(new Scene(string.Empty, [second]));
        second.Transform.Position = new Vector3(2f, 2f, 2f);

        play.Stop();

        second.Transform.Position.Should().Be(new Vector3(2f, 2f, 2f));
        play.State.Should().Be(EditorPlayState.Editing);
    }

    [Fact]
    public void Apply_DispatchesEachRequest()
    {
        (SceneManager scenes, _) = OpenCountingScene();
        EditorPlaySession play = new(scenes);

        play.Apply(PlaybackRequest.Play).Should().BeTrue();
        play.Apply(PlaybackRequest.Pause).Should().BeTrue();
        play.Apply(PlaybackRequest.Restart).Should().BeTrue();
        play.Apply(PlaybackRequest.Stop).Should().BeTrue();

        play.State.Should().Be(EditorPlayState.Editing);
        play.HasSnapshot.Should().BeFalse("stop throws the snapshot away");
    }

    private static (SceneManager Scenes, Scene Scene) OpenScene(params GameObject[] gameObjects)
    {
        SceneManager scenes = new();
        Scene scene = new(string.Empty, [.. gameObjects]);
        scenes.OpenScene(scene);

        return (scenes, scene);
    }

    /// <summary>A scene whose one object carries a script that counts the ticks it gets.</summary>
    private static (SceneManager Scenes, Counter Counter) OpenCountingScene()
    {
        Counter counter = new();
        GameObject ticker = new("Ticker");
        ticker.AddComponent(counter);

        (SceneManager scenes, _) = OpenScene(ticker);

        return (scenes, counter);
    }

    /// <summary>Name, transform and properties of every object, to prove that a no-op stayed a no-op.</summary>
    private static string Describe(SceneManager scenes)
    {
        System.Text.StringBuilder text = new();

        foreach (GameObject gameObject in scenes.ActiveScene!.GameObjects)
        {
            text.Append(gameObject.Name).Append(gameObject.Transform.Position).Append(gameObject.Transform.Scale);

            foreach (Component component in gameObject.Components)
            {
                foreach (PropertySnapshot property in ComponentProperties.Read(component))
                {
                    text.Append(property.Name).Append('=').Append(property.Text);
                }
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// A script that only counts how often the engine ticks it. The count is a field on purpose: the snapshot
    /// captures public properties with a setter, private ones included, so a property here would be put back to
    /// its captured value by a stop and the test could not tell ticking from restoring.
    /// </summary>
    private sealed class Counter : Script
    {
        public int Ticks;

        public override void Update(float deltaTime) => Ticks++;
    }
}
