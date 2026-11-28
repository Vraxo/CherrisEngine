using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// The content browser's tiles sit on the panel, like the hierarchy's rows: no inset list box, no second frame
/// inside the panel's own border. Its controls still own their borders, which is what makes a tile look picked.
/// </summary>
public class ContentBrowserSurfaceTests
{
    [Fact]
    public void TheGrid_IsNotABoxInsideThePanel()
    {
        string grid = Rule("cb-grid");

        grid.Should().NotContain("border:", "the panel already draws the only frame the tiles need");
        grid.Should().NotContain("border-radius:", "a rounded inset would read as a box inside the panel");
        grid.Should().NotContain("background:", "the tiles show the panel's own surface");
        grid.Should().NotContain("padding:", "the panel's padding is the grid's padding");
    }

    [Fact]
    public void TheGrid_StillFillsThePanelAndScrolls()
    {
        string grid = Rule("cb-grid");

        grid.Should().Contain("flex: 1;");
        grid.Should().Contain("overflow: auto;");
        grid.Should().Contain("align-content: flex-start;");
    }

    [Fact]
    public void TheTiles_StillShowHoverAndSelection()
    {
        Rule("cb-item:hover").Should().Contain("background: var(--theme-select-hover);");

        Match selected = Regex.Match(Stylesheet(), @"(?m)^\.cb-item\.selected\s*\{([^}]*)\}");
        selected.Success.Should().BeTrue();
        selected.Groups[1].Value.Should().Contain("border-color: var(--theme-primary);");
        selected.Groups[1].Value.Should().Contain("background: var(--theme-select-parent);");
    }

    private static string Rule(string selector) =>
        Regex.Match(Stylesheet(), @"(?m)^\." + Regex.Escape(selector) + @"\s*\{([^}]*)\}") is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No .{selector} rule in the stylesheets.");

    private static string Stylesheet()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string cssDirectory = Path.Combine(directory.FullName, "src", "OvercoreEditorPhotino", "wwwroot", "css");
            if (Directory.Exists(cssDirectory) && File.Exists(Path.Combine(cssDirectory, "theme.css")))
            {
                string[] files =
                [
                    "theme.css",
                    "launcher.css",
                    "editor-chrome.css",
                    "widgets.css",
                    "hierarchy.css",
                    "inspector.css",
                    "console.css",
                    "content-browser.css",
                    "viewport.css",
                    "status.css",
                    "states.css",
                    "scrollbars.css"
                ];

                string css = string.Join("\n", files.Select(name => File.ReadAllText(Path.Combine(cssDirectory, name))));

                return css;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("stylesheets not found above '" + AppContext.BaseDirectory + "'.");
    }
}
