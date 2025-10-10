using System.Collections.Generic;
using System.Numerics;
using Veldrid;

namespace Cherris;

public static class Input
{
    private static readonly HashSet<Key> _pressedKeys = new();
    private static bool _firstMouseUpdate = true;

    public static bool IsMouseLocked { get; internal set; } = true;
    public static Vector2 MouseDelta { get; private set; }

    internal static void UpdateSnapshot(InputSnapshot snapshot, Vector2 windowCenter)
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
            // On the first frame after being locked, we ignore the delta
            // to avoid a jump from the cursor's unlocked position. The cursor
            // will be warped to the center by the Engine, and subsequent deltas
            // will be correct.
            MouseDelta = Vector2.Zero;
            _firstMouseUpdate = false;
        }
        else
        {
            // The delta is the difference between the current mouse position
            // and the center of the screen (where it was warped to last frame).
            MouseDelta = snapshot.MousePosition - windowCenter;
        }
    }

    public static bool IsKeyDown(Key key)
    {
        return _pressedKeys.Contains(key);
    }
}