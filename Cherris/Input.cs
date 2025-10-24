using System;
using System.Collections.Generic;
using System.Numerics;

namespace Cherris;

public static class Input
{
    private static readonly HashSet<Key> _pressedKeys = new();
    private static readonly HashSet<Key> _justPressedKeys = new();

    private static readonly HashSet<MouseButton> _pressedMouseButtons = new();
    private static readonly HashSet<MouseButton> _justPressedMouseButtons = new();

    private static bool _isMouseLocked;
    public static event Action<bool> OnLockStateChanged;

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

    // Called by the active windowing system at the start of each frame.
    internal static void FrameStarted()
    {
        _justPressedKeys.Clear();
        _justPressedMouseButtons.Clear();
        MouseDelta = Vector2.Zero; // Always reset delta at the start of a frame.
    }

    internal static void ClearState()
    {
        _pressedKeys.Clear();
        _justPressedKeys.Clear();
        _pressedMouseButtons.Clear();
        _justPressedMouseButtons.Clear();
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

    internal static void SetMouseButtonState(MouseButton button, bool isDown)
    {
        if (isDown)
        {
            if (_pressedMouseButtons.Add(button))
            {
                _justPressedMouseButtons.Add(button);
            }
        }
        else
        {
            _pressedMouseButtons.Remove(button);
        }
    }

    internal static void SetMouseDelta(Vector2 delta) => MouseDelta = delta;
    internal static void SetMousePosition(Vector2 position) => MousePosition = position;

    public static bool IsKeyDown(Key key) => _pressedKeys.Contains(key);
    public static bool WasKeyPressed(Key key) => _justPressedKeys.Contains(key);
    public static bool IsMouseButtonDown(MouseButton button) => _pressedMouseButtons.Contains(button);
    public static bool WasMouseButtonPressed(MouseButton button) => _justPressedMouseButtons.Contains(button);
}