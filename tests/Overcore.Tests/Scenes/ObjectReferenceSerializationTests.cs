using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Scenes;

/// <summary>
/// Covers serialized scene and prefab references (<see cref="GameObject"/>, <see cref="Transform"/>,
/// <see cref="Component"/>/<see cref="Script"/> subclasses, and <see cref="PrefabAsset"/>) across forward/cyclic
/// YAML loads, prefab local IDs, and <see cref="GameObject.Instantiate(GameObject)"/> hierarchy remapping.
/// </summary>
public class ObjectReferenceSerializationTests
{
    private sealed class TurretBrain : Script
    {
        public GameObject? TargetObject { get; set; }
        public Transform? Muzzle { get; set; }
        public Light? MuzzleFlash { get; set; }
        public TurretBrain? Partner { get; set; }
        public PrefabAsset? ProjectilePrefab { get; set; }
    }

    [Fact]
    public void SceneYaml_RoundTripsForwardAndCyclicObjectReferences()
    {
        GameObject turretA = new("TurretA");
        GameObject muzzleA = new("MuzzleA");
        muzzleA.Transform.Parent = turretA.Transform;
        Light flash = muzzleA.AddComponent<Light>();
        flash.Intensity = 4.5f;

        GameObject turretB = new("TurretB");
        GameObject target = new("PlayerTarget");

        TurretBrain brainA = turretA.AddComponent<TurretBrain>();
        TurretBrain brainB = turretB.AddComponent<TurretBrain>();

        // Forward references (turretA appears before turretB and target in YAML) + mutual cycle (A <-> B).
        brainA.TargetObject = target;
        brainA.Muzzle = muzzleA.Transform;
        brainA.MuzzleFlash = flash;
        brainA.Partner = brainB;
        brainA.ProjectilePrefab = PrefabAsset.FromPath("Assets/Prefabs/Rocket.prefab");

        brainB.Partner = brainA;
        brainB.TargetObject = turretA;

        SceneSerializer serializer = new();
        string yaml = serializer.SerializeSceneToString([turretA, muzzleA, turretB, target]);

        yaml.Should().Contain("Object:");
        yaml.Should().Contain("Path: Assets/Prefabs/Rocket.prefab");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        loader.RegisterComponentFactory(
            nameof(TurretBrain),
            ComponentDeserializer.CreateAndPopulateComponent<TurretBrain>);

        List<GameObject> loaded = loader.LoadSceneFromYaml(yaml);
        loaded.Should().HaveCount(4);

        GameObject loadedA = loaded.Single(go => go.Name == "TurretA");
        GameObject loadedMuzzle = loaded.Single(go => go.Name == "MuzzleA");
        GameObject loadedB = loaded.Single(go => go.Name == "TurretB");
        GameObject loadedTarget = loaded.Single(go => go.Name == "PlayerTarget");

        TurretBrain loadedBrainA = loadedA.GetComponent<TurretBrain>()!;
        TurretBrain loadedBrainB = loadedB.GetComponent<TurretBrain>()!;

        loadedBrainA.Should().NotBeNull();
        loadedBrainB.Should().NotBeNull();

        loadedBrainA.TargetObject.Should().BeSameAs(loadedTarget);
        loadedBrainA.Muzzle.Should().BeSameAs(loadedMuzzle.Transform);
        loadedBrainA.MuzzleFlash.Should().BeSameAs(loadedMuzzle.GetComponent<Light>());
        loadedBrainA.Partner.Should().BeSameAs(loadedBrainB);
        loadedBrainB.Partner.Should().BeSameAs(loadedBrainA);
        loadedBrainB.TargetObject.Should().BeSameAs(loadedA);
        loadedBrainA.ProjectilePrefab.Should().NotBeNull();
        loadedBrainA.ProjectilePrefab!.AssetPath.Should().Be("Assets/Prefabs/Rocket.prefab");
    }

