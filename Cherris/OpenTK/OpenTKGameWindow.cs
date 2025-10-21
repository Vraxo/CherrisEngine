using System;
using Cherris.Rendering;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using OTKGameWindow = OpenTK.Windowing.Desktop.GameWindow;
using OpenTKKey = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using OpenTKMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;

namespace Cherris;

public class OpenTKGameWindow : IGameWindow
{
    private readonly OTKGameWindow _window;
    private bool _isMouseLocked;
    private Vector2 _lastMousePos;
    private bool _firstMove = true;

    public bool Exists => _window.Exists && !_window.IsExiting;
    public float Width => _window.ClientSize.X;
    public float Height => _window.ClientSize.Y;
    public event Action Resized;

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
        };
        _window = new OTKGameWindow(gameWindowSettings, nativeWindowSettings);

        _window.Load += OnLoad;
        _window.Resize += OnResize;
        _window.KeyDown += OnKeyDown;
        _window.KeyUp += OnKeyUp;
        _window.MouseDown += OnMouseDown;
        _window.MouseUp += OnMouseUp;
        _window.MouseMove += OnMouseMove;

        // This makes the OpenGL context current on this thread.
        _window.MakeCurrent();

        IsMouseLocked = startWithMouseLocked;
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

    private void OnMouseDown(MouseButtonEventArgs e) => Input.SetMouseButtonState(MapButton(e.Button), true);
    private void OnMouseUp(MouseButtonEventArgs e) => Input.SetMouseButtonState(MapButton(e.Button), false);
    private void OnKeyDown(KeyboardKeyEventArgs e) => Input.SetKeyState(MapKey(e.Key), true);
    private void OnKeyUp(KeyboardKeyEventArgs e) => Input.SetKeyState(MapKey(e.Key), false);

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
}