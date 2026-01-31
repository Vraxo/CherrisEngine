using Cherris.RenderingInterface;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Cherris.Core;

public class GameWindow : IGameWindow
{
    public Sdl2Window SdlWindow { get; }
    private readonly Vector2 _windowCenter;
    private GraphicsDevice _graphicsDevice;

    public bool Exists => SdlWindow.Exists;
    public float Width => SdlWindow.Width;
    public float Height => SdlWindow.Height;
    public event Action Resized;

    public Func<bool> IsViewportActive { get; set; } = () => false;

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
        WindowCreateInfo windowCI = new()
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

    public void SetGraphicsDevice(GraphicsDevice gd)
    {
        _graphicsDevice = gd;
    }

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
        _graphicsDevice?.SwapBuffers();
    }

    public void Dispose()
    {
        if (SdlWindow.Exists)
        {
            SdlWindow.Close();
        }
    }
}