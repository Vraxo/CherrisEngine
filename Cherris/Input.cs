using System;
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

        // After re-locking, the mouse position from the OS can be stale for a frame or two.
        // A valid, post-warp position will be very close to the center of the screen. We can
        // reject any position far from the center as it is almost certainly stale data.
        float distanceFromCenter = Vector2.Distance(snapshot.MousePosition, windowCenter);

        // A large distance implies stale data. The _firstMouseUpdate check handles the guaranteed first frame.
        bool isStaleData = distanceFromCenter > windowCenter.X * 0.9f;

        if (_firstMouseUpdate || isStaleData)
        {
            // On the first frame after being locked, or if we detect stale data, ignore the delta.
            MouseDelta = Vector2.Zero;
            _firstMouseUpdate = false; // Consume the flag; the stale data check will protect subsequent frames.
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