using AwesomeAssertions;
using Overcore;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering.Headless;
using Overcore.Rendering.UI;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.UI;

public class InputFieldTests
{
    [Fact]
    public void Update_FocusesFieldEditsUnicodeTextAndSupportsCaretDeletion()
    {
        using HeadlessGameWindow window = new(800, 600);
        HeadlessInput.Reset();
        window.ProcessEvents();
        (GameObject canvasObject, GameObject fieldObject, InputField field) = CreateField();
        IReadOnlyList<UIElement> elements = CanvasLayout.Build([canvasObject, fieldObject], 800f, 600f);
        UIEventSystem events = new();
        List<string> changed = [];
        List<string> ended = [];
        field.ValueChanged += changed.Add;
        field.EndEdit += ended.Add;

        events.Update(elements, new Vector2(30f, 30f), pointerPressed: true, pointerDown: true);
        events.FocusedInputField.Should().BeSameAs(field);

        HeadlessInput.TypeText("Hi🌍");
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        field.Value.Should().Be("Hi🌍");

        HeadlessInput.PressKey(Key.Left);
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        HeadlessInput.TypeText("!");
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        field.Value.Should().Be("Hi!🌍");

        HeadlessInput.PressKey(Key.BackSpace);
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        field.Value.Should().Be("Hi🌍");

        HeadlessInput.PressKey(Key.Delete);
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        field.Value.Should().Be("Hi");
        changed.Should().Equal("Hi🌍", "Hi!🌍", "Hi🌍", "Hi");

        events.Update(elements, new Vector2(700f, 500f), pointerPressed: true, pointerDown: true);
        events.FocusedInputField.Should().BeNull();
        ended.Should().ContainSingle().Which.Should().Be("Hi");
        HeadlessInput.Reset();
        window.ProcessEvents();
    }

    [Fact]
    public void Update_VisualRightArrowMovesIntoRtlTextAndInsertsAtLogicalCaret()
    {
        using HeadlessGameWindow window = new(800, 600);
        HeadlessInput.Reset();
        window.ProcessEvents();
        (GameObject canvasObject, GameObject fieldObject, InputField field) = CreateField();
        field.Value = "سلام";
        IReadOnlyList<UIElement> elements = CanvasLayout.Build([canvasObject, fieldObject], 800f, 600f);
        UIEventSystem events = new();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: true, pointerDown: true);

        HeadlessInput.PressKey(Key.Right);
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        HeadlessInput.TypeText("!");
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);

        field.Value.Should().Be("سلا!م");
        HeadlessInput.Reset();
        window.ProcessEvents();
    }

    [Fact]
    public void Update_CaretMovementAndDeletionRespectCombiningTextElements()
    {
        using HeadlessGameWindow window = new(800, 600);
        HeadlessInput.Reset();
        window.ProcessEvents();
        (GameObject canvasObject, GameObject fieldObject, InputField field) = CreateField();
        field.Value = "e\u0301🌍";
        IReadOnlyList<UIElement> elements = CanvasLayout.Build([canvasObject, fieldObject], 800f, 600f);
        UIEventSystem events = new();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: true, pointerDown: true);

        HeadlessInput.PressKey(Key.Left);
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        HeadlessInput.TypeText("!");
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        field.Value.Should().Be("e\u0301!🌍");

        HeadlessInput.PressKey(Key.BackSpace);
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        HeadlessInput.PressKey(Key.BackSpace);
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);
        field.Value.Should().Be("🌍");
        field.MaxLength = 1;
        field.Value = "e\u0301";
        field.Value.Should().BeEmpty("a combining sequence must not be truncated halfway through its grapheme");

        HeadlessInput.Reset();
        window.ProcessEvents();
    }

    [Fact]
    public void Update_TabFocusesInputFieldAndMovesToNextControl()
    {
        using HeadlessGameWindow window = new(800, 600);
        HeadlessInput.Reset();
        window.ProcessEvents();
        (GameObject canvasObject, GameObject fieldObject, InputField field) = CreateField();
        GameObject buttonObject = new("SubmitButton");
        buttonObject.AddComponent(new RectTransform
        {
            AnchorMin = Vector2.Zero,
            AnchorMax = Vector2.Zero,
            Pivot = Vector2.Zero,
            AnchoredPosition = new Vector2(20f, 80f),
            SizeDelta = new Vector2(120f, 40f)
        });
        Button button = buttonObject.AddComponent(new Button());
        buttonObject.Transform.Parent = canvasObject.Transform;
        IReadOnlyList<UIElement> elements = CanvasLayout.Build([canvasObject, fieldObject, buttonObject], 800f, 600f);
        UIEventSystem events = new();
        int endEdits = 0;
        field.EndEdit += _ => endEdits++;

        HeadlessInput.PressKey(Key.Tab);
        window.ProcessEvents();
        events.Update(elements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        events.FocusedControl.Should().BeSameAs(field);
        HeadlessInput.ReleaseKey(Key.Tab);
        window.ProcessEvents();
        events.Update(elements, Vector2.Zero, pointerPressed: false, pointerDown: false);

        HeadlessInput.PressKey(Key.Tab);
        window.ProcessEvents();
        events.Update(elements, Vector2.Zero, pointerPressed: false, pointerDown: false);
        events.FocusedControl.Should().BeSameAs(button);
        endEdits.Should().Be(1);
        HeadlessInput.Reset();
        window.ProcessEvents();
    }

    [Fact]
    public void Update_ReadOnlyFieldCanFocusButRejectsTextChanges()
    {
        using HeadlessGameWindow window = new(800, 600);
        HeadlessInput.Reset();
        window.ProcessEvents();
        (GameObject canvasObject, GameObject fieldObject, InputField field) = CreateField();
        field.Value = "locked";
        field.ReadOnly = true;
        IReadOnlyList<UIElement> elements = CanvasLayout.Build([canvasObject, fieldObject], 800f, 600f);
        UIEventSystem events = new();

        events.Update(elements, new Vector2(30f, 30f), pointerPressed: true, pointerDown: true);
        HeadlessInput.TypeText("no");
        window.ProcessEvents();
        events.Update(elements, new Vector2(30f, 30f), pointerPressed: false, pointerDown: false);

        events.FocusedInputField.Should().BeSameAs(field);
        field.Value.Should().Be("locked");
        HeadlessInput.Reset();
        window.ProcessEvents();
    }

    private static (GameObject Canvas, GameObject FieldObject, InputField Field) CreateField()
    {
        GameObject canvas = new("Canvas");
        canvas.AddComponent(new RectTransform());
        canvas.AddComponent(new Canvas { ScaleMode = CanvasScaleMode.ConstantPixelSize });

        GameObject fieldObject = new("NameField");
        fieldObject.AddComponent(new RectTransform
        {
            AnchorMin = Vector2.Zero,
            AnchorMax = Vector2.Zero,
            Pivot = Vector2.Zero,
            AnchoredPosition = new Vector2(20f, 20f),
            SizeDelta = new Vector2(300f, 40f)
        });
        fieldObject.AddComponent(new Text());
        InputField field = fieldObject.AddComponent(new InputField { Placeholder = "Name" });
        fieldObject.Transform.Parent = canvas.Transform;
        return (canvas, fieldObject, field);
    }
}
