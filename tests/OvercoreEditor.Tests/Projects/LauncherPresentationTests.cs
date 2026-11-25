using AwesomeAssertions;
using OvercoreEditor.UI;
using Xunit;

namespace OvercoreEditor.Tests.Projects;

public class LauncherPresentationTests
{
    [Fact]
    public void SearchIsCaseInsensitiveAndKeepsRecentOrder()
    {
        string[] projects = ["/games/Chroma", "/games/Other", "/archive/chroma-demo"];

        LauncherPresentation.FilterRecent(projects, " CHROMA ")
            .Should().Equal("/games/Chroma", "/archive/chroma-demo");
        LauncherPresentation.FilterRecent(projects, "   ").Should().Equal(projects);
    }

    [Theory]
    [InlineData(819, false)]
    [InlineData(820, true)]
    [InlineData(1280, true)]
    public void SidebarIsOnlyUsedWhenThereIsRoom(float width, bool expected)
    {
        LauncherPresentation.UseSidebar(width).Should().Be(expected);
    }

    [Theory]
    [InlineData(30, 36, 36)]
    [InlineData(0, 36, 36)]
    [InlineData(44, 36, 44)]
    [InlineData(30, 50, 50)]
    public void ButtonsAreNeverShorterThanThePaddedText(float requested, float frameHeight, float expected)
    {
        LauncherPresentation.ButtonHeight(requested, frameHeight).Should().Be(expected);
    }

    [Fact]
    public void ProjectNamesIgnoreTrailingSeparators()
    {
        var path = Path.Combine(Path.GetTempPath(), "MyGame") + Path.DirectorySeparatorChar;
        LauncherPresentation.ProjectName(path).Should().Be("MyGame");
    }
}
