using Cherris.RenderingInterface;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTKKey = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using OTKGameWindow = OpenTK.Windowing.Desktop.GameWindow;

namespace Cherris.Rendering.OpenTK;

public class OpenTKGameWindow : IGameWindow
{
    internal readonly OTKGameWindow _window;
    private Vector2 _lastMousePos;
    private ImGuiController _imGuiController;

    public bool Exists => _window.Exists && !_window.IsExiting;
    public float Width => _window.ClientSize.X;
    public float Height => _window.ClientSize.Y;
    public event Action Resized;

    public Func<bool> IsViewportActive { get; set; } = () => false;

    public unsafe IntPtr Handle => (IntPtr)_window.WindowPtr;

    public bool IsMouseLocked
    {
        get => Input.IsMouseLocked;
        set => Input.IsMouseLocked = value;
    }

    public OpenTKGameWindow(string title, int width, int height, bool startWithMouseLocked)
    {
        var gameWindowSettings = GameWindowSettings.Default;
        var nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(width, height),
            Title = title,
            NumberOfSamples = 4,
            StencilBits = 8,
            AlphaBits = 8
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
        Input.OnLockStateChanged += OnLockStateChanged;

        _window.MakeCurrent();

        IsMouseLocked = startWithMouseLocked;
    }

    private void OnLockStateChanged(bool locked)
    {
        _window.CursorState = locked
            ? CursorState.Grabbed
            : CursorState.Normal;
    }

    public void SetImGuiController(ImGuiController controller)
    {
        _imGuiController = controller;
    }

    private void OnLoad()
    {
        Input.ClearState();
        _lastMousePos = new Vector2(_window.MouseState.X, _window.MouseState.Y);
    }

    private void OnResize(ResizeEventArgs obj)
    {
        _imGuiController?.WindowResized(obj.Width, obj.Height);
        Resized?.Invoke();
        GL.Viewport(0, 0, obj.Width, obj.Height);
    }

    public void ProcessEvents()
    {
        Input.FrameStarted();
        _window.ProcessEvents(0);
    }

    public void SwapBuffers()
    {
        _window.SwapBuffers();
    }

    private void OnMouseMove(MouseMoveEventArgs e)
    {
        var currentPos = new Vector2(e.X, e.Y);
        _imGuiController?.MouseMove(currentPos);
        Input.SetMousePosition(new System.Numerics.Vector2(currentPos.X, currentPos.Y));

        if (ImGui.GetIO().WantCaptureMouse && !Input.IsMouseButtonDown(MouseButton.Right))
        {
            _lastMousePos = currentPos;
            return;
        }

        if (Input.IsMouseButtonDown(MouseButton.Right))
        {
            var deltaX = currentPos.X - _lastMousePos.X;
            var deltaY = currentPos.Y - _lastMousePos.Y;
            var delta = new System.Numerics.Vector2(deltaX, deltaY);
            Input.SetMouseDelta(delta);
        }

        _lastMousePos = currentPos;
    }

    private void OnMouseDown(MouseButtonEventArgs e)
    {
        bool wantCapture = ImGui.GetIO().WantCaptureMouse;
        _imGuiController?.MouseButton(e.Button, true);

        if (e.Button == global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left && wantCapture)
        {
            return;
        }

        if (e.Button == global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right)
        {
            _lastMousePos = new Vector2(_window.MouseState.X, _window.MouseState.Y);
        }

        Input.SetMouseButtonState(MapButton(e.Button), true);
    }

    private void OnMouseUp(MouseButtonEventArgs e)
    {
        bool wantCapture = ImGui.GetIO().WantCaptureMouse;
        _imGuiController?.MouseButton(e.Button, false);

        if (e.Button == global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left && wantCapture)
        {
            return;
        }

        Input.SetMouseButtonState(MapButton(e.Button), false);
    }

    private void OnKeyDown(KeyboardKeyEventArgs e)
    {
        _imGuiController?.KeyEvent(e.Key, e.IsRepeat, true);
        if (ImGui.GetIO().WantCaptureKeyboard)
        {
            return;
        }

        Input.SetKeyState(MapKey(e.Key), true);
    }

    private void OnKeyUp(KeyboardKeyEventArgs e)
    {
        _imGuiController?.KeyEvent(e.Key, e.IsRepeat, false);
        if (ImGui.GetIO().WantCaptureKeyboard)
        {
            return;
        }

        Input.SetKeyState(MapKey(e.Key), false);
    }

    private void OnTextInput(TextInputEventArgs e)
    {
        _imGuiController?.PressChar((char)e.Unicode);
    }

    private void OnMouseWheel(MouseWheelEventArgs e)
    {
        _imGuiController?.MouseScroll(new Vector2(e.OffsetX, e.OffsetY));

        bool ignoreImGui = IsViewportActive?.Invoke() ?? false;

        if (ImGui.GetIO().WantCaptureMouse && !ignoreImGui)
        {
            return;
        }

        Input.SetMouseWheelDelta(new System.Numerics.Vector2(e.OffsetX, e.OffsetY));
    }

    public void Dispose()
    {
        Input.OnLockStateChanged -= OnLockStateChanged;
        _window?.Dispose();
    }

    private static MouseButton MapButton(global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton button)
    {
        return button switch
        {
            global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left => Cherris.MouseButton.Left,
            global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right => Cherris.MouseButton.Right,
            global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Middle => Cherris.MouseButton.Middle,
            _ => Cherris.MouseButton.LastButton
        };
    }

    private static Key MapKey(OpenTKKey key)
    {
        return key switch
        {
            OpenTKKey.Space => Key.Space,
            OpenTKKey.A => Key.A,
            OpenTKKey.D => Key.D,
            OpenTKKey.S => Key.S,
            OpenTKKey.W => Key.W,
            OpenTKKey.Q => Key.Q,
            OpenTKKey.E => Key.E,
            OpenTKKey.R => Key.R,
            OpenTKKey.Y => Key.Y,
            OpenTKKey.Z => Key.Z,
            OpenTKKey.LeftShift => Key.ShiftLeft,
            OpenTKKey.RightShift => Key.ShiftRight,
            OpenTKKey.LeftControl => Key.ControlLeft,
            OpenTKKey.RightControl => Key.ControlRight,
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