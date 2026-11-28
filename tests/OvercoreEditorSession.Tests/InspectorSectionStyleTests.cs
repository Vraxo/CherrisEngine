using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace OvercoreEditorSession.Tests;

public class InspectorSectionStyleTests
{
    [Fact]
    public void Headers_ShareTheComponentBackground()
    {
        string css = ReadStylesheet();
        Match section = Regex.Match(css, @"(?m)^\.inspector-section\s*\{([^}]*)\}");
        Match header = Regex.Match(css, @"(?m)^\.inspector-section-header\s*\{([^}]*)\}");
        section.Success.Should().BeTrue();
        header.Success.Should().BeTrue();
        section.Groups[1].Value.Should().Contain("background: var(--theme-panel)");
        section.Groups[1].Value.Should().Contain("border-bottom:", "components remain separated by a rule");
        header.Groups[1].Value.Should().Contain("background: transparent", "the header should show the same surface as its body");
        header.Groups[1].Value.Should().NotContain("border-bottom", "there is no separator between the header and its properties");
        Match body = Regex.Match(css, @"(?m)^\.inspector-section-body\s*\{([^}]*)\}");
        body.Success.Should().BeTrue();
        body.Groups[1].Value.Should().Contain(
            "calc(var(--inspector-inset) + var(--inspector-fold-width) + var(--inspector-icon-width) + var(--inspector-header-gap) + var(--inspector-header-gap))",
            "the property labels align with the title after the foldout and icon");
        header.Groups[1].Value.Should().Contain("gap: var(--inspector-header-gap)");
        header.Groups[1].Value.Should().Contain("padding: 3px var(--inspector-inset)");
        css.Should().Contain("width: var(--inspector-fold-width)");
        css.Should().Contain("width: var(--inspector-icon-width)");
        css.Should().NotContain(".inspector-section-header:hover", "hover must not add a darker header band");
    }

    private static string ReadStylesheet()
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
