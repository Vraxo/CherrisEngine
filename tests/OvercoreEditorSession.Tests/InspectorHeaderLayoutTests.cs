using AwesomeAssertions;
using Xunit;

namespace OvercoreEditorSession.Tests;

public class InspectorHeaderLayoutTests
{
    [Fact]
    public void ComponentHeader_PlacesEnableToggleToTheRightOfTheName()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "OvercoreEditorPhotino", "Components", "InspectorPanel.razor");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
            directory = directory.Parent;
        }

        path.Should().NotBeNull();
        string markup = File.ReadAllText(path!);
        int component = markup.IndexOf("@foreach (ComponentSnapshot component", StringComparison.Ordinal);
        component.Should().BeGreaterThanOrEqualTo(0);
        string header = markup[component..];
        int fold = header.IndexOf("class=\"inspector-section-fold\"", StringComparison.Ordinal);
        int foldEnd = header.IndexOf("</button>", fold, StringComparison.Ordinal);
        int toggle = header.IndexOf("class=\"inspector-section-toggle\"", StringComparison.Ordinal);
        int name = header.IndexOf("class=\"inspector-section-name\"", StringComparison.Ordinal);

        fold.Should().BeGreaterThanOrEqualTo(0);
        foldEnd.Should().BeGreaterThan(fold);
        name.Should().BeGreaterThan(foldEnd);
        int nameEnd = header.IndexOf("</button>", name, StringComparison.Ordinal);
        nameEnd.Should().BeGreaterThan(name);
        toggle.Should().BeGreaterThan(nameEnd, "the checkbox belongs to the right of the name and outside the fold controls");
    }
}
