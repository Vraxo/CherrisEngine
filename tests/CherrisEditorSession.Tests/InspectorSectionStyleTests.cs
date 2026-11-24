using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace CherrisEditorSession.Tests;

public class InspectorSectionStyleTests
{
    [Fact]
    public void Headers_ShareTheComponentBackground()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "CherrisEditorPhotino", "wwwroot", "css", "app.css");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
            directory = directory.Parent;
        }

        path.Should().NotBeNull();
        string css = File.ReadAllText(path!);
        Match section = Regex.Match(css, @"(?m)^\.inspector-section\s*\{([^}]*)\}");
        Match header = Regex.Match(css, @"(?m)^\.inspector-section-header\s*\{([^}]*)\}");
        section.Success.Should().BeTrue();
        header.Success.Should().BeTrue();
        section.Groups[1].Value.Should().Contain("background: var(--theme-panel)");
        section.Groups[1].Value.Should().Contain("border-bottom:", "components remain separated by a rule");
        header.Groups[1].Value.Should().Contain("background: transparent", "the header should show the same surface as its body");
        css.Should().NotContain(".inspector-section-header:hover", "hover must not add a darker header band");
    }
}
