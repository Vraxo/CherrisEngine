using AwesomeAssertions;
using Xunit;

namespace CherrisEditorSession.Tests;

public class InspectorHeaderLayoutTests
{
    [Fact]
    public void ComponentHeader_PlacesEnableToggleAfterFoldoutAndBeforeName()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "CherrisEditorPhotino", "Components", "InspectorPanel.razor");
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
        toggle.Should().BeGreaterThan(foldEnd, "checking a component must not activate the foldout");
        name.Should().BeGreaterThan(toggle);
    }
}
