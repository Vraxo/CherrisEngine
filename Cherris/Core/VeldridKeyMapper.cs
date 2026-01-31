namespace Cherris.Core;

public static class VeldridKeyMapper
{
    private static readonly Dictionary<Veldrid.Key, Key> DirectMap = new()
    {
        // Modifiers
        [Veldrid.Key.ShiftLeft] = Key.ShiftLeft,
        [Veldrid.Key.ShiftRight] = Key.ShiftRight,
        [Veldrid.Key.ControlLeft] = Key.ControlLeft,
        [Veldrid.Key.ControlRight] = Key.ControlRight,
        [Veldrid.Key.AltLeft] = Key.AltLeft,
        [Veldrid.Key.AltRight] = Key.AltRight,
        [Veldrid.Key.WinLeft] = Key.WinLeft,
        [Veldrid.Key.WinRight] = Key.WinRight,

        // Navigation
        [Veldrid.Key.Up] = Key.Up,
        [Veldrid.Key.Down] = Key.Down,
        [Veldrid.Key.Left] = Key.Left,
        [Veldrid.Key.Right] = Key.Right,
        [Veldrid.Key.Home] = Key.Home,
        [Veldrid.Key.End] = Key.End,
        [Veldrid.Key.PageUp] = Key.PageUp,
        [Veldrid.Key.PageDown] = Key.PageDown,
        [Veldrid.Key.Insert] = Key.Insert,
        [Veldrid.Key.Delete] = Key.Delete,

        // Action keys
        [Veldrid.Key.Enter] = Key.Enter,
        [Veldrid.Key.Escape] = Key.Escape,
        [Veldrid.Key.Space] = Key.Space,
        [Veldrid.Key.Tab] = Key.Tab,
        [Veldrid.Key.BackSpace] = Key.BackSpace,

        // Lock keys
        [Veldrid.Key.CapsLock] = Key.CapsLock,
        [Veldrid.Key.ScrollLock] = Key.ScrollLock,
        [Veldrid.Key.NumLock] = Key.NumLock,
        [Veldrid.Key.PrintScreen] = Key.PrintScreen,
        [Veldrid.Key.Pause] = Key.Pause,

        // Symbols
        [Veldrid.Key.Tilde] = Key.Tilde,
        [Veldrid.Key.Minus] = Key.Minus,
        [Veldrid.Key.Plus] = Key.Plus,
        [Veldrid.Key.BracketLeft] = Key.BracketLeft,
        [Veldrid.Key.BracketRight] = Key.BracketRight,
        [Veldrid.Key.Semicolon] = Key.Semicolon,
        [Veldrid.Key.Quote] = Key.Quote,
        [Veldrid.Key.Comma] = Key.Comma,
        [Veldrid.Key.Period] = Key.Period,
        [Veldrid.Key.Slash] = Key.Slash,
        [Veldrid.Key.BackSlash] = Key.BackSlash,

        // Other
        [Veldrid.Key.Menu] = Key.Menu,
        [Veldrid.Key.Clear] = Key.Clear,
        [Veldrid.Key.Sleep] = Key.Sleep
    };

    public static Key ToEngineKey(Veldrid.Key key)
    {
        if (key is >= Veldrid.Key.F1 and <= Veldrid.Key.F35)
        {
            return Key.F1 + (key - Veldrid.Key.F1);
        }

        if (key is >= Veldrid.Key.Keypad0 and <= Veldrid.Key.KeypadEnter)
        {
            return Key.Keypad0 + (key - Veldrid.Key.Keypad0);
        }

        if (key is >= Veldrid.Key.A and <= Veldrid.Key.Z)
        {
            return Key.A + (key - Veldrid.Key.A);
        }

        if (key is >= Veldrid.Key.Number0 and <= Veldrid.Key.Number9)
        {
            return Key.Number0 + (key - Veldrid.Key.Number0);
        }

        return DirectMap.TryGetValue(key, out var result)
            ? result
            : Key.Unknown;
    }

    public static MouseButton ToEngineButton(Veldrid.MouseButton button)
    {
        return (MouseButton)button;
    }
}