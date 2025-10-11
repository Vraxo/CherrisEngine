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

    // This flag tracks whether we are waiting for the OS to process a mouse warp
    // that brings the cursor back to the center of the screen after re-locking.
    private static bool _awaitingMouseCentering;

    public static bool IsMouseLocked { get; internal set; } = true;
    public static Vector2 MouseDelta { get; private set; }
    public static Vector2 MousePosition { get; private set; }

    internal static void UpdateSnapshot(InputSnapshot snapshot, Vector2 windowCenter)
    {
        _justPressedMouseButtons.Clear();

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

        foreach (var mouseEvent in snapshot.MouseEvents)
        {
            if (mouseEvent.Down)
            {
                if (_pressedMouseButtons.Add(mouseEvent.MouseButton))
                {
                    _justPressedMouseButtons.Add(mouseEvent.MouseButton);
                }
            }
            else
            {
                _pressedMouseButtons.Remove(mouseEvent.MouseButton);
            }
        }

        MousePosition = snapshot.MousePosition;

        if (!IsMouseLocked)
        {
            MouseDelta = Vector2.Zero;
            // When the mouse is unlocked, we will need to confirm it has been
            // centered again after it is re-locked.
            _awaitingMouseCentering = true;
            return;
        }

        // At this point, the mouse is locked.

        if (_awaitingMouseCentering)
        {
            // Check if the mouse has been centered by the warp. A small tolerance is used.
            float distanceFromCenter = Vector2.Distance(snapshot.MousePosition, windowCenter);
            if (distanceFromCenter > 1.0f)
            {
                // The warp hasn't been processed by the OS yet. The mouse position is stale.
                // We ignore the delta for this frame and wait for a centered position.
                MouseDelta = Vector2.Zero;
                return;
            }
            else
            {
                // The mouse is now centered. We can stop waiting.
                _awaitingMouseCentering = false;
            }
        }

        // The delta is the difference between the current mouse position
        // and the center of the screen (where it was warped to last frame).
        MouseDelta = snapshot.MousePosition - windowCenter;
    }

    public static bool IsKeyDown(Key key)
    {
        return _pressedKeys.Contains(key);
    }

    public static bool WasMouseButtonPressed(MouseButton button)
    {
        return _justPressedMouseButtons.Contains(button);
    }
}