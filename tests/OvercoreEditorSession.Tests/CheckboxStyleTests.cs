using AwesomeAssertions;
using Xunit;

namespace OvercoreEditorSession.Tests;

public class CheckboxStyleTests
{
    [Fact]
    public void Checkboxes_UseTheSameDarkStyleAndKeepAccessibleStates()
    {
        string css = FindStylesheet();
        string[] selectors =
        [
            ".inspector-section-toggle",
            ".imgui-check",
            ".imgui-checkbox input[type=\"checkbox\"]"
        ];

        foreach (string selector in selectors)
        {
            css.Should().Contain(selector + ":checked::after", "the tick is visible only when checked");
            css.Should().Contain(selector + ":focus-visible", "keyboard focus must be visible");
            css.Should().Contain(selector + ":disabled", "disabled checkboxes must look disabled");
        }

        css.Should().Contain("background: var(--theme-recessed);");
        css.Should().Contain("border: solid var(--theme-foreground);");
        css.Should().Contain("@media (forced-colors: active)");
        css.Should().Contain("appearance: auto;", "high-contrast mode uses the system checkbox");
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
