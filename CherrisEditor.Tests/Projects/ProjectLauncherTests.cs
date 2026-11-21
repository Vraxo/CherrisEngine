using AwesomeAssertions;
using CherrisEditor;
using Xunit;

namespace CherrisEditor.Tests.Projects;

public class ProjectLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CherrisLauncherTests", Guid.NewGuid().ToString("N"));

    public ProjectLauncherTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CreateProjectMakesConfigAndStartScene()
    {
        string path = ProjectLauncher.CreateProject(_root, "MyGame");

        path.Should().Be(Path.Combine(_root, "MyGame"));
        File.Exists(Path.Combine(path, "project.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(path, "Main.yaml")).Should().BeTrue();
        Project.Load(path).Name.Should().Be("MyGame");
        ProjectLauncher.ValidateExistingProject(path).Should().Be(path);
    }

    [Fact]
    public void CreateProjectDoesNotOverwriteExistingFolder()
    {
        string path = Path.Combine(_root, "MyGame");
        Directory.CreateDirectory(path);
        string config = Path.Combine(path, "project.yaml");
        File.WriteAllText(config, "original");

        Action create = () => ProjectLauncher.CreateProject(_root, "MyGame");

        create.Should().Throw<IOException>();
        File.ReadAllText(config).Should().Be("original");
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("game/subfolder")]
    [InlineData("game\\subfolder")]
    public void InvalidProjectNamesAreRejected(string name)
    {
        Action create = () => ProjectLauncher.CreateProject(_root, name);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OpeningAPlainFolderOrMissingFolderShowsAnError()
    {
        Action plainFolder = () => ProjectLauncher.ValidateExistingProject(_root);
        Action missingFolder = () => ProjectLauncher.ValidateExistingProject(Path.Combine(_root, "missing"));

        plainFolder.Should().Throw<InvalidOperationException>();
        missingFolder.Should().Throw<DirectoryNotFoundException>();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
