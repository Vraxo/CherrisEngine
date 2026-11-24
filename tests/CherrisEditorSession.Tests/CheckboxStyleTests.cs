using AwesomeAssertions;
using Xunit;

namespace CherrisEditorSession.Tests;

public class CheckboxStyleTests
{
    [Fact]
    public void Checkboxes_UseTheSameDarkStyleAndKeepAccessibleStates()
    {
        string css = File.ReadAllText(FindStylesheet());
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
            string candidate = Path.Combine(directory.FullName, "src", "CherrisEditorPhotino", "wwwroot", "css", "app.css");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("app.css not found above '" + AppContext.BaseDirectory + "'.");
    }
}
