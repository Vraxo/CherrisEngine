using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// The transport is Unity's row of play chips: flat rounded rectangles in Unity's raised button grey with a
/// 1px near-black outline and light grey glyphs, sampled from the editor (#505050 faces, #cccccc glyphs,
/// #0f0f0f outlines) and remapped onto theme roles. The buttons stay the honest controls they were — this
/// pins the look, and that the markup still carries the real transport.
/// </summary>
public class TransportSurfaceTests
{
    [Fact]
    public void TheChip_IsUnitysRaisedRectangle()
    {
        string chip = Rule(".transport-button button");

        chip.Should().Contain("color-mix(in srgb, var(--theme-secondary) 75%, var(--theme-panel))",
            "Unity's face is the raised button grey (#505050 sampled): Secondary three parts to Panel one");
        chip.Should().Contain("width: 31px", "the sampled chip is 1.41:1 wide");
        chip.Should().Contain("height: var(--theme-control-height)", "at the theme's control height");
        chip.Should().Contain("border: var(--theme-border-width) solid var(--theme-window-border);",
            "Unity outlines each chip in the near-black that frames the editor's fields (#0f0f0f sampled)");
    }

    [Fact]
    public void Hover_LiftsTheGreyRatherThanASelectionColour()
    {
        Rule(".transport-button button:hover:not(:disabled)").Should().Contain("background: var(--theme-hover);",
            "Unity's flat buttons lighten under the pointer; they do not turn a selection colour");
    }

    [Fact]
    public void ADisabledChip_SitsAtTheStripeGrey()
    {
        string disabled = Rule(".transport-button button:disabled");

        disabled.Should().Contain("color-mix(in srgb, var(--theme-recessed) 50%, var(--theme-panel))",
            "the disabled face is the lighter stripe grey (#323232 sampled), the same mix the rows stripe with");
        disabled.Should().Contain("color-mix(in srgb, var(--theme-foreground) 50%, var(--theme-panel))",
            "and the glyph dims to Foreground halfway to Panel (#858585 sampled)");
    }

    [Fact]
    public void TheButtons_AreStillTheRealTransport()
    {
        string markup = ReadToolBar();

        markup.Should().Contain("@onclick=\"TogglePlayPause\"");
        markup.Should().Contain("@onclick=\"StopPlayback\"");
        markup.Should().Contain("@onclick=\"RestartPlayback\"",
            "the chips are restyled, not replaced: play/pause, stop and restart all still run");
    }

    private static string Rule(string selector)
    {
        // Comments are stripped first: they would otherwise attach to the selector printed in front of them.
        string bare = Regex.Replace(Stylesheet(), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        List<string> bodies = [];

        foreach (Match match in Regex.Matches(bare, @"([^{}]*)\{([^{}]*)\}"))
        {
            bool names = match.Groups[1].Value
                .Split(',')
                .Any(part => string.Equals(part.Trim(), selector, StringComparison.Ordinal));

            if (names)
            {
                bodies.Add(match.Groups[2].Value);
            }
        }

        return bodies.Count > 0
            ? string.Join("\n", bodies)
            : throw new InvalidOperationException($"No {selector} rule in app.css.");
    }

    private static string ReadToolBar() =>
        Read("src", "OvercoreEditorPhotino", "Components", "ToolBar.razor");

    private static string Stylesheet() =>
        Read("src", "OvercoreEditorPhotino", "wwwroot", "css", "app.css");

    private static string Read(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find {Path.Combine(parts)} above {AppContext.BaseDirectory}.");
    }
}
