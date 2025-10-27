using System;
using System.Numerics;
using Cherris.Rendering;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Cherris;

public class GameWindow : IGameWindow
{
    public Sdl2Window SdlWindow { get; }
    private readonly Vector2 _windowCenter;
    private GraphicsDevice _graphicsDevice;

    public bool Exists => SdlWindow.Exists;
    public float Width => SdlWindow.Width;
    public float Height => SdlWindow.Height;
    public event Action Resized;
    public Func<bool> ShouldIgnoreImGuiCapture { get; set; }

    public bool IsMouseLocked
    {
        get => Input.IsMouseLocked;
        set
        {
            Input.IsMouseLocked = value;
            Sdl2Native.SDL_ShowCursor(Input.IsMouseLocked ? 0 : 1);
            if (Input.IsMouseLocked)
            {
                Sdl2Native.SDL_WarpMouseInWindow(SdlWindow.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
            }
        }
    }

    public GameWindow(string title, int width, int height, bool startWithMouseLocked)
    {
        WindowCreateInfo windowCI = new WindowCreateInfo
        {
            X = 100,
            Y = 100,
            WindowWidth = width,
            WindowHeight = height,
            WindowTitle = title
        };
        SdlWindow = VeldridStartup.CreateWindow(ref windowCI);
        SdlWindow.Resized += () => Resized?.Invoke();
        _windowCenter = new Vector2(SdlWindow.Width / 2f, SdlWindow.Height / 2f);

        SdlWindow.KeyDown += (e) => Input.SetKeyState(VeldridKeyMapper.ToEngineKey(e.Key), true);
        SdlWindow.KeyUp += (e) => Input.SetKeyState(VeldridKeyMapper.ToEngineKey(e.Key), false);
        SdlWindow.MouseDown += (e) => Input.SetMouseButtonState(VeldridKeyMapper.ToEngineButton(e.MouseButton), true);
        SdlWindow.MouseUp += (e) => Input.SetMouseButtonState(VeldridKeyMapper.ToEngineButton(e.MouseButton), false);
        SdlWindow.MouseMove += OnMouseMove;

        IsMouseLocked = startWithMouseLocked;
        Input.ClearState();
    }

    public void SetGraphicsDevice(GraphicsDevice gd) => _graphicsDevice = gd;

    private void OnMouseMove(MouseMoveEventArgs e)
    {
        Input.SetMousePosition(e.MousePosition);
        if (IsMouseLocked)
        {
            Input.SetMouseDelta(e.MousePosition - _windowCenter);
        }
    }

    public void ProcessEvents()
    {
        Input.FrameStarted();
        SdlWindow.PumpEvents();

        if (Input.WasKeyPressed(Key.Escape))
        {
            IsMouseLocked = !IsMouseLocked;
        }

        if (IsMouseLocked && SdlWindow.Exists)
        {
            Sdl2Native.SDL_WarpMouseInWindow(SdlWindow.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
        }
    }

    public void SwapBuffers()
    {
        if (_graphicsDevice is not null)
        {
            _graphicsDevice.SwapBuffers();
        }
    }

    public void Dispose()
    {
        if (SdlWindow.Exists)
        {
            SdlWindow.Close();
        }
    }
}

// Helper class to map Veldrid inputs to engine inputs.
public static class VeldridKeyMapper
{
    public static Key ToEngineKey(Veldrid.Key key)
    {
        if (key >= Veldrid.Key.F1 && key <= Veldrid.Key.F35) return (Key)((int)Key.F1 + (int)key - (int)Veldrid.Key.F1);
        if (key >= Veldrid.Key.Keypad0 && key <= Veldrid.Key.KeypadEnter) return (Key)((int)Key.Keypad0 + (int)key - (int)Veldrid.Key.Keypad0);
        if (key >= Veldrid.Key.A && key <= Veldrid.Key.Z) return (Key)((int)Key.A + (int)key - (int)Veldrid.Key.A);
        if (key >= Veldrid.Key.Number0 && key <= Veldrid.Key.Number9) return (Key)((int)Key.Number0 + (int)key - (int)Veldrid.Key.Number9);

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