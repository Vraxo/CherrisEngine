using System.Collections.Generic;
using Veldrid;

namespace Cherris;

public static class Input
{
    private static readonly HashSet<Key> _pressedKeys = new();

    internal static void UpdateSnapshot(IReadOnlyList<KeyEvent> keyEvents)
    {
        foreach (var keyEvent in keyEvents)
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
    }

    public static bool IsKeyDown(Key key)
    {
        return _pressedKeys.Contains(key);
    }
}