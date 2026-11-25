using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Lifecycle;

/// <summary>
/// The runtime lifecycle a script lives through: waking, switching on and off, ticking, being cloned and being
/// destroyed — plus the object and transform helpers that go with it.
/// </summary>
public class ScriptLifecycleTests
{
    /// <summary>Writes every hook it is given into one shared log, tagged with the hook and the object's name.</summary>
    private sealed class LifeProbe : Script
    {
        private readonly List<string> _log;

        public LifeProbe(List<string> log) => _log = log;

        public int Updates { get; private set; }

        private void Record(string hook) => _log.Add($"{hook}:{GameObject?.Name}");

        public override void Awake() => Record("awake");

        public override void OnEnable() => Record("enable");

        public override void OnDisable() => Record("disable");

        public override void OnDestroy() => Record("destroy");

        public override void Start() => Record("start");

        public override void Update(float deltaTime)
        {
            Updates++;
            Record("update");
        }

        public override void LateUpdate() => Record("late");

        public override void FixedUpdate() => Record("fixed");
    }

    private static Scene StartScene(PhysicsSystem physics, AudioSystem audio, params GameObject[] gameObjects)
    {
        Scene scene = new(string.Empty, [.. gameObjects]);
        scene.Start(physics, audio);

        return scene;
    }

    [Fact]
    public void ASceneWakesThenSwitchesOnThenStarts()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        List<string> log = [];
        GameObject thing = new("Thing");
        thing.AddComponent(new LifeProbe(log));

        _ = StartScene(physics, audio, thing);

