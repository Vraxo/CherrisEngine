using AwesomeAssertions;
using OvercoreEditor.Build;
using Xunit;

namespace OvercoreEditor.Tests.Build;

public class RuntimeProjectLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OvercoreLocatorTests", Guid.NewGuid().ToString("N"));

    public RuntimeProjectLocatorTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("artifacts/bin/OvercoreEditor/debug")]
    [InlineData("src/OvercoreEditor/bin/Debug/net11.0")]
    public void FindsRuntimeFromBuildOutput(string relativeOutput)
    {
        var projectDirectory = Path.Combine(_root, "src", "OvercoreRuntime");
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, "OvercoreRuntime.csproj");
        File.WriteAllText(project, "<Project />");
        var output = Path.Combine(_root, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(output);

        RuntimeProjectLocator.Find(output).Should().Be(project);
    }

    [Fact]
    public void SupportsLegacyFlatSourceLayout()
    {
        var projectDirectory = Path.Combine(_root, "OvercoreRuntime");
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, "OvercoreRuntime.csproj");
        File.WriteAllText(project, "<Project />");
        var output = Path.Combine(_root, "artifacts", "bin", "OvercoreEditor", "debug");
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
