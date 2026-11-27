using AwesomeAssertions;
using Overcore.Rendering.Headless;
using Xunit;

namespace Overcore.Tests.Input;

public class MouseButtonReleaseTests
{
    [Fact]
    public void Release_IsObservableForTheCurrentInputFrame()
    {
        using HeadlessGameWindow window = new(32, 32);
        HeadlessInput.Reset();
        window.ProcessEvents();

        HeadlessInput.PressMouseButton(global::Overcore.MouseButton.Left);
        window.ProcessEvents();
        global::Overcore.Input.IsMouseButtonDown(global::Overcore.MouseButton.Left).Should().BeTrue();
        global::Overcore.Input.WasMouseButtonPressed(global::Overcore.MouseButton.Left).Should().BeTrue();
        global::Overcore.Input.WasMouseButtonReleased(global::Overcore.MouseButton.Left).Should().BeFalse();

        HeadlessInput.ReleaseMouseButton(global::Overcore.MouseButton.Left);
        window.ProcessEvents();
        global::Overcore.Input.IsMouseButtonDown(global::Overcore.MouseButton.Left).Should().BeFalse();
        global::Overcore.Input.WasMouseButtonReleased(global::Overcore.MouseButton.Left).Should().BeTrue();

        window.ProcessEvents();
        global::Overcore.Input.WasMouseButtonReleased(global::Overcore.MouseButton.Left).Should().BeFalse();
    }

    [Fact]
    public void FocusReset_DoesNotSynthesizeAReleaseEdge()
    {
        using HeadlessGameWindow window = new(32, 32);
        HeadlessInput.Reset();
        window.ProcessEvents();
        HeadlessInput.PressMouseButton(global::Overcore.MouseButton.Left);
        window.ProcessEvents();

        HeadlessInput.Reset();
        window.ProcessEvents();

        global::Overcore.Input.IsMouseButtonDown(global::Overcore.MouseButton.Left).Should().BeFalse();
        global::Overcore.Input.WasMouseButtonReleased(global::Overcore.MouseButton.Left).Should().BeFalse();
    }
}
