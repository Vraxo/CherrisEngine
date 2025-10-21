using System;
using System.Collections.Generic;
using System.Numerics;
using Veldrid;

namespace Cherris;

public static class Input
{
    private static readonly HashSet<Key> _pressedKeys = new();
    private static readonly HashSet<MouseButton> _justPressedMouseButtons = new();
    private static readonly HashSet<MouseButton> _pressedMouseButtons = new();
    private static bool _awaitingMouseCentering;

    public static bool IsMouseLocked { get; internal set; } = true;
    public static Vector2 MouseDelta { get; private set; }
    public static Vector2 MousePosition { get; private set; }

    internal static void UpdateSnapshot(InputSnapshot snapshot, Vector2 windowCenter)
    {
        _justPressedMouseButtons.Clear();

        foreach (var keyEvent in snapshot.KeyEvents)
        {
            var engineKey = VeldridKeyMapper.ToEngineKey(keyEvent.Key);
            if (engineKey == Key.Unknown) continue;

            if (keyEvent.Down) _pressedKeys.Add(engineKey);
            else _pressedKeys.Remove(engineKey);
        }

        foreach (var mouseEvent in snapshot.MouseEvents)
        {
            var engineButton = VeldridKeyMapper.ToEngineButton(mouseEvent.MouseButton);
            if (mouseEvent.Down)
            {
                if (_pressedMouseButtons.Add(engineButton))
                {
                    _justPressedMouseButtons.Add(engineButton);
                }
            }
            else
            {
                _pressedMouseButtons.Remove(engineButton);
            }
        }

        MousePosition = snapshot.MousePosition;

        if (!IsMouseLocked)
        {
            MouseDelta = Vector2.Zero;
            _awaitingMouseCentering = true;
            return;
        }

        if (_awaitingMouseCentering)
        {
            float distanceFromCenter = Vector2.Distance(snapshot.MousePosition, windowCenter);
            if (distanceFromCenter > 1.0f)
            {
                MouseDelta = Vector2.Zero;
                return;
            }
            else
            {
                _awaitingMouseCentering = false;
            }
        }
        MouseDelta = snapshot.MousePosition - windowCenter;
    }

    public static bool IsKeyDown(Key key) => _pressedKeys.Contains(key);
    public static bool WasMouseButtonPressed(MouseButton button) => _justPressedMouseButtons.Contains(button);
}

// Helper class to map Veldrid inputs to engine inputs.
public static class VeldridKeyMapper
{
    public static Key ToEngineKey(Veldrid.Key key)
    {
        if (key >= Veldrid.Key.F1 && key <= Veldrid.Key.F35) return (Key)((int)Key.F1 + (int)key - (int)Veldrid.Key.F1);
        if (key >= Veldrid.Key.Keypad0 && key <= Veldrid.Key.KeypadEnter) return (Key)((int)Key.Keypad0 + (int)key - (int)Veldrid.Key.Keypad0);
        if (key >= Veldrid.Key.A && key <= Veldrid.Key.Z) return (Key)((int)Key.A + (int)key - (int)Veldrid.Key.A);
        if (key >= Veldrid.Key.Number0 && key <= Veldrid.Key.Number9) return (Key)((int)Key.Number0 + (int)key - (int)Veldrid.Key.Number0);

        switch (key)
        {
            case Veldrid.Key.ShiftLeft: return Key.ShiftLeft;
            case Veldrid.Key.ShiftRight: return Key.ShiftRight;
            case Veldrid.Key.ControlLeft: return Key.ControlLeft;
            case Veldrid.Key.ControlRight: return Key.ControlRight;
            case Veldrid.Key.AltLeft: return Key.AltLeft;
            case Veldrid.Key.AltRight: return Key.AltRight;
            case Veldrid.Key.WinLeft: return Key.WinLeft;
            case Veldrid.Key.WinRight: return Key.WinRight;
            case Veldrid.Key.Menu: return Key.Menu;
            case Veldrid.Key.Up: return Key.Up;
            case Veldrid.Key.Down: return Key.Down;
            case Veldrid.Key.Left: return Key.Left;
            case Veldrid.Key.Right: return Key.Right;
            case Veldrid.Key.Enter: return Key.Enter;
            case Veldrid.Key.Escape: return Key.Escape;
            case Veldrid.Key.Space: return Key.Space;
            case Veldrid.Key.Tab: return Key.Tab;
            case Veldrid.Key.BackSpace: return Key.BackSpace;
            case Veldrid.Key.Insert: return Key.Insert;
            case Veldrid.Key.Delete: return Key.Delete;
            case Veldrid.Key.PageUp: return Key.PageUp;
            case Veldrid.Key.PageDown: return Key.PageDown;
            case Veldrid.Key.Home: return Key.Home;
            case Veldrid.Key.End: return Key.End;
            case Veldrid.Key.CapsLock: return Key.CapsLock;
            case Veldrid.Key.ScrollLock: return Key.ScrollLock;
            case Veldrid.Key.PrintScreen: return Key.PrintScreen;
            case Veldrid.Key.Pause: return Key.Pause;
            case Veldrid.Key.NumLock: return Key.NumLock;
            case Veldrid.Key.Clear: return Key.Clear;
            case Veldrid.Key.Sleep: return Key.Sleep;
            case Veldrid.Key.Tilde: return Key.Tilde;
            case Veldrid.Key.Minus: return Key.Minus;
            case Veldrid.Key.Plus: return Key.Plus;
            case Veldrid.Key.BracketLeft: return Key.BracketLeft;
            case Veldrid.Key.BracketRight: return Key.BracketRight;
            case Veldrid.Key.Semicolon: return Key.Semicolon;
            case Veldrid.Key.Quote: return Key.Quote;
            case Veldrid.Key.Comma: return Key.Comma;
            case Veldrid.Key.Period: return Key.Period;
            case Veldrid.Key.Slash: return Key.Slash;
            case Veldrid.Key.BackSlash: return Key.BackSlash;
            default: return Key.Unknown;
        }
    }

    public static MouseButton ToEngineButton(Veldrid.MouseButton button)
    {
        return (MouseButton)button;
    }
}