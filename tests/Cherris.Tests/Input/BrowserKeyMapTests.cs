using AwesomeAssertions;
using Cherris.Core;
using Xunit;

namespace Cherris.Tests;

public class BrowserKeyMapTests
{
    [Theory]
    [InlineData("KeyA", Key.A)]
    [InlineData("KeyW", Key.W)]
    [InlineData("KeyZ", Key.Z)]
    [InlineData("Digit0", Key.Number0)]
    [InlineData("Digit9", Key.Number9)]
    [InlineData("Numpad3", Key.Keypad3)]
    [InlineData("NumpadAdd", Key.KeypadAdd)]
    [InlineData("NumpadEnter", Key.KeypadEnter)]
    [InlineData("F1", Key.F1)]
    [InlineData("F12", Key.F12)]
    [InlineData("F24", Key.F24)]
    public void ToKey_MapsLettersDigitsAndFunctionKeys(string code, Key expected)
    {
        BrowserKeyMap.ToKey(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("ArrowUp", Key.Up)]
    [InlineData("ArrowDown", Key.Down)]
    [InlineData("ArrowLeft", Key.Left)]
    [InlineData("ArrowRight", Key.Right)]
    [InlineData("ShiftLeft", Key.ShiftLeft)]
    [InlineData("ControlRight", Key.ControlRight)]
    [InlineData("AltLeft", Key.AltLeft)]
    [InlineData("MetaLeft", Key.WinLeft)]
    [InlineData("MetaRight", Key.WinRight)]
    [InlineData("Space", Key.Space)]
    [InlineData("Escape", Key.Escape)]
    [InlineData("Enter", Key.Enter)]
    [InlineData("Backspace", Key.BackSpace)]
    [InlineData("Tab", Key.Tab)]
    [InlineData("Delete", Key.Delete)]
    [InlineData("PageUp", Key.PageUp)]
    [InlineData("PageDown", Key.PageDown)]
    [InlineData("Backquote", Key.Tilde)]
    [InlineData("Equal", Key.Plus)]
    [InlineData("Backslash", Key.BackSlash)]
    [InlineData("ContextMenu", Key.Menu)]
    public void ToKey_MapsNavigationModifiersAndPunctuation(string code, Key expected)
    {
        BrowserKeyMap.ToKey(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("MediaPlayPause")]
    [InlineData("LaunchMail")]
    [InlineData("")]
    [InlineData(null)]
    public void ToKey_ReturnsUnknownForUnmappedCodes(string? code)
    {
        BrowserKeyMap.ToKey(code).Should().Be(Key.Unknown);
        BrowserKeyMap.TryMapKey(code, out Key key).Should().BeFalse();
        key.Should().Be(Key.Unknown);
    }

    [Fact]
    public void ToKey_IsCaseSensitiveBecauseBrowserCodesAreCanonical()
    {
        BrowserKeyMap.ToKey("keyw").Should().Be(Key.Unknown);
    }

    [Theory]
    [InlineData(0, MouseButton.Left)]
    [InlineData(1, MouseButton.Middle)]
    [InlineData(2, MouseButton.Right)]
    [InlineData(3, MouseButton.Button1)]
    [InlineData(4, MouseButton.Button2)]
    public void TryMapMouseButton_MapsBrowserButtonIndices(int button, MouseButton expected)
    {
        BrowserKeyMap.TryMapMouseButton(button, out MouseButton mapped).Should().BeTrue();
        mapped.Should().Be(expected);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    [InlineData(42)]
    public void TryMapMouseButton_RejectsUnknownButtons(int button)
    {
        BrowserKeyMap.TryMapMouseButton(button, out _).Should().BeFalse();
    }
}
