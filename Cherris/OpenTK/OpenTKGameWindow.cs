using System;
using Cherris.Rendering;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OTKGameWindow = OpenTK.Windowing.Desktop.GameWindow;
using OpenTKKey = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using OpenTKMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
using DirectUI.Input;
using DM = DirectUI;

namespace Cherris;

public class OpenTKGameWindow : IGameWindow
{
    internal readonly OTKGameWindow _window;
    private bool _isMouseLocked;
    private Vector2 _lastMousePos;
    private bool _firstMove = true;
    private InputManager _directUIInputManager;

    public bool Exists => _window.Exists && !_window.IsExiting;
    public float Width => _window.ClientSize.X;
    public float Height => _window.ClientSize.Y;
    public event Action Resized;

    public unsafe IntPtr Handle => (IntPtr)_window.WindowPtr;

    public bool IsMouseLocked
    {
        get => _isMouseLocked;
        set
        {
            _isMouseLocked = value;
            _window.CursorState = _isMouseLocked ? CursorState.Grabbed : CursorState.Normal;
            if (_isMouseLocked)
            {
                _firstMove = true; // Reset on re-lock to avoid large delta jumps
            }
        }
    }

    public OpenTKGameWindow(string title, int width, int height, bool startWithMouseLocked)
    {
        var gameWindowSettings = GameWindowSettings.Default;
        var nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(width, height),
            Title = title,
            NumberOfSamples = 4, // Request 4x MSAA
            StencilBits = 8 // Request an 8-bit stencil buffer for the default framebuffer
        };
        _window = new OTKGameWindow(gameWindowSettings, nativeWindowSettings);

        _window.Load += OnLoad;
        _window.Resize += OnResize;
        _window.KeyDown += OnKeyDown;
        _window.KeyUp += OnKeyUp;
        _window.MouseDown += OnMouseDown;
        _window.MouseUp += OnMouseUp;
        _window.MouseMove += OnMouseMove;
        _window.TextInput += OnTextInput;
        _window.MouseWheel += OnMouseWheel;


        // This makes the OpenGL context current on this thread.
        _window.MakeCurrent();

