using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Cherris;

public class GameWindow
{
    public Sdl2Window SdlWindow { get; }
    private readonly Vector2 _windowCenter;
    private bool _escapePressedLastFrame = false;

    public bool Exists => SdlWindow.Exists;
    public float Width => SdlWindow.Width;
    public float Height => SdlWindow.Height;

    public GameWindow(string title, int width, int height)
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
        _windowCenter = new Vector2(SdlWindow.Width / 2f, SdlWindow.Height / 2f);

        // Initial mouse setup
        Input.IsMouseLocked = true;
        Sdl2Native.SDL_ShowCursor(0);
        Sdl2Native.SDL_WarpMouseInWindow(SdlWindow.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
        SdlWindow.PumpEvents(); // Pump once to clear the warp event
    }

    public void ProcessEvents()
    {
        InputSnapshot snapshot = SdlWindow.PumpEvents();
        Input.UpdateSnapshot(snapshot, _windowCenter);

        // Toggle mouse lock state on Escape key press
        bool isEscapeDown = Input.IsKeyDown(Key.Escape);
        if (isEscapeDown && !_escapePressedLastFrame)
        {
            Input.IsMouseLocked = !Input.IsMouseLocked;
            Sdl2Native.SDL_ShowCursor(Input.IsMouseLocked ? 0 : 1);
            if (Input.IsMouseLocked)
            {
                // When re-locking, center mouse immediately.
                Sdl2Native.SDL_WarpMouseInWindow(SdlWindow.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
            }
        }
        _escapePressedLastFrame = isEscapeDown;

        // If mouse is locked, re-center it for next frame's delta calculation.
        if (Input.IsMouseLocked && SdlWindow.Exists)
        {
            Sdl2Native.SDL_WarpMouseInWindow(SdlWindow.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
        }
    }
}