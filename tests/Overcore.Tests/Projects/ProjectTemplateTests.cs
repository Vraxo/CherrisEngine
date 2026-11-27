using AwesomeAssertions;
using Overcore.Projects;
using Overcore.Serialization;
using Xunit;

namespace Overcore.Tests.Projects;

public sealed class ProjectTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "overcore-templates", Guid.NewGuid().ToString("N"));

    public ProjectTemplateTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Catalog_OffersBlankAndSampleProjects()
    {
        ProjectTemplateCatalog.Templates.Select(template => template.Id)
            .Should().ContainInOrder(ProjectTemplateCatalog.BlankId,
                ProjectTemplateCatalog.ThreeDStarterId, ProjectTemplateCatalog.UiShowcaseId);
    }

    [Fact]
    public void CreateFromTemplate_CopiesAndRenamesTheThreeDStarter()
    {
        string projectPath = ProjectWorkspace.CreateFromTemplate(_root, "My First Game",
            ProjectTemplateCatalog.ThreeDStarterId);

        ProjectConfig project = ProjectConfig.Load(projectPath);
        project.Name.Should().Be("My First Game");
        project.StartScene.Should().Be("Scenes/Main.yaml");
        File.Exists(project.StartScenePath).Should().BeTrue();
        File.Exists(Path.Combine(projectPath, "README.md")).Should().BeTrue();
        File.ReadAllText(project.StartScenePath).Should().Contain("Mesh: Cube").And.Contain("Name: Sun");
        ProjectWorkspace.Validate(projectPath).Should().Be(projectPath);
    }

    [Fact]
    public void CreateFromTemplate_CreatesALoadableInteractiveUiShowcase()
    {
        string projectPath = ProjectWorkspace.CreateFromTemplate(_root, "UI Copy",
            ProjectTemplateCatalog.UiShowcaseId);
        ProjectConfig project = ProjectConfig.Load(projectPath);
        string yaml = File.ReadAllText(project.StartScenePath);

        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        List<Overcore.Core.GameObject> objects = loader.LoadSceneFromYaml(yaml);

        objects.Select(gameObject => gameObject.Name).Should().Contain("Canvas");
        objects.SelectMany(gameObject => gameObject.GetComponents<Overcore.Components.Text>())
            .Select(text => text.Content).Should().Contain(text => text.Contains("مرحبا", StringComparison.Ordinal));
        objects.Select(gameObject => gameObject.GetComponent<Overcore.Components.Button>())
            .Should().Contain(button => button is not null);
        objects.Select(gameObject => gameObject.GetComponent<Overcore.Components.InputField>())
            .Should().Contain(field => field is not null);
        ProjectWorkspace.Validate(projectPath).Should().Be(projectPath);
    }

    [Fact]
    public void CreateFromTemplate_RefusesOverwriteAndUnknownTemplates()
    {
        string existing = Path.Combine(_root, "Taken");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "original");

        Action overwrite = () => ProjectWorkspace.CreateFromTemplate(_root, "Taken",
            ProjectTemplateCatalog.UiShowcaseId);
        Action unknown = () => ProjectWorkspace.CreateFromTemplate(_root, "Fresh", "not-a-template");

        overwrite.Should().Throw<IOException>();
        unknown.Should().Throw<ArgumentException>();
        File.ReadAllText(Path.Combine(existing, "keep.txt")).Should().Be("original");
        Directory.Exists(Path.Combine(_root, "Fresh")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
