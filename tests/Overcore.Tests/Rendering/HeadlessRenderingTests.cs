using AwesomeAssertions;
using Overcore.Rendering.Headless;
using System.Diagnostics;
using System.Numerics;
using Xunit;

namespace Overcore.Tests;

/// <summary>
/// Tests for the headless (off-screen) render path used by webview-hosted UIs. All tests live in one
/// class because the engine's <see cref="Input"/> state is static and tests in the same class run
/// sequentially.
/// </summary>
public class HeadlessRenderingTests
{
    [Fact]
    public void HeadlessGameWindow_Resize_UpdatesSizeAndRaisesResized()
    {
        using var window = new HeadlessGameWindow(800, 600);
        int resizedCount = 0;
        window.Resized += () => resizedCount++;

        window.Resize(1280, 720);

        window.Width.Should().Be(1280f);
        window.Height.Should().Be(720f);
        window.PixelWidth.Should().Be(1280);
        window.PixelHeight.Should().Be(720);
        resizedCount.Should().Be(1);
    }

    [Fact]
    public void HeadlessGameWindow_Resize_IgnoresSubPixelChanges()
    {
        using var window = new HeadlessGameWindow(800, 600);
        int resizedCount = 0;
        window.Resized += () => resizedCount++;

        window.Resize(800, 600);
        window.Resize(800, 600);

        resizedCount.Should().Be(0);
        window.Width.Should().Be(800f);
    }

    [Fact]
    public void HeadlessGameWindow_Resize_ClampsToAtLeastOnePixel()
    {
        using var window = new HeadlessGameWindow(800, 600);

        window.Resize(0, -20);

        window.Width.Should().Be(1f);
        window.Height.Should().Be(1f);
    }

    [Fact]
    public void HeadlessGameWindow_Close_StopsTheGameLoop()
    {
        var window = new HeadlessGameWindow(320, 200);
        window.Exists.Should().BeTrue();

        window.Close();

        window.Exists.Should().BeFalse();
        window.Dispose();
        window.Exists.Should().BeFalse();
    }

    [Fact]
    public void HeadlessGameWindow_ProcessEvents_AppliesQueuedInput()
    {
        using var window = new HeadlessGameWindow(800, 600);

        HeadlessInput.PressKey(Key.W);
        window.ProcessEvents();

        Input.IsKeyDown(Key.W).Should().BeTrue();
        Input.WasKeyPressed(Key.W).Should().BeTrue();
    }

    [Fact]
    public void HeadlessInput_KeyPress_IsJustPressedForExactlyOneFrame()
    {
        using var window = new HeadlessGameWindow(800, 600);

        HeadlessInput.PressKey(Key.W);
        window.ProcessEvents();

        window.ProcessEvents();

        Input.IsKeyDown(Key.W).Should().BeTrue();
        Input.WasKeyPressed(Key.W).Should().BeFalse();

        HeadlessInput.ReleaseKey(Key.W);
        window.ProcessEvents();

        Input.IsKeyDown(Key.W).Should().BeFalse();
    }

    [Fact]
    public void HeadlessInput_MouseMove_AccumulatesWithinAFrame()
    {
        using var window = new HeadlessGameWindow(800, 600);

        HeadlessInput.MoveMouse(10, 5);
        HeadlessInput.MoveMouse(-4, 2);
        window.ProcessEvents();

        Input.MouseDelta.Should().Be(new Vector2(6, 7));

        window.ProcessEvents();

        Input.MouseDelta.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void HeadlessInput_MouseWheel_AccumulatesWithinAFrame()
    {
        using var window = new HeadlessGameWindow(800, 600);

        HeadlessInput.AddMouseWheel(0, 1);
        HeadlessInput.AddMouseWheel(0, 2);
        window.ProcessEvents();

        Input.MouseWheelDelta.Should().Be(new Vector2(0, 3));

        window.ProcessEvents();

        Input.MouseWheelDelta.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void HeadlessInput_MousePosition_IsInViewportPixels()
    {
        using var window = new HeadlessGameWindow(800, 600);

        HeadlessInput.SetMousePosition(120.5f, 64f);
        window.ProcessEvents();

        Input.MousePosition.Should().Be(new Vector2(120.5f, 64f));
    }

    [Fact]
    public void HeadlessInput_MouseLock_FlowsThroughToWindowEvent()
    {
        using var window = new HeadlessGameWindow(800, 600);
        bool? reported = null;
        window.MouseLockChanged += locked => reported = locked;

        HeadlessInput.SetMouseLocked(true);
        window.ProcessEvents();

        Input.IsMouseLocked.Should().BeTrue();
        reported.Should().Be(true);

        HeadlessInput.SetMouseLocked(false);
        window.ProcessEvents();

        Input.IsMouseLocked.Should().BeFalse();
        reported.Should().Be(false);
    }

    [Fact]
    public void HeadlessInput_GameCodeMouseLock_RaisesWindowEvent()
    {
        using var window = new HeadlessGameWindow(800, 600);
        bool? reported = null;
        window.MouseLockChanged += locked => reported = locked;

        Input.IsMouseLocked = true;

        reported.Should().Be(true);

        Input.IsMouseLocked = false;
        reported.Should().Be(false);
    }

    [Fact]
    public void HeadlessInput_Reset_ClearsHeldKeys()
    {
        using var window = new HeadlessGameWindow(800, 600);

        HeadlessInput.PressKey(Key.A);
        window.ProcessEvents();
        Input.IsKeyDown(Key.A).Should().BeTrue();

        HeadlessInput.Reset();
        window.ProcessEvents();

        Input.IsKeyDown(Key.A).Should().BeFalse();
    }

    [Fact]
    public void HeadlessGameWindow_WithFrameCapDisabled_DoesNotThrottle()
    {
        using var window = new HeadlessGameWindow(64, 64) { TargetFrameRate = 0 };
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < 10; i++)
        {
            window.SwapBuffers();
        }

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SceneFrameCapture_FlipRows_ReversesRowOrder()
    {
        // Three rows of two bytes, in OpenGL bottom-up order.
        byte[] pixels = [1, 2, 3, 4, 5, 6];

        SceneFrameCapture.FlipRows(pixels, 2, 3);

        pixels.Should().Equal(new byte[] { 5, 6, 3, 4, 1, 2 });
    }

    [Fact]
    public void SceneFrameCapture_FlipRows_IsANoOpForASingleRow()
    {
        byte[] pixels = [1, 2, 3, 4];

        SceneFrameCapture.FlipRows(pixels, 4, 1);

        pixels.Should().Equal(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void SceneFrameCapture_FlipRows_RejectsUndersizedBuffers()
    {
        Action flip = () => SceneFrameCapture.FlipRows(new byte[4], 8, 2);

        flip.Should().Throw<ArgumentException>();
    }
}