    [Fact]
    public void SaveAndLoadPrefab_RemapsInternalReferencesViaLocalFileIds()
    {
        using TempAssets assets = new();

        GameObject rig = new("TurretRig");
        GameObject barrel = new("Barrel");
        barrel.Transform.Parent = rig.Transform;
        Light flash = barrel.AddComponent<Light>();

        TurretBrain brain = rig.AddComponent<TurretBrain>();
        brain.Muzzle = barrel.Transform;
        brain.MuzzleFlash = flash;
        brain.TargetObject = barrel;

        string prefabPath = assets.Path("TurretRig.prefab");
        SceneSerializer serializer = new();
        serializer.SavePrefab(rig, prefabPath);

        string yaml = File.ReadAllText(prefabPath);
        yaml.Should().Contain("Object: 2", "internal prefab references use local File IDs rather than authoring GUIDs");

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        loader.RegisterComponentFactory(
            nameof(TurretBrain),
            ComponentDeserializer.CreateAndPopulateComponent<TurretBrain>);

        GameObject instance1 = loader.LoadPrefab(prefabPath)[0];
        GameObject instance2 = loader.LoadPrefab(prefabPath)[0];

        GameObject barrel1 = instance1.Transform.Children.Single().GameObject;
        GameObject barrel2 = instance2.Transform.Children.Single().GameObject;

        TurretBrain brain1 = instance1.GetComponent<TurretBrain>()!;
        TurretBrain brain2 = instance2.GetComponent<TurretBrain>()!;

        brain1.Muzzle.Should().BeSameAs(barrel1.Transform);
        brain1.MuzzleFlash.Should().BeSameAs(barrel1.GetComponent<Light>());
        brain1.TargetObject.Should().BeSameAs(barrel1);

        brain2.Muzzle.Should().BeSameAs(barrel2.Transform);
        brain2.Muzzle.Should().NotBeSameAs(brain1.Muzzle, "each prefab instance points at its own hierarchy");
    }

    [Fact]
    public void Instantiate_RemapsInternalReferencesToClonedHierarchy_AndPreservesExternalOnes()
    {
        GameObject externalTarget = new("Boss");

        GameObject original = new("Drone");
        GameObject thruster = new("Thruster");
        thruster.Transform.Parent = original.Transform;
        Light thrusterLight = thruster.AddComponent<Light>();

        TurretBrain brain = original.AddComponent<TurretBrain>();
        brain.Muzzle = thruster.Transform;
        brain.MuzzleFlash = thrusterLight;
        brain.TargetObject = externalTarget;

        GameObject clone = GameObject.Instantiate(original);
        GameObject clonedThruster = clone.Transform.Children.Single().GameObject;
        TurretBrain clonedBrain = clone.GetComponent<TurretBrain>()!;

        clonedBrain.Muzzle.Should().BeSameAs(clonedThruster.Transform);
        clonedBrain.MuzzleFlash.Should().BeSameAs(clonedThruster.GetComponent<Light>());
        clonedBrain.TargetObject.Should().BeSameAs(externalTarget, "references outside the cloned hierarchy stay intact");
    }

    [Fact]
    public void PrefabAsset_Instantiate_SpawnsRootAtRequestedPositionAndRotation()
    {
        using TempAssets assets = new();

        GameObject crate = new("Crate");
        crate.AddComponent<Light>().Intensity = 2.25f;

        string prefabPath = assets.Path("Crate.prefab");
        new SceneSerializer().SavePrefab(crate, prefabPath);

        PrefabAsset prefab = PrefabAsset.FromPath(prefabPath);
        Quaternion rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f);
        GameObject? spawned = GameObject.Instantiate(prefab, new Vector3(4f, 1f, -3f), rot);

        spawned.Should().NotBeNull();
        spawned!.Name.Should().Be("Crate");
        spawned.Transform.Position.Should().Be(new Vector3(4f, 1f, -3f));
        spawned.GetComponent<Light>()!.Intensity.Should().Be(2.25f);
    }
}
