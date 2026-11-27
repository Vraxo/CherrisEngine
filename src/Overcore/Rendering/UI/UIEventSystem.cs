using Overcore.Components;
using System.Numerics;

namespace Overcore.Rendering.UI;

/// <summary>Pointer hit-testing, keyboard focus, text editing, and action dispatch for screen-space controls.</summary>
public sealed class UIEventSystem
{
    private Component? _pressedControl;
    private Component? _focusedControl;
    private bool _wasPointerDown;

    public Button? HoveredButton => HoveredControl as Button;
    public Toggle? HoveredToggle => HoveredControl as Toggle;
    public Slider? HoveredSlider => HoveredControl as Slider;
    public Button? PressedButton => _pressedControl as Button;
    public Toggle? PressedToggle => _pressedControl as Toggle;
    public Slider? PressedSlider => _pressedControl as Slider;
    public Component? FocusedControl => _focusedControl;
    public InputField? FocusedInputField => _focusedControl as InputField;
    private Component? HoveredControl { get; set; }

    /// <summary>Updates pointer state and emits a control action when release remains over its original target.</summary>
    public void Update(IReadOnlyList<UIElement> elements, Vector2 pointerPosition, bool pointerPressed, bool pointerDown,
        bool pointerReleased = false)
    {
        ArgumentNullException.ThrowIfNull(elements);

        HoveredControl = HitControl(elements, pointerPosition);

        if (pointerPressed)
        {
            SetFocusedControl(IsInteractable(HoveredControl) ? HoveredControl : null);
            if (IsInteractable(HoveredControl))
            {
                _pressedControl = HoveredControl;
            }
        }

        ProcessKeyboardFocus(elements);
        ProcessFocusedControl(elements);
        ProcessFocusedInput(elements);

        if (_pressedControl is Slider slider && (pointerDown || pointerReleased) && IsInteractable(slider))
        {
            UIElement? sliderElement = elements.LastOrDefault(element => ReferenceEquals(element.Slider, slider));
            if (sliderElement is not null)
            {
                slider.SetValueFromPointer(pointerPosition, sliderElement.Rect);
            }
        }

        if (pointerReleased)
        {
            if (IsInteractable(_pressedControl) && ReferenceEquals(_pressedControl, HoveredControl))
            {
                switch (_pressedControl)
                {
                    case Button button:
                        button.Click();
                        break;
                    case Toggle toggle:
                        toggle.ToggleValue();
                        break;
                }
            }

            _pressedControl = null;
        }
        else if (!pointerDown && !pointerPressed)
        {
            // A focus loss/reset is not a user release; cancel the press without synthesizing an action.
            _pressedControl = null;
        }

        _wasPointerDown = pointerDown;
    }

    /// <summary>Returns the tint multiplier for a button's image in the current pointer state.</summary>
    public Vector3 GetTint(Button button)
    {
        if (!button.Interactable)
        {
            return button.DisabledColor;
        }

        if (ReferenceEquals(_pressedControl, button) && _wasPointerDown)
        {
            return button.PressedColor;
        }

        return ReferenceEquals(HoveredControl, button) ? button.HoverColor : button.NormalColor;
    }

    /// <summary>Returns the state tint multiplier for a toggle's image.</summary>
    public Vector3 GetTint(Toggle toggle)
    {
        if (!toggle.Interactable)
        {
            return toggle.DisabledColor;
        }

        return toggle.IsOn ? toggle.OnColor : toggle.OffColor;
    }

    private static Component? HitControl(IReadOnlyList<UIElement> elements, Vector2 position)
    {
        for (int index = elements.Count - 1; index >= 0; index--)
        {
            UIElement element = elements[index];
            if (element.Button is { Enabled: true, Interactable: true } button && ContainsPointer(element, position))
            {
                return button;
            }

            if (element.Toggle is { Enabled: true, Interactable: true } toggle && ContainsPointer(element, position))
            {
                return toggle;
            }

            if (element.Slider is { Enabled: true, Interactable: true } slider && ContainsPointer(element, position))
            {
                return slider;
            }

            if (element.InputField is { Enabled: true, Interactable: true } inputField && ContainsPointer(element, position))
            {
                return inputField;
            }

            if ((element.Image is { Enabled: true, RaycastTarget: true }
                    || element.Text is { Enabled: true, RaycastTarget: true })
                && ContainsPointer(element, position))
            {
                // A child label/graphic can route to its nearest ancestor control. Otherwise it blocks controls below.
                return FindControlInParents(element.GameObject);
            }
        }

        return null;
    }

    private static bool ContainsPointer(UIElement element, Vector2 position) =>
        element.Rect.Contains(position)
        && (element.ClipRect is not { } clip || clip.Contains(position))
        && (element.RoundedClips is null || element.RoundedClips.All(mask => mask.Contains(position)));

    private static Component? FindControlInParents(GameObject gameObject)
    {
        for (Transform? transform = gameObject.Transform; transform is not null; transform = transform.Parent)
        {
            Button? button = transform.GameObject.GetComponent<Button>();
            if (button is { Enabled: true })
            {
                return button.Interactable ? button : null;
            }

            Toggle? toggle = transform.GameObject.GetComponent<Toggle>();
            if (toggle is { Enabled: true })
            {
                return toggle.Interactable ? toggle : null;
            }

            Slider? slider = transform.GameObject.GetComponent<Slider>();
            if (slider is { Enabled: true })
            {
                return slider.Interactable ? slider : null;
            }

            InputField? inputField = transform.GameObject.GetComponent<InputField>();
            if (inputField is { Enabled: true })
            {
                return inputField.Interactable ? inputField : null;
            }
        }

        return null;
    }

