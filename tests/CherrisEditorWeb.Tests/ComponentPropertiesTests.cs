using AwesomeAssertions;
using Cherris.Components;
using System.Numerics;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers the reflection bridge the inspector edits through: which properties are shown, how each kind is
/// flattened to text, and how that text is written back.
/// </summary>
public class ComponentPropertiesTests
{
    [Fact]
    public void Read_ListsEveryEditablePropertyOfTheComponent()
    {
        IReadOnlyList<PropertySnapshot> properties = ComponentProperties.Read(new Light());

        properties.Select(property => property.Name).Should().Equal(
            nameof(Light.Type),
            nameof(Light.Color),
            nameof(Light.Intensity),
            nameof(Light.AmbientStrength),
            nameof(Light.Range),
            nameof(Light.InnerConeAngle),
            nameof(Light.OuterConeAngle));

        properties.Single(property => property.Name == nameof(Light.Type)).Kind.Should().Be(PropertyKind.Choice);
        properties.Single(property => property.Name == nameof(Light.Intensity)).Kind.Should().Be(PropertyKind.Number);
        properties.Single(property => property.Name == nameof(Light.Color)).IsColor.Should().BeTrue();
    }

    [Fact]
    public void Read_SkipsWhatTheInspectorCannotEdit()
    {
        // Component.GameObject is [HideInInspector]; Camera's matrices are computed, so they have no setter.
        ComponentProperties.Read(new Camera()).Select(property => property.Name)
            .Should().Equal(nameof(Camera.FieldOfView), nameof(Camera.NearClipPlane), nameof(Camera.FarClipPlane));
    }

    [Fact]
    public void Read_StatesTheRangeAPropertyDeclares()
    {
        PropertySnapshot volume = ComponentProperties.Read(new AudioSource(), nameof(AudioSource.Volume))!;

        volume.Minimum.Should().Be(0f);
        volume.Maximum.Should().Be(1f);
        volume.Kind.Should().Be(PropertyKind.Number);
    }

    [Fact]
    public void Read_FlattensChoicesAndVectors()
    {
        Light light = new() { Type = LightType.Spot, Color = new Vector3(0.25f, 0.5f, 1f) };

        ComponentProperties.Read(light, nameof(Light.Type))!.Text.Should().Be("Spot");
        ComponentProperties.Read(light, nameof(Light.Type))!.Choices.Should().Equal("Directional", "Point", "Spot");

        PropertySnapshot color = ComponentProperties.Read(light, nameof(Light.Color))!;
        color.Text.Should().Be("0.25, 0.5, 1");
        color.Components.Should().Equal(0.25f, 0.5f, 1f);
        color.IsColor.Should().BeTrue();
    }

    [Fact]
    public void Apply_WritesEveryKind()
    {
        Light light = new();

        ComponentProperties.TryApply(light, nameof(Light.Type), "Point", out _).Should().BeTrue();
        ComponentProperties.TryApply(light, nameof(Light.Intensity), "2.5", out _).Should().BeTrue();
        ComponentProperties.TryApply(light, nameof(Light.Color), "1, 0.5, 0", out _).Should().BeTrue();

        light.Type.Should().Be(LightType.Point);
        light.Intensity.Should().Be(2.5f);
        light.Color.Should().Be(new Vector3(1f, 0.5f, 0f));
    }

    [Fact]
    public void Apply_WritesTogglesAndStrings()
    {
        AudioSource source = new();

        ComponentProperties.TryApply(source, nameof(AudioSource.Loop), "true", out _).Should().BeTrue();
        ComponentProperties.TryApply(source, nameof(AudioSource.ClipName), "boom", out _).Should().BeTrue();

        source.Loop.Should().BeTrue();
        source.ClipName.Should().Be("boom");
    }

    [Theory]
    [InlineData("Intensity", "bright", "not one")]
    [InlineData("Intensity", "1, 2", "wants 1 number")]
    [InlineData("Color", "1, 2", "wants 3 numbers")]
    [InlineData("Type", "Sunlight", "does not have a value")]
    [InlineData("Nope", "1", "no editable property")]
    public void Apply_ExplainsWhatIsWrong(string property, string text, string reason)
    {
        ComponentProperties.TryApply(new Light(), property, text, out string? error).Should().BeFalse();
        error.Should().Contain(reason);
    }

    [Fact]
    public void Apply_ExplainsAToggleThatIsNotABoolean()
    {
        ComponentProperties.TryApply(new AudioSource(), nameof(AudioSource.Loop), "maybe", out string? error)
            .Should().BeFalse();
        error.Should().Contain("toggle");
    }

    [Fact]
    public void Apply_ClampsToTheDeclaredRange()
    {
        AudioSource source = new();

        ComponentProperties.TryApply(source, nameof(AudioSource.Pitch), "99", out _).Should().BeTrue();

        source.Pitch.Should().Be(3f, "Pitch is declared [Range(0.1, 3)]");
    }

    [Fact]
    public void TryReadText_UsesTheFormatAnEditSendsBack()
    {
        Light light = new() { Intensity = 1.75f };

        ComponentProperties.TryReadText(light, nameof(Light.Intensity), out string text).Should().BeTrue();
        text.Should().Be("1.75");

        ComponentProperties.TryReadText(light, "Nope", out _).Should().BeFalse();
    }
}