        IsMouseLocked = startWithMouseLocked;
    }

    public void SetInputManager(InputManager manager)
    {
        _directUIInputManager = manager;
    }

    private void OnLoad()
    {
        Input.ClearState(); // Ensure clean state on startup
        _lastMousePos = new Vector2(_window.MouseState.X, _window.MouseState.Y);
    }

    private void OnResize(ResizeEventArgs obj)
    {
        Resized?.Invoke();
        GL.Viewport(0, 0, obj.Width, obj.Height);
    }

    public void ProcessEvents()
    {
        Input.FrameStarted();
        _directUIInputManager?.PrepareNextFrame();
        // OpenTK's GameWindow processes events on its own thread via Run(),
        // but we need to process them manually for our game loop. A timeout of 0
        // processes all pending events and returns immediately.
        _window.ProcessEvents(0);

        if (Input.WasKeyPressed(Key.Escape))
        {
            IsMouseLocked = !IsMouseLocked;
        }
    }

    public void SwapBuffers()
    {
        _window.SwapBuffers();
    }

    private void OnMouseMove(MouseMoveEventArgs e)
    {
        _directUIInputManager?.SetMousePosition((int)e.X, (int)e.Y);

        if (IsMouseLocked)
        {
            if (_firstMove)
            {
                _lastMousePos = new Vector2(e.X, e.Y);
                _firstMove = false;
            }

            var deltaX = e.X - _lastMousePos.X;
            var deltaY = e.Y - _lastMousePos.Y; // OpenTK's Y-axis is inverted compared to Veldrid for mouse movement
            _lastMousePos = new Vector2(e.X, e.Y);

            Input.SetMouseDelta(new System.Numerics.Vector2(deltaX, deltaY));
        }
        else
        {
            Input.SetMouseDelta(System.Numerics.Vector2.Zero);
        }
        Input.SetMousePosition(new System.Numerics.Vector2(e.X, e.Y));
    }

    private void OnMouseDown(MouseButtonEventArgs e)
    {
        _directUIInputManager?.SetMouseDown(MapDirectUIButton(e.Button));
        Input.SetMouseButtonState(MapButton(e.Button), true);
    }

    private void OnMouseUp(MouseButtonEventArgs e)
    {
        _directUIInputManager?.SetMouseUp(MapDirectUIButton(e.Button));
        Input.SetMouseButtonState(MapButton(e.Button), false);
    }

    private void OnKeyDown(KeyboardKeyEventArgs e)
    {
        _directUIInputManager?.AddKeyPressed(MapDirectUIKey(e.Key));
        Input.SetKeyState(MapKey(e.Key), true);
    }

    private void OnKeyUp(KeyboardKeyEventArgs e)
    {
        _directUIInputManager?.AddKeyReleased(MapDirectUIKey(e.Key));
        Input.SetKeyState(MapKey(e.Key), false);
    }

    private void OnTextInput(TextInputEventArgs e)
    {
        _directUIInputManager?.AddCharacterInput((char)e.Unicode);
    }

    private void OnMouseWheel(MouseWheelEventArgs e)
    {
        _directUIInputManager?.AddMouseWheelDelta(e.OffsetY);
    }

    public void Dispose()
    {
        _window?.Dispose();
    }

    // --- Input Mapping ---
    private static MouseButton MapButton(OpenTKMouseButton button)
    {
        return button switch
        {
            OpenTKMouseButton.Left => MouseButton.Left,
            OpenTKMouseButton.Right => MouseButton.Right,
            OpenTKMouseButton.Middle => MouseButton.Middle,
            _ => MouseButton.LastButton // Indicates an unhandled button
        };
    }

    private static DM.MouseButton MapDirectUIButton(OpenTKMouseButton button)
    {
        return button switch
        {
            OpenTKMouseButton.Left => DM.MouseButton.Left,
            OpenTKMouseButton.Right => DM.MouseButton.Right,
            OpenTKMouseButton.Middle => DM.MouseButton.Middle,
            _ => DM.MouseButton.Left // default
        };
    }

    private static Key MapKey(OpenTKKey key)
    {
        // This is a partial mapping. A full implementation would be much larger.
        return key switch
        {
            OpenTKKey.Space => Key.Space,
            OpenTKKey.A => Key.A,
            OpenTKKey.D => Key.D,
            OpenTKKey.S => Key.S,
            OpenTKKey.W => Key.W,
            OpenTKKey.LeftShift => Key.ShiftLeft,
            OpenTKKey.RightShift => Key.ShiftRight,
            OpenTKKey.Escape => Key.Escape,
            OpenTKKey.F12 => Key.F12,
            OpenTKKey.Up => Key.Up,
            OpenTKKey.Down => Key.Down,
            OpenTKKey.Left => Key.Left,
            OpenTKKey.Right => Key.Right,
            _ => Key.Unknown
        };
    }

    private static DirectUI.Keys MapDirectUIKey(OpenTKKey key) => key switch
    {
        OpenTKKey.Space => DirectUI.Keys.Space,
        OpenTKKey.D0 => DirectUI.Keys.D0,
        OpenTKKey.D1 => DirectUI.Keys.D1,
        OpenTKKey.D2 => DirectUI.Keys.D2,
        OpenTKKey.D3 => DirectUI.Keys.D3,
        OpenTKKey.D4 => DirectUI.Keys.D4,
        OpenTKKey.D5 => DirectUI.Keys.D5,
        OpenTKKey.D6 => DirectUI.Keys.D6,
        OpenTKKey.D7 => DirectUI.Keys.D7,
        OpenTKKey.D8 => DirectUI.Keys.D8,
        OpenTKKey.D9 => DirectUI.Keys.D9,
        OpenTKKey.A => DirectUI.Keys.A,
        OpenTKKey.B => DirectUI.Keys.B,
        OpenTKKey.C => DirectUI.Keys.C,
        OpenTKKey.D => DirectUI.Keys.D,
        OpenTKKey.E => DirectUI.Keys.E,
        OpenTKKey.F => DirectUI.Keys.F,
        OpenTKKey.G => DirectUI.Keys.G,
        OpenTKKey.H => DirectUI.Keys.H,
        OpenTKKey.I => DirectUI.Keys.I,
        OpenTKKey.J => DirectUI.Keys.J,
        OpenTKKey.K => DirectUI.Keys.K,
        OpenTKKey.L => DirectUI.Keys.L,
        OpenTKKey.M => DirectUI.Keys.M,
        OpenTKKey.N => DirectUI.Keys.N,
        OpenTKKey.O => DirectUI.Keys.O,
        OpenTKKey.P => DirectUI.Keys.P,
        OpenTKKey.Q => DirectUI.Keys.Q,
        OpenTKKey.R => DirectUI.Keys.R,
        OpenTKKey.S => DirectUI.Keys.S,
        OpenTKKey.T => DirectUI.Keys.T,
        OpenTKKey.U => DirectUI.Keys.U,
        OpenTKKey.V => DirectUI.Keys.V,
        OpenTKKey.W => DirectUI.Keys.W,
        OpenTKKey.X => DirectUI.Keys.X,
        OpenTKKey.Y => DirectUI.Keys.Y,
        OpenTKKey.Z => DirectUI.Keys.Z,
        OpenTKKey.Escape => DirectUI.Keys.Escape,
        OpenTKKey.Enter => DirectUI.Keys.Enter,
        OpenTKKey.Tab => DirectUI.Keys.Tab,
        OpenTKKey.Backspace => DirectUI.Keys.Backspace,
        OpenTKKey.Insert => DirectUI.Keys.Insert,
        OpenTKKey.Delete => DirectUI.Keys.Delete,
        OpenTKKey.Right => DirectUI.Keys.RightArrow,
        OpenTKKey.Left => DirectUI.Keys.LeftArrow,
        OpenTKKey.Down => DirectUI.Keys.DownArrow,
        OpenTKKey.Up => DirectUI.Keys.UpArrow,
        OpenTKKey.PageUp => DirectUI.Keys.PageUp,
        OpenTKKey.PageDown => DirectUI.Keys.PageDown,
        OpenTKKey.Home => DirectUI.Keys.Home,
        OpenTKKey.End => DirectUI.Keys.End,
        OpenTKKey.F1 => DirectUI.Keys.F1,
        OpenTKKey.F2 => DirectUI.Keys.F2,
        OpenTKKey.F3 => DirectUI.Keys.F3,
        OpenTKKey.F4 => DirectUI.Keys.F4,
        OpenTKKey.F5 => DirectUI.Keys.F5,
        OpenTKKey.F6 => DirectUI.Keys.F6,
        OpenTKKey.F7 => DirectUI.Keys.F7,
        OpenTKKey.F8 => DirectUI.Keys.F8,
        OpenTKKey.F9 => DirectUI.Keys.F9,
        OpenTKKey.F10 => DirectUI.Keys.F10,
        OpenTKKey.F11 => DirectUI.Keys.F11,
        OpenTKKey.F12 => DirectUI.Keys.F12,
        OpenTKKey.LeftShift => DirectUI.Keys.Shift,
        OpenTKKey.RightShift => DirectUI.Keys.Shift,
        OpenTKKey.LeftControl => DirectUI.Keys.Control,
        OpenTKKey.RightControl => DirectUI.Keys.Control,
        OpenTKKey.LeftAlt => DirectUI.Keys.Alt,
        OpenTKKey.RightAlt => DirectUI.Keys.Alt,
        _ => DirectUI.Keys.Unknown,
    };
}