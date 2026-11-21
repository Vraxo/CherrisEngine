using AwesomeAssertions;
using CherrisEditor.Build;
using Xunit;

namespace CherrisEditor.Tests.Build;

public class RuntimeProjectLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CherrisLocatorTests", Guid.NewGuid().ToString("N"));

    public RuntimeProjectLocatorTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("artifacts/bin/CherrisEditor/debug")]
    [InlineData("src/CherrisEditor/bin/Debug/net11.0")]
    public void FindsRuntimeFromBuildOutput(string relativeOutput)
    {
        var projectDirectory = Path.Combine(_root, "src", "CherrisRuntime");
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, "CherrisRuntime.csproj");
        File.WriteAllText(project, "<Project />");
        var output = Path.Combine(_root, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(output);

        RuntimeProjectLocator.Find(output).Should().Be(project);
    }

    [Fact]
    public void SupportsLegacyFlatSourceLayout()
    {
        var projectDirectory = Path.Combine(_root, "CherrisRuntime");
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, "CherrisRuntime.csproj");
        File.WriteAllText(project, "<Project />");
        var output = Path.Combine(_root, "artifacts", "bin", "CherrisEditor", "debug");
        Directory.CreateDirectory(output);

        RuntimeProjectLocator.Find(output).Should().Be(project);
    }

    [Fact]
    public void ReturnsNullWhenNoRuntimeProjectExists()
    {
        RuntimeProjectLocator.Find(_root).Should().BeNull();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
