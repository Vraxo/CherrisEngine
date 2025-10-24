using Cherris.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTKKey = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using OTKGameWindow = OpenTK.Windowing.Desktop.GameWindow;

namespace Cherris.OpenTK;

public class OpenTKGameWindow : IGameWindow
{
    internal readonly OTKGameWindow _window;
    private bool _isMouseLocked;
    private Vector2 _lastMousePos;
    private bool _firstMove = true;
    private ImGuiController _imGuiController;

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

            _window.CursorState = _isMouseLocked 
                ? CursorState.Grabbed 
                : CursorState.Normal;
            
            if (!_isMouseLocked)
            {
                return;
            }

            _firstMove = true; // Reset on re-lock to avoid large delta jumps
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
            StencilBits = 8, // Request an 8-bit stencil buffer for the default framebuffer
            AlphaBits = 8 // Request an 8-bit alpha channel for transparency compositing
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

    public void SetImGuiController(ImGuiController controller)
    {
        _imGuiController = controller;
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
            // Let ImGui handle a potential Escape key press before we toggle mouse lock
            if (_imGuiController?.WantCaptureKeyboard != true)
            {
                IsMouseLocked = !IsMouseLocked;
            }
        }
    }

    public void SwapBuffers()
    {
        _window.SwapBuffers();
    }

    private void OnMouseMove(MouseMoveEventArgs e)
    {
        _imGuiController?.MouseMove(new Vector2(e.X, e.Y));

        if (_imGuiController?.WantCaptureMouse == true)
        {
            Input.SetMouseDelta(System.Numerics.Vector2.Zero);
            return;
        }

        if (IsMouseLocked)
        {
            if (_firstMove)
            {
                _lastMousePos = new Vector2(e.X, e.Y);
                _firstMove = false;
            }

            var deltaX = e.X - _lastMousePos.X;
            var deltaY = e.Y - _lastMousePos.Y;
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
        _imGuiController?.MouseButton((MouseButton)e.Button, true);
        if (_imGuiController?.WantCaptureMouse == true) return;
        Input.SetMouseButtonState(MapButton((MouseButton)e.Button), true);
    }

    private void OnMouseUp(MouseButtonEventArgs e)
    {
        _imGuiController?.MouseButton((MouseButton)e.Button, false);
        if (_imGuiController?.WantCaptureMouse == true) return;
        Input.SetMouseButtonState(MapButton((MouseButton)e.Button), false);
    }

    private void OnKeyDown(KeyboardKeyEventArgs e)
    {
        _imGuiController?.KeyEvent(e.Key, e.IsRepeat, true);
        if (_imGuiController?.WantCaptureKeyboard == true) return;
        Input.SetKeyState(MapKey(e.Key), true);
    }

    private void OnKeyUp(KeyboardKeyEventArgs e)
    {
        _imGuiController?.KeyEvent(e.Key, e.IsRepeat, false);
        if (_imGuiController?.WantCaptureKeyboard == true) return;
        Input.SetKeyState(MapKey(e.Key), false);
    }

    private void OnTextInput(TextInputEventArgs e)
    {
        _imGuiController?.PressChar((char)e.Unicode);
    }

    private void OnMouseWheel(MouseWheelEventArgs e)
    {
        _imGuiController?.MouseScroll(new Vector2(e.OffsetX, e.OffsetY));
        if (_imGuiController?.WantCaptureMouse == true) return;
    }

    public void Dispose()
    {
        _window?.Dispose();
    }

    // --- Input Mapping ---
    private static Cherris.MouseButton MapButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => Cherris.MouseButton.Left,
            MouseButton.Right => Cherris.MouseButton.Right,
            MouseButton.Middle => Cherris.MouseButton.Middle,
            _ => Cherris.MouseButton.LastButton // Indicates an unhandled button
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