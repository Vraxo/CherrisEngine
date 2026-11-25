using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace CherrisEditorSession.Tests;

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
            : throw new InvalidOperationException($"No .{selector} rule in app.css.");

    private static string Stylesheet()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "CherrisEditorPhotino", "wwwroot", "css", "app.css");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("app.css not found above '" + AppContext.BaseDirectory + "'.");
    }
}
