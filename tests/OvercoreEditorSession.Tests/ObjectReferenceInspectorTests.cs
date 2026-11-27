using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using System.Numerics;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Covers how <see cref="ComponentProperties"/> exposes and writes serialized object/prefab references,
/// <see cref="Vector4"/>, and <see cref="LayerMask"/> in the session-backed Photino/Blazor inspector.
/// </summary>
public class ObjectReferenceInspectorTests
{
    private sealed class ReferenceHolder : Script
    {
        public GameObject? TargetObject { get; set; }
        public Transform? LookAt { get; set; }
        public Light? LinkedLight { get; set; }
        public PrefabAsset? SpawnPrefab { get; set; }
        public Vector4 Tint { get; set; } = new(1f, 0.5f, 0.25f, 1f);
        public LayerMask Mask { get; set; } = LayerMask.Everything;
    }

    [Fact]
    public void Read_ExposesSceneChoicesAndLabelsForGameObjectTransformAndComponentReferences()
    {
        GameObject actor = new("Turret");
        ReferenceHolder holder = actor.AddComponent<ReferenceHolder>();

        GameObject lamp = new("Lamp");
        Light lampLight = lamp.AddComponent<Light>();

        GameObject crate = new("Crate"); // Has no Light component -> excluded from LinkedLight choices!

        Scene scene = new(string.Empty, [actor, lamp, crate]);
        _ = scene;

        IReadOnlyList<PropertySnapshot> props = ComponentProperties.Read(holder);

        PropertySnapshot targetProp = props.Single(p => p.Name == nameof(ReferenceHolder.TargetObject));
        targetProp.Kind.Should().Be(PropertyKind.ObjectReference);
        targetProp.Text.Should().BeEmpty("null reference serializes as empty string (None)");
        targetProp.Choices.Should().Contain($"obj:{lamp.Id:D}").And.Contain($"obj:{crate.Id:D}");
        targetProp.ChoiceLabels.Should().Contain("None (GameObject)").And.Contain("Lamp");

        PropertySnapshot lightProp = props.Single(p => p.Name == nameof(ReferenceHolder.LinkedLight));
        lightProp.Kind.Should().Be(PropertyKind.ObjectReference);
        lightProp.Choices.Should().Contain($"obj:{lamp.Id:D}:Light");
        lightProp.Choices.Should().NotContain(c => c.Contains(crate.Id.ToString("D")), "Crate has no Light component");

        // Assigning via TryApply resolves live instances and clears back to null on empty string.
        ComponentProperties.TryApply(holder, nameof(ReferenceHolder.TargetObject), $"obj:{crate.Id:D}", out _).Should().BeTrue();
        holder.TargetObject.Should().BeSameAs(crate);

        ComponentProperties.TryApply(holder, nameof(ReferenceHolder.LookAt), $"obj:{lamp.Id:D}", out _).Should().BeTrue();
        holder.LookAt.Should().BeSameAs(lamp.Transform);

        ComponentProperties.TryApply(holder, nameof(ReferenceHolder.LinkedLight), $"obj:{lamp.Id:D}", out _).Should().BeTrue();
        holder.LinkedLight.Should().BeSameAs(lampLight);

        ComponentProperties.TryApply(holder, nameof(ReferenceHolder.SpawnPrefab), "Assets/Prefabs/Bullet.prefab", out _).Should().BeTrue();
        holder.SpawnPrefab.Should().NotBeNull();
        holder.SpawnPrefab!.AssetPath.Should().Be("Assets/Prefabs/Bullet.prefab");

        // Reset to default sets object reference back to null.
        PropertySnapshot assignedLightSnap = ComponentProperties.Read(holder, nameof(ReferenceHolder.LinkedLight))!;
        assignedLightSnap.CanReset.Should().BeTrue();
        ComponentProperties.TryApply(holder, nameof(ReferenceHolder.LinkedLight), assignedLightSnap.DefaultText!, out _).Should().BeTrue();
        holder.LinkedLight.Should().BeNull();
    }

    [Fact]
    public void ReadAndApply_SupportsVector4AndLayerMaskProperties()
    {
        GameObject go = new("Probe");
        ReferenceHolder holder = go.AddComponent<ReferenceHolder>();

        IReadOnlyList<PropertySnapshot> props = ComponentProperties.Read(holder);
        props.Single(p => p.Name == nameof(ReferenceHolder.Tint)).Kind.Should().Be(PropertyKind.Vector4);
        props.Single(p => p.Name == nameof(ReferenceHolder.Mask)).Kind.Should().Be(PropertyKind.Integer);

        ComponentProperties.TryApply(holder, nameof(ReferenceHolder.Tint), "0.1, 0.2, 0.3, 0.8", out _).Should().BeTrue();
        holder.Tint.Should().Be(new Vector4(0.1f, 0.2f, 0.3f, 0.8f));

        ComponentProperties.TryApply(holder, nameof(ReferenceHolder.Mask), "5", out _).Should().BeTrue();
        holder.Mask.Value.Should().Be(5);
    }

    [Fact]
    public void BuiltInCatalog_RegistersParticleSystemAndNavigationComponents()
    {
        IReadOnlyList<ComponentCatalog.Entry> entries = ComponentCatalog.BuiltIn;
        entries.Select(e => e.ComponentType).Should().Contain([
            typeof(ParticleSystem),
            typeof(NavMeshSurface),
            typeof(NavMeshAgent),
            typeof(NavMeshObstacle)
        ]);
    }
}