        log.Should().ContainInOrder("awake:Thing", "enable:Thing", "start:Thing");
    }

    [Fact]
    public void AFrameUpdatesThenRunsLateUpdate()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        List<string> log = [];
        GameObject thing = new("Thing");
        LifeProbe probe = thing.AddComponent(new LifeProbe(log));

        Scene scene = StartScene(physics, audio, thing);
        scene.Update(0.016f);

        probe.Updates.Should().Be(1);
        log.Should().ContainInOrder("update:Thing", "late:Thing");
    }

    [Fact]
    public void SwitchingAnObjectOffStopsItAndSwitchingItOnBringsItBack()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        List<string> log = [];
        GameObject thing = new("Thing");
        LifeProbe probe = thing.AddComponent(new LifeProbe(log));

        Scene scene = StartScene(physics, audio, thing);
        thing.SetActive(false);

        log.Should().Contain("disable:Thing");
        probe.IsActive.Should().BeFalse();
        probe.HasAwoken.Should().BeTrue("an object waking is a one-off; switching it off is not");

        scene.Update(0.016f);
        probe.Updates.Should().Be(0, "a switched-off object does not tick");

        log.Clear();
        thing.SetActive(true);

        log.Should().ContainSingle("the second switching on does not wake or start it again")
            .Which.Should().Be("enable:Thing");

        scene.Update(0.016f);
        probe.Updates.Should().Be(1);
    }

    [Fact]
    public void AChildOfASwitchedOffObjectIsOffWithIt()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        List<string> log = [];
        GameObject parent = new("Parent");
        GameObject child = new("Child");
        child.Transform.Parent = parent.Transform;
        LifeProbe probe = child.AddComponent(new LifeProbe(log));

        _ = StartScene(physics, audio, parent, child);

        probe.IsActive.Should().BeTrue();

        parent.SetActive(false);

        probe.IsActive.Should().BeFalse("the child is not active in the hierarchy any more");
        child.ActiveSelf.Should().BeTrue("the child itself was never switched off");
        child.ActiveInHierarchy.Should().BeFalse();
    }

    [Fact]
    public void DestroyTakesAnObjectAwayAtTheEndOfTheFrame()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        List<string> log = [];
        GameObject thing = new("Thing");
        thing.AddComponent(new LifeProbe(log));

        Scene scene = StartScene(physics, audio, thing);

        GameObject.Destroy(thing);

        scene.GameObjects.Should().Contain(thing, "nothing goes until the frame is over");
        log.Should().NotContain("destroy:Thing");

        scene.Update(0.016f);

        scene.GameObjects.Should().NotContain(thing);
        thing.IsDestroyed.Should().BeTrue();
        log.Should().Contain("destroy:Thing");
    }

    [Fact]
    public void DestroyCanWait()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        List<string> log = [];
        GameObject thing = new("Thing");
        thing.AddComponent(new LifeProbe(log));

        Scene scene = StartScene(physics, audio, thing);

        GameObject.Destroy(thing, 0.05f);

        scene.Update(0.01f);
        scene.GameObjects.Should().Contain(thing);

        scene.Update(0.01f);
        scene.GameObjects.Should().Contain(thing);

        scene.Update(0.05f);
        scene.GameObjects.Should().NotContain(thing);
    }

    [Fact]
    public void InstantiateClonesTheHierarchyAndItsComponents()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        List<string> log = [];
        GameObject original = new("Turret")
        {
            Tag = "Defence",
            Layer = 3
        };
        original.Transform.WorldPosition = new Vector3(1.0f, 2.0f, 3.0f);
        original.AddComponent(new BoxCollider { Size = new Vector3(2.0f, 2.0f, 2.0f) });
        original.AddComponent(new LifeProbe(log));

        GameObject barrel = new("Barrel");
        barrel.Transform.Parent = original.Transform;
        barrel.Transform.Position = new Vector3(0.0f, 1.0f, 0.0f);
        barrel.AddComponent(new Light());

        Scene scene = StartScene(physics, audio, original, barrel);

        int wakesBefore = log.Count(entry => entry.StartsWith("awake:") && entry.EndsWith(":Turret"));

        GameObject clone = GameObject.Instantiate(original);

        clone.Should().NotBeSameAs(original);
        clone.Id.Should().NotBe(original.Id);
        clone.Name.Should().Be("Turret");
        clone.Tag.Should().Be("Defence");
        clone.Layer.Should().Be(3);
        clone.Transform.WorldPosition.Should().Be(new Vector3(1.0f, 2.0f, 3.0f));

        clone.GetComponent<BoxCollider>().Should().NotBeNull("every component is copied");
        clone.GetComponent<LifeProbe>().Should().NotBeSameAs(original.GetComponent<LifeProbe>());

        clone.Transform.Children.Should().ContainSingle();
        clone.Transform.Children[0].GameObject.Name.Should().Be("Barrel");
        clone.Transform.Children[0].GameObject.GetComponent<Light>().Should().NotBeNull();

        scene.GameObjects.Should().Contain(clone, "a clone joins the scene it was made in");
        clone.scene.Should().BeSameAs(scene);

        log.Count(entry => entry.StartsWith("awake:") && entry.EndsWith(":Turret"))
            .Should().BeGreaterThan(wakesBefore, "the clone is woken the moment it appears");
    }

    [Fact]
    public void InstantiateCanPlaceTheClone()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject original = new("Crate");
        original.AddComponent(new BoxCollider());

        _ = StartScene(physics, audio, original);

        GameObject placed = GameObject.Instantiate(original, new Vector3(5.0f, 0.0f, 0.0f), Quaternion.Identity);

        placed.Transform.WorldPosition.Should().Be(new Vector3(5.0f, 0.0f, 0.0f));
    }

    [Fact]
    public void ObjectsAreFoundByNameAndTag()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        GameObject player = new("Player") { Tag = "Player" };
        GameObject enemy = new("Enemy") { Tag = "Enemy" };
        player.AddComponent(new Light());

        _ = StartScene(physics, audio, player, enemy);

        GameObject.Find("Player").Should().BeSameAs(player);
        GameObject.Find("Nothing").Should().BeNull();
        GameObject.FindWithTag("Enemy").Should().BeSameAs(enemy);
        GameObject.FindGameObjectsWithTag("Player").Should().ContainSingle().Which.Should().BeSameAs(player);
        player.CompareTag("Player").Should().BeTrue();
        player.CompareTag("Enemy").Should().BeFalse();
        new GameObject().Tag.Should().Be(GameObject.Untagged);
    }

    [Fact]
    public void ComponentsAreFoundUpAndDownTheHierarchy()
    {
        GameObject root = new("Root");
        GameObject middle = new("Middle");
        GameObject leaf = new("Leaf");
        middle.Transform.Parent = root.Transform;
        leaf.Transform.Parent = middle.Transform;

        Light rootLight = root.AddComponent(new Light());
        Light leafLight = leaf.AddComponent(new Light());

        root.GetComponentInChildren<Light>().Should().BeSameAs(rootLight, "the object itself is searched first");
        middle.GetComponentInChildren<Light>().Should().BeSameAs(leafLight);
        leaf.GetComponentInParent<Light>().Should().BeSameAs(leafLight);
        middle.GetComponentInParent<Light>().Should().BeSameAs(rootLight);
        root.GetComponentsInChildren<Light>().Should().Equal(rootLight, leafLight);
        new GameObject("Loose").GetComponentInChildren<Light>().Should().BeNull();
    }

    [Fact]
    public void TransformHelpersMoveTurnAndReparent()
    {
        GameObject parent = new("Parent");
        parent.Transform.WorldPosition = new Vector3(10.0f, 0.0f, 0.0f);

        GameObject child = new("Child");
        child.Transform.WorldPosition = new Vector3(1.0f, 0.0f, 0.0f);

        child.Transform.SetParent(parent.Transform);

        child.Transform.WorldPosition.Should().Be(new Vector3(1.0f, 0.0f, 0.0f), "the world position is kept");
        child.Transform.Position.Should().Be(new Vector3(-9.0f, 0.0f, 0.0f), "and re-expressed under the parent");

        child.Transform.Translate(new Vector3(0.0f, 1.0f, 0.0f));

        child.Transform.WorldPosition.Should().Be(new Vector3(1.0f, 1.0f, 0.0f));

        GameObject looker = new("Looker");
        looker.Transform.LookAt(new Vector3(0.0f, 0.0f, 10.0f));

        looker.Transform.Forward.Should().Be(new Vector3(0.0f, 0.0f, 1.0f));

        // A quarter turn about +Y takes +Z to +X, so an object facing +Z turns to face +X.
        looker.Transform.Rotate(new Vector3(0.0f, 90.0f, 0.0f));

        looker.Transform.Forward.X.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void TimeIsAdvancedByTheFrameLoop()
    {
        using SceneManager scenes = new();

        GameObject thing = new("Thing");
        Scene scene = new(string.Empty, [thing]);
        scenes.OpenScene(scene);
        scenes.Start();

        float before = Time.ElapsedTime;

        Time.TimeScale = 0.5f;

        try
        {
            scenes.Update(0.2f);

            Time.DeltaTime.Should().BeApproximately(0.1f, 0.0001f, "the clock is scaled");
            Time.ElapsedTime.Should().BeApproximately(before + 0.1f, 0.0001f);
            Time.UnscaledDeltaTime.Should().BeApproximately(0.2f, 0.0001f);

            Time.TimeScale = 0.0f;
            scenes.Update(0.2f);

            Time.DeltaTime.Should().Be(0.0f, "a frozen clock hands the world nothing to move by");
        }
        finally
        {
            Time.Reset();
        }
    }

    [Fact]
    public void FixedUpdateRunsOncePerPhysicsStep()
    {
        using SceneManager scenes = new();

        List<string> log = [];
        GameObject thing = new("Thing");
        thing.AddComponent(new LifeProbe(log));

        Scene scene = new(string.Empty, [thing]);
        scenes.OpenScene(scene);
        scenes.Start();

        log.Clear();

        // Two and a half steps' worth of time: two fixed steps, one frame.
        scenes.Update(2.5f / 60.0f);

        log.Count(entry => entry == "fixed:Thing").Should().Be(2);
        log.Count(entry => entry == "update:Thing").Should().Be(1);
    }
}
