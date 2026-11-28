using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Pins the Unity-style context-menu look: a dark <c>Background</c> surface, a mid-grey <c>Header</c> hover bar,
/// <c>Foreground</c> text with muted disabled items, and a <c>DropdownOutline</c> separator hairline.
/// </summary>
public class MenuStyleTests
{
    [Fact]
    public void Menus_UseTheDarkSurfaceAndGreyHover()
    {
        string css = FindStylesheet();

        Match popup = Regex.Match(css, @"(?m)^\.menu-popup\s*\{([^}]*)\}");
        popup.Success.Should().BeTrue();
        popup.Groups[1].Value.Should().Contain("background: var(--theme-background);");
        popup.Groups[1].Value.Should().Contain("border: var(--theme-border-width) solid var(--theme-dropdown-outline);");
        popup.Groups[1].Value.Should().Contain("padding: 4px 0;");

        Match item = Regex.Match(css, @"(?m)^\.menu-item\s*\{([^}]*)\}");
        item.Success.Should().BeTrue();
        item.Groups[1].Value.Should().Contain("color: var(--theme-foreground);");
        item.Groups[1].Value.Should().Contain("padding: 3px 12px;");

        Match hover = Regex.Match(css, @"(?m)^\.menu-item:hover:not\(:disabled\)\s*\{([^}]*)\}");
        hover.Success.Should().BeTrue();
        hover.Groups[1].Value.Should().Contain("background: var(--theme-header);");
        hover.Groups[1].Value.Should().Contain("color: var(--theme-foreground-hover);");

        Match disabled = Regex.Match(css, @"(?m)^\.menu-item:disabled\s*\{([^}]*)\}");
        disabled.Success.Should().BeTrue();
        disabled.Groups[1].Value.Should().Contain("color: var(--theme-accent-gray);");

        Match separator = Regex.Match(css, @"(?m)^\.menu-separator\s*\{([^}]*)\}");
        separator.Success.Should().BeTrue();
        separator.Groups[1].Value.Should().Contain("background: var(--theme-dropdown-outline);");
        separator.Groups[1].Value.Should().Contain("margin: 3px 12px;");

        Match shortcut = Regex.Match(css, @"(?m)^\.menu-shortcut\s*\{([^}]*)\}");
        shortcut.Success.Should().BeTrue();
        shortcut.Groups[1].Value.Should().Contain("color: var(--theme-foreground);");
        css.Should().Contain(".menu-item:disabled .menu-shortcut");
    }

    [Fact]
    public void Menus_DoNotUseTheBluePrimaryHover()
    {
        string css = FindStylesheet();

        Match hover = Regex.Match(css, @"(?m)^\.menu-item:hover:not\(:disabled\)\s*\{([^}]*)\}");
        hover.Success.Should().BeTrue();
        hover.Groups[1].Value.Should().NotContain("var(--theme-primary)");
    }

    private static string FindStylesheet()
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
