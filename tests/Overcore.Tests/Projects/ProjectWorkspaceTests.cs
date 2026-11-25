using AwesomeAssertions;
using Overcore.Projects;
using Xunit;

namespace Overcore.Tests.Projects;

public class ProjectWorkspaceTests : IDisposable
{
    private readonly string _root;

    public ProjectWorkspaceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "overcore-projects", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_ReadsTheDescriptorTheImGuiEditorWrites()
    {
        string root = Path.Combine(_root, "MyGame");
        Directory.CreateDirectory(root);

        // Same keys and PascalCase convention as src/OvercoreEditor/Projects/Project.Save().
        File.WriteAllText(
            Path.Combine(root, ProjectConfig.FileName),
            "Name: SpaceGame" + Environment.NewLine +
            "StartScene: Scenes/Level1.yaml" + Environment.NewLine +
            "PackAssets: true" + Environment.NewLine);

        ProjectConfig project = ProjectConfig.Load(root);

        project.Name.Should().Be("SpaceGame");
        project.StartScene.Should().Be("Scenes/Level1.yaml");
        project.PackAssets.Should().BeTrue();
        project.RootPath.Should().Be(Path.GetFullPath(root));
        project.StartScenePath.Should().Be(Path.Combine(root, "Scenes", "Level1.yaml"));
    }

    [Fact]
    public void Load_FallsBackToDefaults_WhenThereIsNoDescriptor()
    {
        string root = Path.Combine(_root, "Empty");
        Directory.CreateDirectory(root);

        ProjectConfig project = ProjectConfig.Load(root);

        project.StartScene.Should().Be(ProjectConfig.DefaultStartScene);
        project.RootPath.Should().Be(Path.GetFullPath(root));
    }

    [Fact]
    public void Create_WritesTheDescriptorAndAnEmptyStartScene()
    {
        string root = ProjectWorkspace.Create(_root, "MyGame");

        root.Should().Be(Path.Combine(_root, "MyGame"));
        File.Exists(Path.Combine(root, ProjectConfig.FileName)).Should().BeTrue();

        ProjectConfig project = ProjectConfig.Load(root);
        project.Name.Should().Be("MyGame");
        File.Exists(project.StartScenePath).Should().BeTrue();
        File.ReadAllText(project.StartScenePath).Should().Contain("GameObjects");
    }

    [Fact]
    public void Create_RefusesToOverwriteAnExistingFolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Taken"));

        Action create = () => ProjectWorkspace.Create(_root, "Taken");

        create.Should().Throw<IOException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("nested/name")]
    [InlineData("nested\\name")]
    [InlineData(" padded")]
    public void GetNewProjectPath_RejectsUnusableNames(string name)
    {
        Action resolve = () => ProjectWorkspace.GetNewProjectPath(_root, name);

        resolve.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_RejectsFoldersThatAreNotProjects()
    {
        string notAProject = Path.Combine(_root, "Docs");
        Directory.CreateDirectory(notAProject);
        File.WriteAllText(Path.Combine(notAProject, "notes.txt"), "hello");

        Action validate = () => ProjectWorkspace.Validate(notAProject);
        Action missing = () => ProjectWorkspace.Validate(Path.Combine(_root, "nope"));

        validate.Should().Throw<InvalidOperationException>().WithMessage("*project.yaml*");
        missing.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Validate_TrimsAndResolvesAPath()
    {
        string root = ProjectWorkspace.Create(_root, "Trimmed");

        ProjectWorkspace.Validate("  " + root + "  ").Should().Be(Path.GetFullPath(root));
    }

    [Fact]
    public void DisplayName_PrefersTheDescriptorAndFallsBackToTheFolder()
    {
        string root = ProjectWorkspace.Create(_root, "FolderName");

        ProjectWorkspace.DisplayName(root).Should().Be("FolderName");

        File.WriteAllText(Path.Combine(root, ProjectConfig.FileName), "Name: [unclosed");
        ProjectWorkspace.DisplayName(root).Should().Be("FolderName");
    }

    [Fact]
    public void Exists_IsFalseWhenTheStartSceneIsGoneButTrueWhileTheDescriptorIsThere()
    {
        string root = ProjectWorkspace.Create(_root, "Present");

        ProjectWorkspace.Exists(root).Should().BeTrue();
        ProjectWorkspace.Exists(Path.Combine(_root, "absent")).Should().BeFalse();
    }
}
