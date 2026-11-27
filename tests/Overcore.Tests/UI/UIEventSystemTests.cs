using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering.Headless;
using Overcore.Rendering.UI;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.UI;

public class UIEventSystemTests
{
    [Fact]
    public void Update_FiresClickWhenPressBeginsAndEndsOnSameButton()
    {
        Button button = new();
        UIElement[] elements = [CreateElement(button, new RectF(10f, 20f, 100f, 40f))];
        UIEventSystem events = new();
        int clicks = 0;
        button.Clicked += () => clicks++;

        events.Update(elements, new Vector2(30f, 30f), pointerPressed: true, pointerDown: true);
        events.HoveredButton.Should().BeSameAs(button);
        events.GetTint(button).Should().Be(button.PressedColor);

        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false, pointerReleased: true);

        clicks.Should().Be(1);
        events.PressedButton.Should().BeNull();
    }

    [Fact]
    public void Update_HandlesPressAndReleaseBetweenRenderedFrames()
    {
        Button button = new();
        UIElement[] elements = [CreateElement(button, new RectF(0f, 0f, 100f, 100f))];
        UIEventSystem events = new();
        int clicks = 0;
        button.Clicked += () => clicks++;

        events.Update(elements, new Vector2(50f, 50f), pointerPressed: true, pointerDown: false, pointerReleased: true);

        clicks.Should().Be(1);
    }

    [Fact]
    public void Update_DoesNotFireWhenPointerIsReleasedOutsideButton()
    {
        Button button = new();
        UIElement[] elements = [CreateElement(button, new RectF(10f, 20f, 100f, 40f))];
        UIEventSystem events = new();
        int clicks = 0;
        button.Clicked += () => clicks++;

        events.Update(elements, new Vector2(30f, 30f), pointerPressed: true, pointerDown: true);
        events.Update(elements, new Vector2(300f, 300f), pointerPressed: false, pointerDown: false, pointerReleased: true);

        clicks.Should().Be(0);
    }

    [Fact]
    public void Update_CancelsPressWhenInputStateIsResetWithoutRelease()
    {
        Button button = new();
        UIElement[] elements = [CreateElement(button, new RectF(0f, 0f, 100f, 100f))];
        UIEventSystem events = new();
        int clicks = 0;
        button.Clicked += () => clicks++;

        events.Update(elements, new Vector2(50f, 50f), pointerPressed: true, pointerDown: true);
        events.Update(elements, new Vector2(50f, 50f), pointerPressed: false, pointerDown: false);

        clicks.Should().Be(0);
        events.PressedButton.Should().BeNull();
    }

    [Fact]
    public void Update_ToggleChangesValueWhenReleasedOverItsPressTarget()
    {
        Toggle toggle = new();
        UIElement[] elements = [CreateElement(toggle, new RectF(10f, 20f, 100f, 40f))];
        UIEventSystem events = new();
        List<bool> values = [];
        toggle.ValueChanged += values.Add;

        events.Update(elements, new Vector2(30f, 30f), pointerPressed: true, pointerDown: true);
        events.HoveredToggle.Should().BeSameAs(toggle);
        events.PressedToggle.Should().BeSameAs(toggle);
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false, pointerReleased: true);

        toggle.IsOn.Should().BeTrue();
        values.Should().Equal(true);
        events.PressedToggle.Should().BeNull();
    }

    [Fact]
    public void Update_ToggleDoesNotChangeWhenReleasedOutsideOrDisabled()
    {
        Toggle toggle = new();
        UIElement[] elements = [CreateElement(toggle, new RectF(0f, 0f, 100f, 100f))];
        UIEventSystem events = new();

        events.Update(elements, new Vector2(50f, 50f), pointerPressed: true, pointerDown: true);
        events.Update(elements, new Vector2(150f, 50f), pointerPressed: false, pointerDown: false, pointerReleased: true);
        toggle.IsOn.Should().BeFalse();

        toggle.Interactable = false;
        events.Update(elements, new Vector2(50f, 50f), pointerPressed: true, pointerDown: true);
        events.Update(elements, new Vector2(50f, 50f), pointerPressed: false, pointerDown: false, pointerReleased: true);
        toggle.IsOn.Should().BeFalse();
    }

    [Fact]
    public void Update_SliderTracksDragAndClampsAtTheEndpoints()
    {
        Slider slider = new() { MaxValue = 10f };
        UIElement[] elements = [CreateElement(slider, new RectF(0f, 0f, 100f, 30f))];
        UIEventSystem events = new();
        List<float> values = [];
        slider.ValueChanged += values.Add;

        events.Update(elements, new Vector2(50f, 15f), pointerPressed: true, pointerDown: true);
        events.HoveredSlider.Should().BeSameAs(slider);
        slider.Value.Should().BeApproximately(5f, 0.001f);
        events.Update(elements, new Vector2(200f, 15f), pointerPressed: false, pointerDown: true);
        slider.Value.Should().Be(10f);
        events.Update(elements, new Vector2(-50f, 15f), pointerPressed: false, pointerDown: false, pointerReleased: true);
        slider.Value.Should().Be(0f);
        values.Should().Equal(5f, 10f, 0f);
        events.PressedSlider.Should().BeNull();
    }

    [Fact]
    public void Update_VerticalSliderMapsBottomToMinimumAndRoundsWholeNumbers()
    {
        Slider slider = new()
        {
            MinValue = 1f,
            MaxValue = 9f,
            Value = 1f,
            WholeNumbers = true,
            Direction = SliderDirection.Vertical
        };
        UIElement[] elements = [CreateElement(slider, new RectF(0f, 0f, 30f, 100f))];
        UIEventSystem events = new();

        events.Update(elements, new Vector2(15f, 91f), pointerPressed: true, pointerDown: true);
        slider.Value.Should().Be(1f);
        events.Update(elements, new Vector2(15f, 60f), pointerPressed: false, pointerDown: true);
        slider.Value.Should().Be(4f);
        events.Update(elements, new Vector2(15f, 60f), pointerPressed: false, pointerDown: false, pointerReleased: true);
    }

    [Fact]
    public void Update_DisabledSliderDoesNotCapturePointer()
    {
        Slider slider = new() { Interactable = false };
        UIElement[] elements = [CreateElement(slider, new RectF(0f, 0f, 100f, 30f))];
        UIEventSystem events = new();

        events.Update(elements, new Vector2(50f, 15f), pointerPressed: true, pointerDown: true);

        events.HoveredSlider.Should().BeNull();
        events.PressedSlider.Should().BeNull();
        slider.Value.Should().Be(0f);
    }

    [Fact]
    public void Update_TabMovesFocusInDrawOrderAndShiftTabWraps()
    {
        using HeadlessGameWindow window = new(800, 600);
        HeadlessInput.Reset();
        window.ProcessEvents();
        Button button = new();
        int buttonClicks = 0;
        button.Clicked += () => buttonClicks++;
        Toggle toggle = new();
        Slider slider = new();
        Button disabledButton = new() { Interactable = false };
        UIElement[] elements =
        [
            CreateElement(button, new RectF(0f, 0f, 20f, 20f), 0),
            CreateElement(disabledButton, new RectF(20f, 0f, 20f, 20f), 1),
            CreateElement(toggle, new RectF(40f, 0f, 20f, 20f), 2),
            CreateElement(slider, new RectF(60f, 0f, 20f, 20f), 3)
        ];
        UIEventSystem events = new();

        HeadlessInput.PressKey(Key.Tab);
        window.ProcessEvents();
        events.Update(elements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        events.FocusedControl.Should().BeSameAs(button);
        HeadlessInput.PressKey(Key.Enter);
        window.ProcessEvents();
        events.Update(elements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        buttonClicks.Should().Be(1);

        HeadlessInput.ReleaseKey(Key.Tab);
        HeadlessInput.ReleaseKey(Key.Enter);
        window.ProcessEvents();
        events.Update(elements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        HeadlessInput.PressKey(Key.Tab);
        window.ProcessEvents();
        events.Update(elements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        events.FocusedControl.Should().BeSameAs(toggle);

        HeadlessInput.ReleaseKey(Key.Tab);
        HeadlessInput.PressKey(Key.ShiftLeft);
        HeadlessInput.PressKey(Key.Tab);
        window.ProcessEvents();
        events.Update(elements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        events.FocusedControl.Should().BeSameAs(button);

        HeadlessInput.Reset();
        window.ProcessEvents();
    }

    [Fact]
    public void Update_KeyboardActivatesFocusedToggleAndAdjustsFocusedSlider()
    {
        using HeadlessGameWindow window = new(800, 600);
        HeadlessInput.Reset();
        window.ProcessEvents();
        Toggle toggle = new();
        UIElement[] toggleElements = [CreateElement(toggle, new RectF(0f, 0f, 100f, 50f))];
        UIEventSystem events = new();

        HeadlessInput.PressKey(Key.Tab);
        window.ProcessEvents();
        events.Update(toggleElements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        HeadlessInput.ReleaseKey(Key.Tab);
        window.ProcessEvents();
        events.Update(toggleElements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        HeadlessInput.PressKey(Key.Space);
        window.ProcessEvents();
        events.Update(toggleElements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        toggle.IsOn.Should().BeTrue();

        HeadlessInput.Reset();
        window.ProcessEvents();
        Slider slider = new();
        UIElement[] sliderElements = [CreateElement(slider, new RectF(0f, 0f, 100f, 30f))];
        events = new UIEventSystem();
        HeadlessInput.PressKey(Key.Tab);
        window.ProcessEvents();
        events.Update(sliderElements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        HeadlessInput.ReleaseKey(Key.Tab);
        window.ProcessEvents();
        events.Update(sliderElements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        HeadlessInput.PressKey(Key.Right);
        window.ProcessEvents();
        events.Update(sliderElements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        slider.Value.Should().BeApproximately(0.01f, 0.0001f);
        HeadlessInput.PressKey(Key.End);
        window.ProcessEvents();
        events.Update(sliderElements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        slider.Value.Should().Be(1f);

        HeadlessInput.Reset();
        window.ProcessEvents();
    }

    [Fact]
    public void Update_ChildTextBubblesClickToAncestorButton()
    {
        GameObject buttonObject = new("Button");
        Button button = buttonObject.AddComponent(new Button());
        GameObject labelObject = new("Label");
        labelObject.Transform.Parent = buttonObject.Transform;
        Text label = labelObject.AddComponent(new Text { Content = "Go" });
        UIElement[] elements =
        [
            new(buttonObject, new Canvas(), new RectTransform(), new RectF(0f, 0f, 100f, 50f), null, null, null, button, 0),
            new(labelObject, new Canvas(), new RectTransform(), new RectF(10f, 10f, 80f, 32f), null, null, label, null, 1)
        ];
        UIEventSystem events = new();

        events.Update(elements, new Vector2(20f, 20f), pointerPressed: true, pointerDown: true);

        events.HoveredButton.Should().BeSameAs(button);
    }

    [Fact]
    public void Update_ClippedButtonCannotBeHoveredOutsideItsInheritedClip()
    {
        Button button = new();
        Image image = new();
        UIElement[] elements =
        [
            new(new GameObject("ClippedButton"), new Canvas(), new RectTransform(),
                new RectF(0f, 0f, 100f, 100f), new RectF(0f, 0f, 50f, 100f), image, null, button, 0)
        ];
        UIEventSystem events = new();

        events.Update(elements, new Vector2(75f, 50f), pointerPressed: false, pointerDown: false);

        events.HoveredButton.Should().BeNull();
    }

    [Fact]
    public void Update_TopRaycastGraphicBlocksButtonsBehindIt()
    {
        Button button = new();
        Image blocker = new();
        UIElement[] elements =
        [
            CreateElement(button, new RectF(0f, 0f, 100f, 100f), 0),
            CreateElement(blocker, new RectF(0f, 0f, 100f, 100f), 1)
        ];
        UIEventSystem events = new();

        events.Update(elements, new Vector2(50f, 50f), pointerPressed: true, pointerDown: true);

        events.HoveredButton.Should().BeNull();
        events.PressedButton.Should().BeNull();
    }

    private static UIElement CreateElement(Button button, RectF rect, int order = 0) =>
        new(new GameObject("Button"), new Canvas(), new RectTransform(), rect, null, null, null, button, order);

    private static UIElement CreateElement(Toggle toggle, RectF rect, int order = 0) =>
        new(new GameObject("Toggle"), new Canvas(), new RectTransform(), rect, null, null, null, null, order, toggle);

    private static UIElement CreateElement(Slider slider, RectF rect, int order = 0) =>
        new(new GameObject("Slider"), new Canvas(), new RectTransform(), rect, null, null, null, null, order, null, slider);

    private static UIElement CreateElement(Image image, RectF rect, int order) =>
        new(new GameObject("Image"), new Canvas(), new RectTransform(), rect, null, image, null, null, order);
}
