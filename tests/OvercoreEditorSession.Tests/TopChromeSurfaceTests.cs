using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// The two strips at the top of the editor — the menu bar and the transport — are one piece of chrome: the
/// same rows on the same background at the theme's control height, and one interaction language for their
/// controls. An open menu title uses the same grey lift as a hover, never the selection blue the UE5 style
/// set paints a held title with.
/// </summary>
public class TopChromeSurfaceTests
{
    [Fact]
    public void TheStrips_ShareOneRhythm()
    {
        foreach (string strip in new[] { Rule("menubar"), Rule("editor-toolbar-strip") })
        {
            strip.Should().Contain("padding: 4px 12px;", "the menu bar and the transport breathe the same");
            strip.Should().Contain("background: var(--theme-background);");
            strip.Should().Contain("border-bottom: var(--theme-border-width) solid var(--theme-window-border);",
                "and are seamed by the same hairline");
        }
    }

    [Fact]
    public void TheRows_AreTheThemesControlHeight()
    {
        Rule("menubar-brand").Should().Contain("height: var(--theme-control-height);",
            "the brand stands as tall as the transport chips");
        Rule("menubar-button").Should().Contain("height: var(--theme-control-height);",
            "a menu title and a transport chip are the same row");
    }

    [Fact]
    public void AnOpenMenuTitle_UsesTheGreyLift()
    {
        string open = Rule("menubar-button.open");

        open.Should().Contain("background: var(--theme-hover);",
            "a held title stays grey, the way Unity's does");
        open.Should().NotContain("var(--theme-primary)", "no selection blue in the top chrome");
    }

    [Fact]
    public void TheMenus_SayWhatTheyOpen()
    {
        string markup = ReadMenuBar();

        markup.Should().Contain("aria-haspopup=\"menu\"");
        markup.Should().Contain("aria-expanded=",
            "every menu title reports its state, the way the hierarchy's create button does");
        markup.Should().Contain("role=\"menubar\"");
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
                .Any(part => string.Equals(part.Trim(), "." + selector, StringComparison.Ordinal));

            if (names)
            {
                bodies.Add(match.Groups[2].Value);
            }
        }

        return bodies.Count > 0
            ? string.Join("\n", bodies)
            : throw new InvalidOperationException($"No .{selector} rule in app.css.");
    }

    private static string ReadMenuBar() =>
        Read("src", "OvercoreEditorPhotino", "Components", "MenuBar.razor");

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
