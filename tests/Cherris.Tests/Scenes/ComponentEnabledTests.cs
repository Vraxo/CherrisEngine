using AwesomeAssertions;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Physics;
using Cherris.Serialization;
using Xunit;

namespace Cherris.Tests;

/// <summary>
/// Unity's enable toggle on the engine side: <see cref="Component.Enabled"/> is the author's decision, is saved
/// with the scene, and a component it is off on stops drawing, lighting and ticking — while
/// <see cref="Script.IsSuspended"/>, the editor's hold on a script, is neither.
/// </summary>
public class ComponentEnabledTests
{
    private sealed class CountingScript : Script
    {
        public int Starts { get; private set; }
        public int Updates { get; private set; }

        public override void Start() => Starts++;

        public override void Update(float deltaTime) => Updates++;
    }

    private sealed class ThrowingScript : Script
    {
        public int Updates { get; private set; }

        public override void Update(float deltaTime)
        {
            Updates++;
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void AComponentStartsEnabled()
    {
        new Light().Enabled.Should().BeTrue();
        new CountingScript().Enabled.Should().BeTrue();
    }

    [Fact]
    public void Update_SkipsAScriptThatIsOff_OrOneTheEditorHoldsBack()
    {
        CountingScript off = new() { Enabled = false };
        CountingScript held = new() { IsSuspended = true };
        CountingScript on = new();
        GameObject gameObject = new("ticker");
        gameObject.AddComponent(off);
        gameObject.AddComponent(held);
        gameObject.AddComponent(on);
        Scene scene = new(string.Empty, [gameObject]);

        scene.Update(0.1f);
        scene.Update(0.1f);

        off.Updates.Should().Be(0);
        held.Updates.Should().Be(0);
        on.Updates.Should().Be(2);
    }

    [Fact]
    public void Start_SkipsAScriptThatIsOff_ButStartsOneTheEditorHoldsBack()
    {
        CountingScript off = new() { Enabled = false };
        CountingScript held = new() { IsSuspended = true };
        GameObject gameObject = new("ticker");
        gameObject.AddComponent(off);
        gameObject.AddComponent(held);
        Scene scene = new(string.Empty, [gameObject]);

        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        scene.Start(physics, audio);

        off.Starts.Should().Be(0, "a script the author switched off never starts");
        held.Starts.Should().Be(1, "the scene starts on its way into Play, before the editor lets go of it");
    }

    [Fact]
    public void Update_StartsAScriptThatWasSwitchedOnAfterTheSceneStarted()
    {
        CountingScript late = new() { Enabled = false };
        GameObject gameObject = new("ticker");
        gameObject.AddComponent(late);
        Scene scene = new(string.Empty, [gameObject]);

        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        scene.Start(physics, audio);
        late.Starts.Should().Be(0);

        // The header checkbox, after the scene is open.
        late.Enabled = true;

        scene.Update(0.1f);
        scene.Update(0.1f);

        late.Starts.Should().Be(1, "Start() runs once, on the frame the script is switched on, before its first Update()");
        late.Updates.Should().Be(2);

        late.Enabled = false;
        scene.Update(0.1f);
        late.Enabled = true;
        scene.Update(0.1f);

        late.Starts.Should().Be(1, "switching it off and on again does not start it a second time");
        late.Updates.Should().Be(3);
    }

    [Fact]
    public void Start_RunsEachScriptsStartOnce()
    {
        CountingScript script = new();
        GameObject gameObject = new("ticker");
        gameObject.AddComponent(script);
        Scene scene = new(string.Empty, [gameObject]);

        using PhysicsSystem physics = new();
        using AudioSystem audio = new();
        scene.Start(physics, audio);
        scene.Start(physics, audio);

        script.Starts.Should().Be(1, "a second Start() on the same scene, as a resume from pause does, starts nothing twice");
    }

    [Fact]
    public void Update_InASceneThatWasNeverStarted_StartsNothing()
    {
        CountingScript script = new();
        GameObject gameObject = new("ticker");
        gameObject.AddComponent(script);
        Scene scene = new(string.Empty, [gameObject]);

        scene.Update(0.1f);

        // The desktop editor's edit mode: the scene was never started, so there are no systems to start with.
        script.Starts.Should().Be(0);
        script.Updates.Should().Be(1);
    }

    [Fact]
    public void AScriptThatThrows_IsHeldBack_ButStaysEnabled()
    {
        ThrowingScript script = new();
        GameObject gameObject = new("ticker");
        gameObject.AddComponent(script);
        Scene scene = new(string.Empty, [gameObject]);

        scene.Update(0.1f);
        scene.Update(0.1f);

        script.Updates.Should().Be(1, "the second frame does not run a script that threw on the first");
        script.IsSuspended.Should().BeTrue();
        script.Enabled.Should().BeTrue("a throw is the engine's doing, not the author's decision");
    }

    [Fact]
    public void TheMainCameraAndTheLights_SkipAComponentThatIsOff()
    {
        GameObject cameraObject = new("Camera");
        cameraObject.AddComponent(new Camera { Enabled = false });
        GameObject lamp = new("Lamp");
        Light dim = lamp.AddComponent(new Light { Enabled = false });
        Light lit = lamp.AddComponent(new Light());
        Scene scene = new(string.Empty, [cameraObject, lamp]);

        scene.FindMainComponents();

        scene.MainCamera.Should().BeNull("a camera that is switched off is not the one the scene renders from");
        scene.Lights.Should().Contain(lit).And.NotContain(dim);
    }

    [Fact]
    public void ASavedScene_KeepsAToggledComponentOff_AndSaysNothingAboutOneThatIsOn()
    {
        GameObject sun = new("Sun");
        sun.AddComponent(new Light { Enabled = false });
        GameObject cameraObject = new("Camera");
        cameraObject.AddComponent(new Camera());
        SceneSerializer serializer = new();

        string yaml = serializer.SerializeSceneToString([sun, cameraObject]);

        yaml.Should().Contain("Enabled: false");
        serializer.SerializeSceneToString([cameraObject]).Should().NotContain("Enabled", "the default is left out, as every default is");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        List<GameObject> loaded = loader.LoadSceneFromYaml(yaml);

        loaded[0].GetComponent<Light>()!.Enabled.Should().BeFalse();
        loaded[1].GetComponent<Camera>()!.Enabled.Should().BeTrue();
    }
}
