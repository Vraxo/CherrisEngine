using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.UI;

public class UISerializationTests
{
    [Fact]
    public void UIComponentPropertiesRoundTripThroughSceneYaml()
    {
        GameObject canvasObject = new("Canvas");
        canvasObject.AddComponent(new RectTransform());
        canvasObject.AddComponent(new Canvas
        {
            ReferenceWidth = 1280f,
            ReferenceHeight = 720f,
            MatchWidthOrHeight = 0.25f,
            SortingOrder = 3
        });
        canvasObject.AddComponent(new RectMask2D
        {
            CornerRadius = 14f,
            Feather = 5f,
            MaskTextureName = "Assets/UI/alpha.png",
            MaskThreshold = 0.42f
        });

        GameObject panel = new("Panel");
        panel.Transform.Parent = canvasObject.Transform;
        panel.AddComponent(new RectTransform
        {
            AnchorMin = new Vector2(0f, 1f),
            AnchorMax = new Vector2(0f, 1f),
            Pivot = new Vector2(0f, 1f),
            AnchoredPosition = new Vector2(16f, -20f),
            SizeDelta = new Vector2(320f, 64f)
        });
        panel.AddComponent(new Image
        {
            SpriteName = "Assets/UI/panel.sprite.json",
            TextureName = "Assets/UI/panel.png",
            UVMin = new Vector2(0.1f, 0.2f),
            UVMax = new Vector2(0.8f, 0.9f),
            SliceLeft = 0.12f,
            SliceTop = 0.18f,
            SliceRight = 0.14f,
            SliceBottom = 0.2f,
            Color = new Vector3(0.2f, 0.4f, 0.8f),
            Alpha = 0.75f
        });
        panel.AddComponent(new Text
        {
            Content = "Continue",
            FontName = "Assets/Fonts/Display.ttf",
            FontSize = 30f,
            Wrap = false,
            Color = new Vector3(0.9f, 0.8f, 0.7f),
            HorizontalAlignment = HorizontalTextAlignment.Center,
            VerticalAlignment = VerticalTextAlignment.Bottom,
            Direction = TextDirection.RightToLeft
        });
        panel.AddComponent(new Button { Interactable = false });
        panel.AddComponent(new Toggle
        {
            IsOn = true,
            Interactable = false,
            OnColor = new Vector3(0.2f, 0.8f, 0.3f),
            OffColor = new Vector3(0.3f, 0.3f, 0.3f)
        });
        panel.AddComponent(new Slider
        {
            MinValue = -2f,
            MaxValue = 8f,
            Value = 3f,
            WholeNumbers = true,
            Direction = SliderDirection.Vertical,
            TrackThickness = 4f,
            HandleSize = 22f
        });
        panel.AddComponent(new InputField
        {
            Value = "Player One",
            Placeholder = "Enter name",
            PlaceholderColor = new Vector3(0.5f, 0.55f, 0.6f),
            MaxLength = 24,
            ReadOnly = true,
            Interactable = false
        });
        panel.AddComponent(new HorizontalLayoutGroup
        {
            Spacing = 12f,
            PaddingLeft = 8f,
            PaddingRight = 16f,
            ChildAlignment = LayoutAlignment.Center,
            ChildForceExpandWidth = true,
            ControlChildHeight = false
        });

        string yaml = new SceneSerializer().SerializeSceneToString([canvasObject, panel]);
        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        List<GameObject> loaded = loader.LoadSceneFromYaml(yaml);

        Canvas canvas = loaded[0].GetComponent<Canvas>()!;
        canvas.ReferenceWidth.Should().Be(1280f);
        canvas.ReferenceHeight.Should().Be(720f);
        canvas.MatchWidthOrHeight.Should().Be(0.25f);
        canvas.SortingOrder.Should().Be(3);
        loaded[0].GetComponent<RectMask2D>().Should().NotBeNull();
        loaded[0].GetComponent<RectMask2D>()!.CornerRadius.Should().Be(14f);
        loaded[0].GetComponent<RectMask2D>()!.Feather.Should().Be(5f);
        loaded[0].GetComponent<RectMask2D>()!.MaskTextureName.Should().Be("Assets/UI/alpha.png");
        loaded[0].GetComponent<RectMask2D>()!.MaskThreshold.Should().Be(0.42f);
        loaded[1].Transform.Parent.Should().Be(loaded[0].Transform);

        RectTransform rect = loaded[1].GetComponent<RectTransform>()!;
        rect.AnchorMin.Should().Be(new Vector2(0f, 1f));
        rect.AnchorMax.Should().Be(new Vector2(0f, 1f));
        rect.AnchoredPosition.Should().Be(new Vector2(16f, -20f));
        rect.SizeDelta.Should().Be(new Vector2(320f, 64f));

        Image image = loaded[1].GetComponent<Image>()!;
        image.SpriteName.Should().Be("Assets/UI/panel.sprite.json");
        image.TextureName.Should().Be("Assets/UI/panel.png");
        image.UVMin.Should().Be(new Vector2(0.1f, 0.2f));
        image.UVMax.Should().Be(new Vector2(0.8f, 0.9f));
        image.SliceLeft.Should().Be(0.12f);
        image.SliceTop.Should().Be(0.18f);
        image.SliceRight.Should().Be(0.14f);
        image.SliceBottom.Should().Be(0.2f);
        image.Color.Should().Be(new Vector3(0.2f, 0.4f, 0.8f));
        image.Alpha.Should().Be(0.75f);
        Text text = loaded[1].GetComponent<Text>()!;
        text.Content.Should().Be("Continue");
        text.FontName.Should().Be("Assets/Fonts/Display.ttf");
        text.FontSize.Should().Be(30f);
        text.Wrap.Should().BeFalse();
        text.HorizontalAlignment.Should().Be(HorizontalTextAlignment.Center);
        text.VerticalAlignment.Should().Be(VerticalTextAlignment.Bottom);
        text.Direction.Should().Be(TextDirection.RightToLeft);
        loaded[1].GetComponent<Button>()!.Interactable.Should().BeFalse();
        Toggle toggle = loaded[1].GetComponent<Toggle>()!;
        toggle.IsOn.Should().BeTrue();
        toggle.Interactable.Should().BeFalse();
        toggle.OnColor.Should().Be(new Vector3(0.2f, 0.8f, 0.3f));
        toggle.OffColor.Should().Be(new Vector3(0.3f, 0.3f, 0.3f));
        Slider slider = loaded[1].GetComponent<Slider>()!;
        slider.MinValue.Should().Be(-2f);
        slider.MaxValue.Should().Be(8f);
        slider.Value.Should().Be(3f);
        slider.WholeNumbers.Should().BeTrue();
        slider.Direction.Should().Be(SliderDirection.Vertical);
        slider.TrackThickness.Should().Be(4f);
        slider.HandleSize.Should().Be(22f);
        InputField inputField = loaded[1].GetComponent<InputField>()!;
        inputField.Value.Should().Be("Player One");
        inputField.Placeholder.Should().Be("Enter name");
        inputField.PlaceholderColor.Should().Be(new Vector3(0.5f, 0.55f, 0.6f));
        inputField.MaxLength.Should().Be(24);
        inputField.ReadOnly.Should().BeTrue();
        inputField.Interactable.Should().BeFalse();
        HorizontalLayoutGroup layout = loaded[1].GetComponent<HorizontalLayoutGroup>()!;
        layout.Spacing.Should().Be(12f);
        layout.PaddingLeft.Should().Be(8f);
        layout.PaddingRight.Should().Be(16f);
        layout.ChildAlignment.Should().Be(LayoutAlignment.Center);
        layout.ChildForceExpandWidth.Should().BeTrue();
        layout.ControlChildHeight.Should().BeFalse();
        ComponentCatalog.Find("Horizontal Layout Group")!.ComponentType.Should().Be(typeof(HorizontalLayoutGroup));
        ComponentCatalog.Find("Vertical Layout Group")!.ComponentType.Should().Be(typeof(VerticalLayoutGroup));
        ComponentCatalog.Find("Toggle")!.ComponentType.Should().Be(typeof(Toggle));
        ComponentCatalog.Find("Slider")!.ComponentType.Should().Be(typeof(Slider));
        ComponentCatalog.Find("Input Field")!.ComponentType.Should().Be(typeof(InputField));
    }
}
