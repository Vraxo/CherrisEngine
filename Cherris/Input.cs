using System.Collections.Generic;
using System.Numerics;
using Veldrid;

namespace Cherris;

public static class Input
{
    private static readonly HashSet<Key> _pressedKeys = new();
    private static bool _firstMouseUpdate = true;

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

        if (_firstMouseUpdate)
        {
            MouseDelta = Vector2.Zero;
            _firstMouseUpdate = false;
        }
        else
        {
            MouseDelta = snapshot.MousePosition;
        }
    }

    public static bool IsKeyDown(Key key)
    {
        return _pressedKeys.Contains(key);
    }
}