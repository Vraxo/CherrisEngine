using System.Numerics;
using System.Text;

namespace Overcore;

public static class Input
{
    private static readonly HashSet<Key> _pressedKeys = new();
    private static readonly HashSet<Key> _justPressedKeys = new();

    private static readonly HashSet<MouseButton> _pressedMouseButtons = new();
    private static readonly HashSet<MouseButton> _justPressedMouseButtons = new();
    private static readonly HashSet<MouseButton> _justReleasedMouseButtons = new();

    private static bool _isMouseLocked;
    private static string _textInput = String.Empty;
    public static event Action<bool> OnLockStateChanged;

    /// <summary>Text characters received during the current input frame.</summary>
    public static string TextInput => _textInput;

    public static bool IsMouseLocked
    {
        get => _isMouseLocked;
        set
        {
            if (_isMouseLocked == value) return;
            _isMouseLocked = value;
            OnLockStateChanged?.Invoke(value);
        }
    }

    public static Vector2 MouseDelta { get; private set; }
    public static Vector2 MousePosition { get; private set; }
    public static Vector2 MouseWheelDelta { get; private set; }

    // Called by the active windowing system at the start of each frame.
    internal static void FrameStarted()
    {
        _justPressedKeys.Clear();
        _justPressedMouseButtons.Clear();
        _justReleasedMouseButtons.Clear();
        _textInput = String.Empty;
        MouseDelta = Vector2.Zero; // Always reset delta at the start of a frame.
        MouseWheelDelta = Vector2.Zero;
    }

    internal static void ClearState()
    {
        _pressedKeys.Clear();
        _justPressedKeys.Clear();
        _pressedMouseButtons.Clear();
        _justPressedMouseButtons.Clear();
        _justReleasedMouseButtons.Clear();
        _textInput = String.Empty;
    }

    internal static void SetKeyState(Key key, bool isDown)
    {
        if (key == Key.Unknown) return;

        if (isDown)
        {
            if (_pressedKeys.Add(key))
            {
                _justPressedKeys.Add(key);
            }
        }
        else
        {
            _pressedKeys.Remove(key);
        }
    }

    internal static void AddTextInput(int unicode)
    {
        if (Rune.TryCreate(unicode, out Rune rune) && !Rune.IsControl(rune))
        {
            _textInput += rune.ToString();
        }
    }

    internal static void AddTextInput(string? text)
    {
        if (String.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!Rune.IsControl(rune))
            {
                _textInput += rune.ToString();
            }
        }
    }

    internal static void SetMouseButtonState(MouseButton button, bool isDown)
    {
        if (isDown)
        {
            if (_pressedMouseButtons.Add(button))
            {
                _justPressedMouseButtons.Add(button);
            }
        }
        else if (_pressedMouseButtons.Remove(button))
        {
            _justReleasedMouseButtons.Add(button);
        }
    }

    internal static void SetMouseDelta(Vector2 delta) => MouseDelta = delta;
    internal static void SetMousePosition(Vector2 position) => MousePosition = position;
    internal static void SetMouseWheelDelta(Vector2 delta) => MouseWheelDelta = delta;

    public static bool IsKeyDown(Key key) => _pressedKeys.Contains(key);
    public static bool WasKeyPressed(Key key) => _justPressedKeys.Contains(key);
    public static bool IsMouseButtonDown(MouseButton button) => _pressedMouseButtons.Contains(button);
    public static bool WasMouseButtonPressed(MouseButton button) => _justPressedMouseButtons.Contains(button);
    public static bool WasMouseButtonReleased(MouseButton button) => _justReleasedMouseButtons.Contains(button);
}