    private void ProcessKeyboardFocus(IReadOnlyList<UIElement> elements)
    {
        if (!Input.WasKeyPressed(Key.Tab))
        {
            return;
        }

        List<Component> controls = GetFocusableControls(elements);
        if (controls.Count == 0)
        {
            SetFocusedControl(null);
            return;
        }

        bool reverse = Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight);
        int current = controls.FindIndex(control => ReferenceEquals(control, _focusedControl));
        int next = current < 0
            ? (reverse ? controls.Count - 1 : 0)
            : (current + (reverse ? controls.Count - 1 : 1)) % controls.Count;
        SetFocusedControl(controls[next]);
    }

    private void ProcessFocusedControl(IReadOnlyList<UIElement> elements)
    {
        if (_focusedControl is not null && (!IsInteractable(_focusedControl) ||
            !elements.Any(element => ReferenceEquals(element.Button, _focusedControl)
                || ReferenceEquals(element.Toggle, _focusedControl)
                || ReferenceEquals(element.Slider, _focusedControl)
                || ReferenceEquals(element.InputField, _focusedControl))))
        {
            SetFocusedControl(null);
        }

        if (_focusedControl is Button button && (Input.WasKeyPressed(Key.Enter) || Input.WasKeyPressed(Key.Space)))
        {
            button.Click();
        }
        else if (_focusedControl is Toggle toggle && (Input.WasKeyPressed(Key.Enter) || Input.WasKeyPressed(Key.Space)))
        {
            toggle.ToggleValue();
        }
        else if (_focusedControl is Slider slider)
        {
            if (Input.WasKeyPressed(Key.Home))
            {
                slider.NormalizedValue = 0f;
            }
            else if (Input.WasKeyPressed(Key.End))
            {
                slider.NormalizedValue = 1f;
            }
            else
            {
                float direction =
                    (Input.WasKeyPressed(Key.Right) || Input.WasKeyPressed(Key.Up) ? 1f : 0f) -
                    (Input.WasKeyPressed(Key.Left) || Input.WasKeyPressed(Key.Down) ? 1f : 0f);
                if (direction != 0f)
                {
                    float range = MathF.Abs(slider.MaxValue - slider.MinValue);
                    float normalizedStep = slider.WholeNumbers && range > 0f ? 1f / range : 0.01f;
                    slider.NormalizedValue += direction * normalizedStep;
                }
            }
        }
    }

    private void ProcessFocusedInput(IReadOnlyList<UIElement> elements)
    {
        if (FocusedInputField is not { } inputField)
        {
            return;
        }

        if (!inputField.Enabled || !inputField.Interactable ||
            !elements.Any(element => ReferenceEquals(element.InputField, inputField)))
        {
            SetFocusedControl(null);
            return;
        }

        inputField.InsertText(Input.TextInput);
        TextDirection direction = inputField.GameObject?.GetComponent<Text>()?.Direction ?? TextDirection.Auto;
        foreach (Key key in new[] { Key.Left, Key.Right, Key.Home, Key.End, Key.BackSpace, Key.Delete })
        {
            if (!Input.WasKeyPressed(key)) continue;
            IReadOnlyList<int>? visualCaretStops = key is Key.Left or Key.Right
                ? ArabicTextLayout.ShapeLine(inputField.Value, direction).GetVisualCaretStops()
                : null;
            inputField.HandleKey(key, visualCaretStops);
        }

        if (Input.WasKeyPressed(Key.Enter) || Input.WasKeyPressed(Key.KeypadEnter) || Input.WasKeyPressed(Key.Escape))
        {
            SetFocusedControl(null);
        }
    }

    private void SetFocusedControl(Component? control)
    {
        if (ReferenceEquals(_focusedControl, control))
        {
            if (control is InputField sameField)
            {
                sameField.MoveCaretToEnd();
            }
            return;
        }

        if (_focusedControl is InputField previousField)
        {
            previousField.FinishEditing();
        }

        _focusedControl = control;
        if (_focusedControl is InputField nextField)
        {
            nextField.MoveCaretToEnd();
        }
    }

    private static List<Component> GetFocusableControls(IReadOnlyList<UIElement> elements)
    {
        List<Component> controls = [];
        HashSet<Component> seen = [];
        foreach (UIElement element in elements)
        {
            AddFocusable(element.Button);
            AddFocusable(element.Toggle);
            AddFocusable(element.Slider);
            AddFocusable(element.InputField);
        }

        return controls;

        void AddFocusable(Component? control)
        {
            if (control is not null && IsInteractable(control) && seen.Add(control))
            {
                controls.Add(control);
            }
        }
    }

    private static bool IsInteractable(Component? control) => control switch
    {
        Button { Enabled: true, Interactable: true } => true,
        Toggle { Enabled: true, Interactable: true } => true,
        Slider { Enabled: true, Interactable: true } => true,
        InputField { Enabled: true, Interactable: true } => true,
        _ => false
    };
}
