using System.Collections.Generic;
using System.Numerics;
using Veldrid;

namespace Cherris;

public static class Input
{
    private static readonly HashSet<Key> _pressedKeys = new();
    private static Vector2 _lastMousePosition;
    private static bool _firstMouseUpdate = true;

    public static bool IsMouseLocked { get; internal set; } = true;
    public static Vector2 MouseDelta { get; private set; }

    internal static void UpdateSnapshot(InputSnapshot snapshot)
    {
        foreach (var keyEvent in snapshot.KeyEvents)
        {
            if (keyEvent.Down)
            {
                _pressedKeys.Add(keyEvent.Key);
            }
            else
            {
                _pressedKeys.Remove(keyEvent.Key);
            }
        }

        if (!IsMouseLocked)
        {
            MouseDelta = Vector2.Zero;
            // When the mouse is unlocked and then re-locked, we need to treat
            // the next update as the first one to avoid a sudden camera jump.
            _firstMouseUpdate = true;
            return;
        }

        if (_firstMouseUpdate)
        {
            // On the first frame (or after being re-locked), we can't calculate a delta.
            // So we store the initial position and report zero delta.
            MouseDelta = Vector2.Zero;
            _lastMousePosition = snapshot.MousePosition;
            _firstMouseUpdate = false;
        }
        else
        {
            // The delta is the difference between the current and last mouse positions.
            MouseDelta = snapshot.MousePosition - _lastMousePosition;
            _lastMousePosition = snapshot.MousePosition;
        }
    }

    public static bool IsKeyDown(Key key)
    {
        return _pressedKeys.Contains(key);
    }
}