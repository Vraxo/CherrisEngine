using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering.UI;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.UI;

public class CanvasLayoutTests
{
    [Fact]
    public void Build_ResolvesTopLeftAnchoredElementInPixelSpace()
    {
        (GameObject canvasObject, GameObject element) = CreateUiTree();
        RectTransform rect = element.GetComponent<RectTransform>()!;
        rect.AnchorMin = rect.AnchorMax = Vector2.Zero;
        rect.Pivot = Vector2.Zero;
        rect.AnchoredPosition = new Vector2(24f, 36f);
        rect.SizeDelta = new Vector2(120f, 48f);
        element.AddComponent(new Image());

        UIElement resolved = CanvasLayout.Build([canvasObject], 1920f, 1080f).Single();

        resolved.Rect.Should().Be(new RectF(24f, 36f, 120f, 48f));
    }

    [Fact]
    public void Build_StretchesBetweenAnchorsAndScalesWithScreenSize()
    {
        (GameObject canvasObject, GameObject element) = CreateUiTree();
        Canvas canvas = canvasObject.GetComponent<Canvas>()!;
        canvas.MatchWidthOrHeight = 0f;

        RectTransform rect = element.GetComponent<RectTransform>()!;
        rect.AnchorMin = new Vector2(0.1f, 0.2f);
        rect.AnchorMax = new Vector2(0.9f, 0.8f);
        rect.Pivot = new Vector2(0.5f, 0.5f);
        rect.SizeDelta = new Vector2(-40f, -20f);
        rect.AnchoredPosition = Vector2.Zero;
        element.AddComponent(new Image());

        // At half the reference width, scale is 0.5 and the 960x1080 viewport maps to a
        // 1920x2160 logical canvas. The two-axis anchors stretch within that logical space.
        UIElement resolved = CanvasLayout.Build([canvasObject], 960f, 1080f).Single();

        resolved.Rect.X.Should().BeApproximately(106f, 0.001f);
        resolved.Rect.Y.Should().BeApproximately(221f, 0.001f);
        resolved.Rect.Width.Should().BeApproximately(748f, 0.001f);
        resolved.Rect.Height.Should().BeApproximately(638f, 0.001f);
    }

    [Fact]
    public void Build_PropagatesRectMaskClipToDescendantGraphics()
    {
        (GameObject canvasObject, GameObject maskObject) = CreateUiTree();
        RectTransform maskRect = maskObject.GetComponent<RectTransform>()!;
        maskRect.AnchorMin = maskRect.AnchorMax = Vector2.Zero;
        maskRect.Pivot = Vector2.Zero;
        maskRect.AnchoredPosition = new Vector2(20f, 30f);
        maskRect.SizeDelta = new Vector2(200f, 100f);
        maskObject.AddComponent(new RectMask2D());

        GameObject child = new("OverflowingChild");
        child.AddComponent(new RectTransform
        {
            AnchorMin = Vector2.Zero,
            AnchorMax = Vector2.Zero,
            Pivot = Vector2.Zero,
            AnchoredPosition = new Vector2(180f, 70f),
            SizeDelta = new Vector2(100f, 80f)
        });
        child.AddComponent(new Image());
        child.Transform.Parent = maskObject.Transform;

        UIElement resolved = CanvasLayout.Build([canvasObject, maskObject, child], 800f, 600f)
            .Single(element => ReferenceEquals(element.GameObject, child));

        resolved.Rect.Should().Be(new RectF(200f, 100f, 100f, 80f));
        resolved.ClipRect.Should().Be(new RectF(20f, 30f, 200f, 100f));
    }

    [Fact]
    public void Build_HorizontalLayoutGroupArrangesChildrenWithSpacingPaddingAndAlignment()
    {
        GameObject canvasObject = CreateCanvasRoot();
        canvasObject.AddComponent(new HorizontalLayoutGroup
        {
            PaddingLeft = 10f,
            PaddingRight = 20f,
            PaddingTop = 5f,
            PaddingBottom = 15f,
            Spacing = 10f,
            ChildAlignment = LayoutAlignment.End
        });
        GameObject first = AddLayoutChild(canvasObject, "First", new Vector2(100f, 50f));
        GameObject second = AddLayoutChild(canvasObject, "Second", new Vector2(200f, 80f));
        first.AddComponent(new Image());
        second.AddComponent(new Image());

        UIElement[] resolved = CanvasLayout.Build([canvasObject, first, second], 800f, 600f)
            .OrderBy(element => element.DrawOrder).ToArray();

        resolved[0].Rect.Should().Be(new RectF(470f, 535f, 100f, 50f));
        resolved[1].Rect.Should().Be(new RectF(580f, 505f, 200f, 80f));
    }

