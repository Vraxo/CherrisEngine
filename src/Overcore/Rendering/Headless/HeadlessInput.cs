using System.Collections.Concurrent;
using System.Numerics;

namespace Overcore.Rendering.Headless;

/// <summary>
/// Thread-safe entry point for forwarding browser/webview input into the engine's static
/// <see cref="Input"/> state when the host has no native window.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Input"/> is a set of unsynchronized static collections, so it must only be mutated from
/// the render thread. Hosts therefore enqueue commands here (typically from the UI thread) and the
/// render thread drains them at the start of a frame via <see cref="ApplyPendingInput"/>, which
/// <see cref="HeadlessGameWindow.ProcessEvents"/> calls automatically.
/// </para>
/// <para>
/// Coordinates are viewport pixels, not CSS pixels: multiply by the device pixel ratio and subtract
/// the viewport's origin before calling <see cref="SetMousePosition"/>.
/// </para>
/// </remarks>
public static class HeadlessInput
{
    private enum CommandKind
    {
        Key,
        MouseButton,
        MouseMove,
        MousePosition,
        MouseWheel,
        TextInput,
        MouseLocked,
        Reset
    }

    private readonly record struct Command(
        CommandKind Kind,
        Key Key,
        MouseButton Button,
        float X,
        float Y,
        bool State,
        string? Text);

    private static readonly ConcurrentQueue<Command> Pending = new();

    /// <summary>Reports a key going down. Use <see cref="Key.Unknown"/> for unmapped browser keys.</summary>
    public static void PressKey(Key key) => Enqueue(CommandKind.Key, key, default, 0, 0, true);

    /// <summary>Reports a key going up.</summary>
    public static void ReleaseKey(Key key) => Enqueue(CommandKind.Key, key, default, 0, 0, false);

    /// <summary>Reports a key state change without deciding up or down at the call site.</summary>
    public static void SetKey(Key key, bool isDown) => Enqueue(CommandKind.Key, key, default, 0, 0, isDown);

    /// <summary>Queues text input from a keyboard or browser before the next render-thread frame.</summary>
    public static void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Pending.Enqueue(new Command(CommandKind.TextInput, default, default, 0, 0, false, text));
    }

    /// <summary>Reports a mouse button going down.</summary>
    public static void PressMouseButton(MouseButton button) =>
        Enqueue(CommandKind.MouseButton, default, button, 0, 0, true);

    /// <summary>Reports a mouse button going up.</summary>
    public static void ReleaseMouseButton(MouseButton button) =>
        Enqueue(CommandKind.MouseButton, default, button, 0, 0, false);

    /// <summary>Reports a mouse button state change.</summary>
    public static void SetMouseButton(MouseButton button, bool isDown) =>
        Enqueue(CommandKind.MouseButton, default, button, 0, 0, isDown);

    /// <summary>
    /// Reports relative pointer movement, as produced by pointer lock. Deltas accumulate over a frame,
    /// so several browser events between two frames are summed rather than overwritten.
    /// </summary>
    public static void MoveMouse(float deltaX, float deltaY) =>
        Enqueue(CommandKind.MouseMove, default, default, deltaX, deltaY, false);

    /// <summary>Reports the absolute pointer position inside the viewport, in viewport pixels.</summary>
    public static void SetMousePosition(float x, float y) =>
        Enqueue(CommandKind.MousePosition, default, default, x, y, false);

    /// <summary>Reports wheel movement; deltas accumulate over a frame.</summary>
    public static void AddMouseWheel(float deltaX, float deltaY) =>
        Enqueue(CommandKind.MouseWheel, default, default, deltaX, deltaY, false);

    /// <summary>
    /// Reports that the host changed pointer lock state (for example the user pressed Escape and the
    /// browser released the lock). Game code reads the result through <see cref="Input.IsMouseLocked"/>.
    /// </summary>
    public static void SetMouseLocked(bool locked) =>
        Enqueue(CommandKind.MouseLocked, default, default, 0, 0, locked);

    /// <summary>
    /// Drops queued commands and clears held keys and buttons. Use it when the webview loses focus so
    /// a key held during the focus loss cannot stay stuck down.
    /// </summary>
    public static void Reset()
    {
        Pending.Clear();
        Enqueue(CommandKind.Reset, default, default, 0, 0, false);
    }

    /// <summary>
    /// Applies every queued command to <see cref="Input"/>. Must be called from the render thread,
    /// after <see cref="Input"/>'s per-frame state has been reset and before the scene updates.
    /// </summary>
    public static void ApplyPendingInput()
    {
        while (Pending.TryDequeue(out Command command))
        {
            switch (command.Kind)
            {
                case CommandKind.Key:
                    Input.SetKeyState(command.Key, command.State);
                    break;

                case CommandKind.MouseButton:
                    Input.SetMouseButtonState(command.Button, command.State);
                    break;

                case CommandKind.MouseMove:
                    Input.SetMouseDelta(Input.MouseDelta + new Vector2(command.X, command.Y));
                    break;

                case CommandKind.MousePosition:
                    Input.SetMousePosition(new Vector2(command.X, command.Y));
                    break;

                case CommandKind.MouseWheel:
                    Input.SetMouseWheelDelta(Input.MouseWheelDelta + new Vector2(command.X, command.Y));
                    break;

                case CommandKind.TextInput:
                    Input.AddTextInput(command.Text);
                    break;

                case CommandKind.MouseLocked:
                    Input.IsMouseLocked = command.State;
                    break;

                case CommandKind.Reset:
                    Input.ClearState();
                    break;
            }
        }
    }

    private static void Enqueue(CommandKind kind, Key key, MouseButton button, float x, float y, bool state)
    {
        Pending.Enqueue(new Command(kind, key, button, x, y, state, null));
    }
}
