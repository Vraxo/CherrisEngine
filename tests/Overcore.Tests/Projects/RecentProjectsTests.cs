using AwesomeAssertions;
using Overcore.Projects;
using Xunit;

namespace Overcore.Tests.Projects;

public class RecentProjectsTests : IDisposable
{
    private readonly string _root;
    private readonly RecentProjects _recent;

    public RecentProjectsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "overcore-recent", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _recent = new RecentProjects(Path.Combine(_root, "history"));
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
    public void Get_IsEmptyBeforeAnythingWasOpened()
    {
        _recent.Get().Should().BeEmpty();
        _recent.GetLast().Should().BeNull();
    }

    [Fact]
    public void Remember_PutsTheNewestProjectFirstAndOnceOnly()
    {
        string first = CreateProject("First");
        string second = CreateProject("Second");

        _recent.Remember(first);
        _recent.Remember(second);
        _recent.Remember(first);

        _recent.Get().Should().Equal(first, second);
        _recent.GetLast().Should().Be(Path.GetFullPath(first));
    }

    [Fact]
    public void Forget_RemovesTheEntryAndTheLastProjectPointer()
    {
        string first = CreateProject("First");
        string second = CreateProject("Second");

        _recent.Remember(first);
        _recent.Remember(second);
        _recent.Forget(second);

        _recent.Get().Should().Equal(first);
        _recent.GetLast().Should().BeNull("the most recent project was removed");
    }

    [Fact]
    public void Get_KeepsEntriesWhoseFolderDisappeared()
    {
        string project = CreateProject("Vanishing");
        _recent.Remember(project);

        Directory.Delete(project, recursive: true);

        // The launcher shows these as missing and offers a Remove button, so they must survive a read.
        _recent.Get().Should().Equal(project);
        _recent.GetLast().Should().BeNull();
    }

    [Fact]
    public void Remember_IsCappedAtTheMaximumNumberOfEntries()
    {
        for (int index = 0; index < RecentProjects.MaximumEntries + 4; index++)
        {
            _recent.Remember(CreateProject($"Game{index}"));
        }

        _recent.Get().Should().HaveCount(RecentProjects.MaximumEntries);
    }

    private string CreateProject(string name)
    {
        string root = Path.Combine(_root, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ProjectConfig.FileName), $"Name: {name}");

        return root;
    }
}