    [Fact]
    public void Build_VerticalLayoutGroupCanExpandChildrenToFillAvailableSpace()
    {
        GameObject canvasObject = CreateCanvasRoot();
        canvasObject.AddComponent(new VerticalLayoutGroup
        {
            PaddingTop = 10f,
            PaddingBottom = 10f,
            Spacing = 20f,
            ChildForceExpandWidth = true,
            ChildForceExpandHeight = true
        });
        GameObject first = AddLayoutChild(canvasObject, "First", new Vector2(80f, 100f));
        GameObject second = AddLayoutChild(canvasObject, "Second", new Vector2(120f, 100f));
        first.AddComponent(new Image());
        second.AddComponent(new Image());

        UIElement[] resolved = CanvasLayout.Build([canvasObject, first, second], 800f, 600f)
            .OrderBy(element => element.DrawOrder).ToArray();

        resolved[0].Rect.Should().Be(new RectF(0f, 10f, 800f, 280f));
        resolved[1].Rect.Should().Be(new RectF(0f, 310f, 800f, 280f));
    }

    [Fact]
    public void Build_NestedLayoutGroupUsesResolvedParentRectangle()
    {
        GameObject canvasObject = CreateCanvasRoot();
        GameObject container = new("Container");
        container.AddComponent(new RectTransform
        {
            AnchorMin = Vector2.Zero,
            AnchorMax = Vector2.Zero,
            Pivot = Vector2.Zero,
            AnchoredPosition = new Vector2(50f, 40f),
            SizeDelta = new Vector2(300f, 200f)
        });
        container.AddComponent(new VerticalLayoutGroup { PaddingLeft = 12f, PaddingTop = 8f, Spacing = 6f });
        container.Transform.Parent = canvasObject.Transform;
        GameObject child = AddLayoutChild(container, "Child", new Vector2(60f, 30f));
        child.AddComponent(new Image());

        UIElement resolved = CanvasLayout.Build([canvasObject, container, child], 800f, 600f).Single();

        resolved.Rect.Should().Be(new RectF(62f, 48f, 60f, 30f));
    }

    [Fact]
    public void Build_CollectsTextOnlyGraphics()
    {
        (GameObject canvasObject, GameObject element) = CreateUiTree();
        Text text = element.AddComponent(new Text { Content = "Ready" });

        UIElement resolved = CanvasLayout.Build([canvasObject], 1920f, 1080f).Single();

        resolved.Text.Should().BeSameAs(text);
        resolved.Image.Should().BeNull();
    }

    [Fact]
    public void Build_CollectsToggleControlsWithoutAnImage()
    {
        (GameObject canvasObject, GameObject element) = CreateUiTree();
        Toggle toggle = element.AddComponent(new Toggle { IsOn = true });

        UIElement resolved = CanvasLayout.Build([canvasObject], 800f, 600f).Single();

        resolved.Toggle.Should().BeSameAs(toggle);
        resolved.Image.Should().BeNull();
    }

    [Fact]
    public void Build_CollectsSliderControlsWithoutAnImage()
    {
        (GameObject canvasObject, GameObject element) = CreateUiTree();
        Slider slider = element.AddComponent(new Slider { Value = 0.5f });

        UIElement resolved = CanvasLayout.Build([canvasObject], 800f, 600f).Single();

        resolved.Slider.Should().BeSameAs(slider);
        resolved.Image.Should().BeNull();
    }

    [Fact]
    public void Build_DoesNotIncludeInactiveCanvasOrGraphic()
    {
        (GameObject canvasObject, GameObject element) = CreateUiTree();
        element.AddComponent(new Image());
        element.SetActive(false);

        CanvasLayout.Build([canvasObject], 800f, 600f).Should().BeEmpty();
    }

    private static GameObject CreateCanvasRoot()
    {
        GameObject canvasObject = new("Canvas");
        canvasObject.AddComponent(new RectTransform());
        canvasObject.AddComponent(new Canvas { ScaleMode = CanvasScaleMode.ConstantPixelSize });
        return canvasObject;
    }

    private static GameObject AddLayoutChild(GameObject parent, string name, Vector2 size)
    {
        GameObject child = new(name);
        child.AddComponent(new RectTransform
        {
            AnchorMin = Vector2.Zero,
            AnchorMax = Vector2.Zero,
            Pivot = Vector2.Zero,
            SizeDelta = size
        });
        child.Transform.Parent = parent.Transform;
        return child;
    }

    private static (GameObject Canvas, GameObject Element) CreateUiTree()
    {
        GameObject canvasObject = new("Canvas");
        canvasObject.AddComponent(new RectTransform());
        canvasObject.AddComponent(new Canvas
        {
            ScaleMode = CanvasScaleMode.ConstantPixelSize
        });

        GameObject element = new("Panel");
        element.AddComponent(new RectTransform());
        element.Transform.Parent = canvasObject.Transform;
        return (canvasObject, element);
    }
}
