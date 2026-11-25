using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Panel tabs are Unity's docked-window tabs, sampled from the editor: a dark well with no divider line under
/// the tab text, and an active tab that paints the panel's own surface so tab and panel read as one piece.
/// The tabs carry decorative icons; only the ones that switch panels are controls.
/// </summary>
public class PanelTabSurfaceTests
{
    [Fact]
    public void TheWell_HasNoDividerUnderTheTabText()
    {
        string strip = Rule("panel-tabstrip");

        strip.Should().NotContain("border-bottom",
            "the reference's tabs have no line under the text; the active tab merges into the panel");
        strip.Should().Contain("background: var(--theme-background);",
            "the well is the window background (#141414 sampled)");
    }

    [Fact]
    public void TheActiveTab_IsThePanelsOwnSurface()
    {
        Rule("panel-tab.active").Should().Contain("background: var(--theme-panel);",
            "the face is the panel's own surface (#333333 sampled — the same brush .panel paints with), so " +
            "there is no seam where the tab ends");
        Rule("panel").Should().Contain("background: var(--theme-panel);",
            "the merge is structural: tab and panel share the role");
    }

    [Fact]
    public void ATab_IsARoundedTopFaceWithoutSeparators()
    {
        string tab = Rule("panel-tab");

        tab.Should().Contain("border-radius: var(--theme-radius) var(--theme-radius) 0 0;");
        tab.Should().NotContain("border-right",
            "the reference separates tabs by contrast alone, not by hairlines");
    }

    [Fact]
    public void OnlySwitchingTabs_AreControls()
    {
        string hover = Rule("button.panel-tab:hover");

        hover.Should().Contain("background: var(--theme-tab-hover);",
            "a hovering switcher tab lifts to the dock-tab hover role, not a hard-coded grey");
        Rule("span.panel-tab").Should().NotContain("background:",
            "a single-tab panel is a label; it does not pretend to respond to the pointer");
    }

    [Fact]
    public void TheTabs_CarryTheirIcons()
    {
        foreach (string panel in new[] { ReadPanel("HierarchyPanel"), ReadPanel("InspectorPanel"), ReadEditor() })
        {
            panel.Should().Contain("panel-tab-icon", "each tab wears its icon, like the reference's dot and bulb");
            panel.Should().Contain("aria-hidden=\"true\"", "which is chrome, hidden from assistive tech");
        }

        ReadPanel("InspectorPanel").Should().Contain("fa-circle-info",
            "the reference's inspector dot is this glyph");
    }

    private static string Rule(string selector)
    {
        // Comments are stripped first: they would otherwise attach to the selector printed in front of them.
        string bare = Regex.Replace(Stylesheet(), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        List<string> bodies = [];

        foreach (Match match in Regex.Matches(bare, @"([^{}]*)\{([^{}]*)\}"))
        {
            // The selector may be a bare class or a compound (span.panel-tab); match either spelling.
            bool names = match.Groups[1].Value
                .Split(',')
                .Any(part => string.Equals(part.Trim(), "." + selector, StringComparison.Ordinal)
                    || string.Equals(part.Trim(), selector, StringComparison.Ordinal));

            if (names)
            {
                bodies.Add(match.Groups[2].Value);
            }
        }

        return bodies.Count > 0
            ? string.Join("\n", bodies)
            : throw new InvalidOperationException($"No {selector} rule in app.css.");
    }

    private static string ReadPanel(string name) =>
        Read("src", "OvercoreEditorPhotino", "Components", name + ".razor");

    private static string ReadEditor() =>
        Read("src", "OvercoreEditorPhotino", "Pages", "Editor.razor");

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
