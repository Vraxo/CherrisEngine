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
    private bool _escapePressedLastFrame = false;

    public bool Exists => SdlWindow.Exists;
    public float Width => SdlWindow.Width;
    public float Height => SdlWindow.Height;

    public event Action Resized;

    public bool IsMouseLocked
    {
        get => Input.IsMouseLocked;
        set => Input.IsMouseLocked = value;
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

        Input.IsMouseLocked = startWithMouseLocked;
        Sdl2Native.SDL_ShowCursor(startWithMouseLocked ? 0 : 1);
        if (startWithMouseLocked)
        {
            Sdl2Native.SDL_WarpMouseInWindow(SdlWindow.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
            SdlWindow.PumpEvents();
        }
    }

    public void ProcessEvents()
    {
        InputSnapshot snapshot = SdlWindow.PumpEvents();
        Input.UpdateSnapshot(snapshot, _windowCenter);

        bool isEscapeDown = Input.IsKeyDown(Key.Escape);
        if (isEscapeDown && !_escapePressedLastFrame)
        {
            Input.IsMouseLocked = !Input.IsMouseLocked;
            Sdl2Native.SDL_ShowCursor(Input.IsMouseLocked ? 0 : 1);
            if (Input.IsMouseLocked)
            {
                Sdl2Native.SDL_WarpMouseInWindow(SdlWindow.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
            }
        }
        _escapePressedLastFrame = isEscapeDown;

        if (Input.IsMouseLocked && SdlWindow.Exists)
        {
            Sdl2Native.SDL_WarpMouseInWindow(SdlWindow.SdlWindowHandle, (int)_windowCenter.X, (int)_windowCenter.Y);
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