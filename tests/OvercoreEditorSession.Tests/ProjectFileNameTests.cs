using AwesomeAssertions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Covers the one set of name rules the editor writes by: what a file or folder inside a project may be called, and
/// the path a typed name turns into before anything is written.
/// </summary>
public class ProjectFileNameTests
{
    [Theory]
    [InlineData("Level", "Level")]
    [InlineData("  Level 1  ", "Level 1")]
    [InlineData("my_scene-2", "my_scene-2")]
    [InlineData("Café", "Café")]
    public void TryNormalize_KeepsWhatCanBeAFileName(string typed, string expected)
    {
        ProjectFileName.TryNormalize(typed, out string name, out string error).Should().BeTrue();

        name.Should().Be(expected);
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".hidden")]
    [InlineData("folder/Level")]
    [InlineData("folder\\Level")]
    [InlineData("Level:1")]
    [InlineData("What?")]
    [InlineData("a*b")]
    [InlineData("a|b")]
    [InlineData("a\tb")]
    public void TryNormalize_RefusesWhatIsNotAFileNameOnEveryPlatform(string typed)
    {
        ProjectFileName.TryNormalize(typed, out string name, out string error).Should().BeFalse();

        name.Should().BeEmpty();
        error.Should().NotBeEmpty("a refusal has to say why");
    }

    [Theory]
    [InlineData("Level", ".yaml", "Level.yaml")]
    [InlineData("Level.yaml", ".yaml", "Level.yaml")]
    [InlineData("Level.YAML", ".yaml", "Level.YAML")]
    [InlineData("Level.yml", ".yaml", "Level.yml")]
    [InlineData("Player", ".lua", "Player.lua")]
    [InlineData("Player.lua", ".lua", "Player.lua")]
    [InlineData("Levels", "", "Levels")]
    public void WithExtension_AddsOnlyWhatIsMissing(string name, string extension, string expected)
    {
        ProjectFileName.WithExtension(name, extension).Should().Be(expected);
    }

    [Fact]
    public void Available_SkipsTheNamesAlreadyTaken()
    {
        using TemporaryFolder folder = new();

        File.WriteAllText(Path.Combine(folder.Path, "NewScene.yaml"), "GameObjects: []");
        File.WriteAllText(Path.Combine(folder.Path, "NewScene2.yaml"), "GameObjects: []");
        Directory.CreateDirectory(Path.Combine(folder.Path, "New Folder"));

        ProjectFileName.Available(folder.Path, "NewScene", ".yaml").Should().Be("NewScene3");
        ProjectFileName.Available(folder.Path, "New Folder", string.Empty).Should().Be("New Folder2");
    }

    [Fact]
    public void Available_UsesTheNameItselfWhenNothingIsTaken()
    {
        using TemporaryFolder folder = new();

        ProjectFileName.Available(folder.Path, "NewScene", ".yaml").Should().Be("NewScene");
        ProjectFileName.Available(Path.Combine(folder.Path, "not-there"), "NewScene", ".yaml").Should()
            .Be("NewScene", "a folder that does not exist yet holds nothing");
    }

    [Fact]
    public void TryPreview_SaysThePathANameBecomes()
    {
        using TemporaryFolder folder = new();

        ProjectFileName.TryPreview(folder.Path, "Level 1", ".yaml", out string path).Should().BeTrue();
        path.Should().Be(Path.Combine(folder.Path, "Level 1.yaml"));

        ProjectFileName.TryPreview(folder.Path, "Level:1", ".yaml", out string refused).Should().BeFalse();
        refused.Should().BeEmpty();
    }

    [Fact]
    public void Describe_SaysAPathTheWayADialogDoes()
    {
        using TemporaryFolder project = new();

        string inside = Path.Combine(project.Path, "Levels", "Main.yaml");

        ProjectFileName.Describe(inside, project.Path).Should().Be("Levels/Main.yaml");

        // A path outside the project is not made relative to it: those two dots are a different folder.
        string outside = Path.Combine(Path.GetTempPath(), "elsewhere.yaml");

        ProjectFileName.Describe(outside, project.Path).Should().Be(outside);
    }

    /// <summary>A folder that cleans itself up, so the tests leave nothing behind.</summary>
    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "overcore-name-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary folder is not worth failing a test over.
            }
        }
    }
}
