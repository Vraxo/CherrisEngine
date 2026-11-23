namespace Cherris.Core;

/// <summary>
/// Maps browser input identifiers onto the engine's input enums, for hosts whose UI is a webview or a
/// browser page (see <c>Cherris.Rendering.Headless</c>).
/// </summary>
/// <remarks>
/// <para>
/// Keys are matched against <c>KeyboardEvent.code</c> rather than <c>key</c>, because <c>code</c> is the
/// physical key and therefore layout independent: the same physical key maps to the same engine
/// <see cref="Key"/> on QWERTY, AZERTY and Dvorak.
/// </para>
/// <para>
/// Mouse buttons use <c>MouseEvent.button</c>: 0 left, 1 middle, 2 right, 3 and 4 the two side buttons.
/// </para>
/// </remarks>
public static class BrowserKeyMap
{
    private static readonly Dictionary<string, Key> DirectMap = new(StringComparer.Ordinal)
    {
        // Modifiers
        ["ShiftLeft"] = Key.ShiftLeft,
        ["ShiftRight"] = Key.ShiftRight,
        ["ControlLeft"] = Key.ControlLeft,
        ["ControlRight"] = Key.ControlRight,
        ["AltLeft"] = Key.AltLeft,
        ["AltRight"] = Key.AltRight,
        ["MetaLeft"] = Key.WinLeft,
        ["MetaRight"] = Key.WinRight,
        ["ContextMenu"] = Key.Menu,

        // Navigation
        ["ArrowUp"] = Key.Up,
        ["ArrowDown"] = Key.Down,
        ["ArrowLeft"] = Key.Left,
        ["ArrowRight"] = Key.Right,
        ["Home"] = Key.Home,
        ["End"] = Key.End,
        ["PageUp"] = Key.PageUp,
        ["PageDown"] = Key.PageDown,
        ["Insert"] = Key.Insert,
        ["Delete"] = Key.Delete,

        // Action keys
        ["Enter"] = Key.Enter,
        ["NumpadEnter"] = Key.KeypadEnter,
        ["Escape"] = Key.Escape,
        ["Space"] = Key.Space,
        ["Tab"] = Key.Tab,
        ["Backspace"] = Key.BackSpace,

        // Lock keys
        ["CapsLock"] = Key.CapsLock,
        ["ScrollLock"] = Key.ScrollLock,
        ["NumLock"] = Key.NumLock,
        ["PrintScreen"] = Key.PrintScreen,
        ["Pause"] = Key.Pause,
        ["Clear"] = Key.Clear,

        // Punctuation
        ["Backquote"] = Key.Tilde,
        ["Minus"] = Key.Minus,
        ["Equal"] = Key.Plus,
        ["BracketLeft"] = Key.BracketLeft,
        ["BracketRight"] = Key.BracketRight,
        ["Semicolon"] = Key.Semicolon,
        ["Quote"] = Key.Quote,
        ["Comma"] = Key.Comma,
        ["Period"] = Key.Period,
        ["Slash"] = Key.Slash,
        ["Backslash"] = Key.BackSlash,
        ["IntlBackslash"] = Key.BackSlash,
        ["IntlYen"] = Key.BackSlash,

        // Keypad operators
        ["NumpadAdd"] = Key.KeypadAdd,
        ["NumpadSubtract"] = Key.KeypadSubtract,
        ["NumpadMultiply"] = Key.KeypadMultiply,
        ["NumpadDivide"] = Key.KeypadDivide,
        ["NumpadDecimal"] = Key.KeypadDecimal
    };

    static BrowserKeyMap()
    {
        // Letters, digits and function keys follow a fixed naming pattern in KeyboardEvent.code.
        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            DirectMap[$"Key{letter}"] = Enum.Parse<Key>(letter.ToString());
        }

        for (int digit = 0; digit <= 9; digit++)
        {
            DirectMap[$"Digit{digit}"] = Enum.Parse<Key>($"Number{digit}");
            DirectMap[$"Numpad{digit}"] = Enum.Parse<Key>($"Keypad{digit}");
        }

        for (int function = 1; function <= 24; function++)
        {
            DirectMap[$"F{function}"] = Enum.Parse<Key>($"F{function}");
        }
    }

    /// <summary>
    /// Translates a <c>KeyboardEvent.code</c> value (for example <c>"KeyW"</c> or <c>"ArrowLeft"</c>)
    /// into an engine <see cref="Key"/>.
    /// </summary>
    /// <returns><see cref="Key.Unknown"/> for unmapped or missing codes.</returns>
    public static Key ToKey(string? code)
    {
        return TryMapKey(code, out Key key) ? key : Key.Unknown;
    }

    /// <summary>
    /// Translates a <c>KeyboardEvent.code</c> value into an engine <see cref="Key"/>.
    /// </summary>
    /// <returns>False when the code is null, empty or has no engine equivalent.</returns>
    public static bool TryMapKey(string? code, out Key key)
    {
        if (string.IsNullOrEmpty(code))
        {
            key = Key.Unknown;
            return false;
        }

        return DirectMap.TryGetValue(code, out key);
    }

    /// <summary>
    /// Translates a <c>MouseEvent.button</c> value into an engine <see cref="MouseButton"/>.
    /// </summary>
    /// <returns>False for buttons the engine does not model (5 and above).</returns>
    public static bool TryMapMouseButton(int button, out MouseButton mapped)
    {
        switch (button)
        {
            case 0:
                mapped = MouseButton.Left;
                return true;
            case 1:
                mapped = MouseButton.Middle;
                return true;
            case 2:
                mapped = MouseButton.Right;
                return true;
            case 3:
                mapped = MouseButton.Button1;
                return true;
            case 4:
                mapped = MouseButton.Button2;
                return true;
            default:
                mapped = MouseButton.Left;
                return false;
        }
    }
}
