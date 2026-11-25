using AwesomeAssertions;
using OvercoreEditor;
using Xunit;

namespace OvercoreEditor.Tests.Projects;

public class ProjectLauncherProcessTests
{
    [Fact]
    public void AppHostOpensNewProjectPageWithoutPassingAnAssembly()
    {
        string executable = Path.Combine("editor folder", "OvercoreEditor.exe");

        var info = ProjectLauncherProcess.CreateStartInfo(executable, "ignored.dll", createProject: true);

        info.FileName.Should().Be(executable);
        info.UseShellExecute.Should().BeFalse();
        info.ArgumentList.Should().Equal("--new-project");
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    public void DotnetHostIncludesAssemblyBeforeLauncherArgument(string executable)
    {
        string assembly = Path.Combine("editor folder", "OvercoreEditor.dll");

        var info = ProjectLauncherProcess.CreateStartInfo(executable, assembly, createProject: false);

        info.ArgumentList.Should().Equal(assembly, "--launcher");
    }

    [Fact]
    public void DotnetHostRequiresAnEntryAssembly()
    {
        Action create = () => ProjectLauncherProcess.CreateStartInfo("dotnet", null, createProject: true);
        create.Should().Throw<InvalidOperationException>();
    }
}
