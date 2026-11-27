using AwesomeAssertions;
using Overcore.Components;
using Overcore.Rendering.Shadows;
using System.Numerics;
using Xunit;

namespace OvercoreEditorSession.Tests;

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
            nameof(Light.OuterConeAngle),

            // The shadow settings, appended in declaration order. Listing them here rather than loosening the
            // assertion is what makes the inspector's row order a decision instead of a consequence.
            nameof(Light.Shadows),
            nameof(Light.ShadowStrength),
            nameof(Light.ShadowBias),
            nameof(Light.ShadowNormalBias),
            nameof(Light.ShadowNearPlane),
            nameof(Light.ShadowResolution));

        properties.Single(property => property.Name == nameof(Light.Type)).Kind.Should().Be(PropertyKind.Choice);
        properties.Single(property => property.Name == nameof(Light.Intensity)).Kind.Should().Be(PropertyKind.Number);
        properties.Single(property => property.Name == nameof(Light.Color)).IsColor.Should().BeTrue();
    }

    [Fact]
    public void ReadAndApply_ExposeUiLayoutAndGraphicPropertiesToTheGenericInspector()
    {
        RectTransform rect = new();
        string[] rectProperties = ComponentProperties.Read(rect).Select(property => property.Name).ToArray();
        foreach (string propertyName in new[]
        {
            nameof(RectTransform.AnchorMin), nameof(RectTransform.AnchorMax), nameof(RectTransform.AnchoredPosition),
            nameof(RectTransform.SizeDelta), nameof(RectTransform.Pivot)
        })
        {
            rectProperties.Should().Contain(propertyName);
        }
        ComponentProperties.TryApply(rect, nameof(RectTransform.AnchorMin), "0.25, 0.5", out _).Should().BeTrue();
        rect.AnchorMin.Should().Be(new Vector2(0.25f, 0.5f));

        Image image = new();
        string[] imageProperties = ComponentProperties.Read(image).Select(property => property.Name).ToArray();
        foreach (string propertyName in new[]
        {
            nameof(Image.TextureName), nameof(Image.UVMin), nameof(Image.UVMax), nameof(Image.SliceLeft),
            nameof(Image.SliceTop), nameof(Image.SliceRight), nameof(Image.SliceBottom), nameof(Image.Color)
        })
        {
            imageProperties.Should().Contain(propertyName);
        }
        ComponentProperties.TryApply(image, nameof(Image.SliceLeft), "0.15", out _).Should().BeTrue();
        image.SliceLeft.Should().Be(0.15f);
    }

    [Fact]
    public void ReadAndApply_ExposeTheRemainingUiControlsToTheGenericInspector()
    {
        Canvas canvas = new();
        ComponentProperties.Read(canvas).Select(property => property.Name).Should().Contain(nameof(Canvas.ScaleMode));
        ComponentProperties.TryApply(canvas, nameof(Canvas.ScaleMode), nameof(CanvasScaleMode.ConstantPixelSize), out _).Should().BeTrue();
        canvas.ScaleMode.Should().Be(CanvasScaleMode.ConstantPixelSize);

        Text text = new();
        ComponentProperties.TryApply(text, nameof(Text.Content), "Ready", out _).Should().BeTrue();
        text.Content.Should().Be("Ready");
        ComponentProperties.TryApply(text, nameof(Text.FontName), "Assets/Fonts/Display.ttf", out _).Should().BeTrue();
        text.FontName.Should().Be("Assets/Fonts/Display.ttf");

        Button button = new();
        ComponentProperties.TryApply(button, nameof(Button.Interactable), "false", out _).Should().BeTrue();
        button.Interactable.Should().BeFalse();

        Toggle toggle = new();
        ComponentProperties.TryApply(toggle, nameof(Toggle.IsOn), "true", out _).Should().BeTrue();
        toggle.IsOn.Should().BeTrue();

        Slider slider = new();
        ComponentProperties.TryApply(slider, nameof(Slider.Direction), nameof(SliderDirection.Vertical), out _).Should().BeTrue();
        slider.Direction.Should().Be(SliderDirection.Vertical);

        InputField inputField = new();
        ComponentProperties.TryApply(inputField, nameof(InputField.Placeholder), "Enter name", out _).Should().BeTrue();
        inputField.Placeholder.Should().Be("Enter name");

        HorizontalLayoutGroup layout = new();
        ComponentProperties.TryApply(layout, nameof(HorizontalLayoutGroup.Spacing), "12", out _).Should().BeTrue();
        layout.Spacing.Should().Be(12f);
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
    public void Read_FlattensAListOfNumbers()
    {
        // The kind the split list needs, and the reason the inspector gained one: its length is data, so there is no
        // arity to build a row from.
        Shadows shadows = new() { Splits = [0.1f, 0.25f, 0.55f] };

        PropertySnapshot splits = ComponentProperties.Read(shadows, nameof(Shadows.Splits))!;

        splits.Kind.Should().Be(PropertyKind.Numbers);
        splits.Text.Should().Be("0.1, 0.25, 0.55");
        splits.Components.Should().Equal(0.1f, 0.25f, 0.55f);
        splits.Choices.Should().BeEmpty();
    }

    [Fact]
    public void Apply_WritesAListOfNumbers()
    {
        Shadows shadows = new();

        ComponentProperties.TryApply(shadows, nameof(Shadows.Splits), "0.2, 0.5, 0.8", out _).Should().BeTrue();

        shadows.Splits.Should().Equal(0.2f, 0.5f, 0.8f);
    }

    [Fact]
    public void Apply_ReadsAnEmptyFieldAsTheEmptyList()
    {
        // One cascade has no boundaries, and clearing the field is how a person says so. Treating the empty field as a
        // parse error would leave the author with no way to express the value the component supports.
        Shadows shadows = new() { Splits = [0.2f, 0.5f, 0.8f] };

        ComponentProperties.TryApply(shadows, nameof(Shadows.Splits), "", out _).Should().BeTrue();

        shadows.Splits.Should().BeEmpty();
    }

    [Fact]
    public void Apply_RefusesAListWithAWordInIt()
    {
        Shadows shadows = new() { Splits = [0.2f, 0.5f, 0.8f] };

        ComponentProperties.TryApply(shadows, nameof(Shadows.Splits), "0.2, soon, 0.8", out string? error)
            .Should().BeFalse();

        error.Should().Contain("list of numbers");
        shadows.Splits.Should().Equal(0.2f, 0.5f, 0.8f);
    }

    [Fact]
    public void Read_ListsTheShadowsSettingsAndNotOnlyTheirCount()
    {
        // Both front ends walk the same editable set, so this is the assertion that a Shadows component is tunable at
        // all — it is not, if the properties are hidden from the reflection walk.
        ComponentProperties.Read(new Shadows()).Select(property => property.Name)
            .Should().Equal(nameof(Shadows.Distance), nameof(Shadows.Cascades), nameof(Shadows.Splits));
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

    [Theory]
    [InlineData("0.01", "a plain float drags 0.01 a pixel, as DragFloat does")]
    [InlineData("1", "an integer drags a whole step a pixel, as DragInt does")]
    [InlineData("0.1", "one vector component drags 0.1 a pixel, as DragFloat3 does")]
    public void DragSpeed_MatchesTheImGuiWidgetThatPropertyWouldUse(string expected, string because)
    {
        PropertySnapshot snapshot = expected switch
        {
            "0.01" => new PropertySnapshot("Value", PropertyKind.Number, "1", [1f], []),
            "1" => new PropertySnapshot("Count", PropertyKind.Integer, "1", [1f], []),
            _ => new PropertySnapshot("Offset", PropertyKind.Vector3, "1, 2, 3", [1f, 2f, 3f], [])
        };

        snapshot.DragSpeed.Should().Be(Single.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), because);
    }

    [Fact]
    public void DragSpeed_SpreadsABoundedRangeOverTwoHundredPixels()
    {
        // Volume is [Range(0, 1)], so a drag crosses it in 200 pixels rather than in ten.
        ComponentProperties.Read(new AudioSource(), nameof(AudioSource.Volume))!.DragSpeed.Should().Be(0.005f);

        // Light's intensity is [Range(0, float.MaxValue)]: that is a clamp, not a slider, so it drags slowly.
        ComponentProperties.Read(new Light(), nameof(Light.Intensity))!.DragSpeed.Should().Be(0.01f);
    }

    [Fact]
    public void DragSpeed_StepsAColourComponentByOneOverTwoFiftyFive()
    {
        PropertySnapshot colour = ComponentProperties.Read(new Light(), nameof(Light.Color))!;

        colour.IsColor.Should().BeTrue();
        colour.DragSpeed.Should().Be(1f / 255f, "ImGui's ColorEdit3 drags a channel in 8-bit steps");
    }

    [Fact]
    public void TryGetRange_ReportsWhetherBothBoundsAreDeclared()
    {
        PropertySnapshot volume = ComponentProperties.Read(new AudioSource(), nameof(AudioSource.Volume))!;

        volume.TryGetRange(out float minimum, out float maximum).Should().BeTrue();
        minimum.Should().Be(0f);
        maximum.Should().Be(1f);

        PropertySnapshot none = new("Offset", PropertyKind.Vector3, "1, 2, 3", [1f, 2f, 3f], []);
        none.TryGetRange(out _, out _).Should().BeFalse();
        none.HasBoundedRange.Should().BeFalse();
    }

    [Fact]
    public void TryReadText_UsesTheFormatAnEditSendsBack()
    {
        Light light = new() { Intensity = 1.75f };

        ComponentProperties.TryReadText(light, nameof(Light.Intensity), out string text).Should().BeTrue();
        text.Should().Be("1.75");

        ComponentProperties.TryReadText(light, "Nope", out _).Should().BeFalse();
    }

    [Fact]
    public void Defaults_AreTheValuesANewComponentStartsWith()
    {
        Light fresh = new();
        IReadOnlyDictionary<string, string> defaults = ComponentProperties.Defaults(typeof(Light));

        defaults.Keys.Should().BeEquivalentTo(
            ComponentProperties.Read(fresh).Select(property => property.Name),
            "every property the inspector shows offers the value a reset would restore");

        defaults[nameof(Light.Intensity)].Should().Be("1");
        defaults[nameof(Light.Type)].Should().Be("Directional");
        defaults[nameof(Light.Color)].Should().Be("1, 1, 1");
        defaults[nameof(Light.AmbientStrength)].Should().Be("0.3");
    }

    [Fact]
    public void CanReset_IsTrueOnlyAwayFromTheDefault()
    {
        Light light = new() { Intensity = 3.5f, Type = LightType.Spot };

        PropertySnapshot changed = ComponentProperties.Read(light, nameof(Light.Intensity))!;
        changed.DefaultText.Should().Be("1");
        changed.CanReset.Should().BeTrue();

        ComponentProperties.Read(light, nameof(Light.Type))!.CanReset.Should().BeTrue();

        PropertySnapshot untouched = ComponentProperties.Read(light, nameof(Light.AmbientStrength))!;
        untouched.CanReset.Should().BeFalse("0.3 is where a new Light starts");
    }

    [Fact]
    public void CanReset_SurvivesAFormattingRoundTrip()
    {
        // 1.0f and 1 are the same value and the same text, so a property set to its default by hand is still
        // "at the default": the snapshot compares the text the field shows, not the object it came from.
        Light light = new() { Intensity = 1f, Color = new Vector3(1f, 1f, 1f) };

        ComponentProperties.Read(light, nameof(Light.Intensity))!.CanReset.Should().BeFalse();
        ComponentProperties.Read(light, nameof(Light.Color))!.CanReset.Should().BeFalse();

        light.Intensity = 2f;
        ComponentProperties.Read(light, nameof(Light.Intensity))!.CanReset.Should().BeTrue();
    }

    [Fact]
    public void TryReadDefaultText_ReportsAPropertyWithNoDefault()
    {
        ComponentProperties.TryReadDefaultText(typeof(Light), nameof(Light.Intensity), out string text)
            .Should().BeTrue();
        text.Should().Be("1");

        ComponentProperties.TryReadDefaultText(typeof(Light), "Nope", out _).Should().BeFalse();
    }
}
