using AwesomeAssertions;
using Cherris.Scripting;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers the rules behind "New script: …" in the Add Component menu: which names can be script names, where a script
/// lives, and the file that gets written.
/// </summary>
public class ScriptTemplateTests
{
    [Theory]
    [InlineData("Player")]
    [InlineData("player_controller")]
    [InlineData("_internal")]
    [InlineData("Turret2")]
    public void ValidNames_AreLettersDigitsAndUnderscores(string name)
    {
        ScriptTemplate.IsValidName(name).Should().BeTrue();
        ScriptTemplate.Normalize(name).Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2Player")]
    [InlineData("my script")]
    [InlineData("Player-Move")]
    [InlineData("Player.Move")]
    [InlineData("Player/Other")]
    [InlineData(null)]
    public void InvalidNames_AreRefused(string? name)
    {
        ScriptTemplate.IsValidName(name).Should().BeFalse();
        ScriptTemplate.Normalize(name).Should().BeNull();
    }

    [Fact]
    public void Normalize_TrimsAndForgivesTheExtension()
    {
        ScriptTemplate.Normalize("  Player  ").Should().Be("Player");
        ScriptTemplate.Normalize("Player.lua").Should().Be("Player");
        ScriptTemplate.Normalize("Player.LUA").Should().Be("Player");
    }

    [Fact]
    public void Paths_AreProjectRelative_InsideTheScriptsFolder()
    {
        ScriptTemplate.RelativePath("Player").Should().Be("Scripts/Player.lua");
        ScriptTemplate.FullPath("/tmp/project", "Player").Should().Be(
            Path.Combine("/tmp/project", "Scripts", "Player.lua"));
        ScriptTemplate.FullPath("/tmp/project", "Player.lua").Should().Be(
            Path.Combine("/tmp/project", "Scripts", "Player.lua"));

        ScriptTemplate.FullPath(null, "Player").Should().BeNull("the demo scene has no project to write to");
        ScriptTemplate.FullPath("/tmp/project", "my script").Should().BeNull();
    }

    [Fact]
    public void Source_StartsWithTheHandlersTheAdapterCalls()
    {
        string source = ScriptTemplate.Source("Player");

        source.Should().Contain("Player.lua").And.Contain("function start()").And.Contain("function update(deltaTime)");
    }

    [Fact]
    public void Create_WritesTheFileOnce_AndNeverOverwritesIt()
    {
        using TemporaryProject project = new();

        ScriptTemplate.TryCreate(project.Root, "Player", out string path, out string? error).Should().BeTrue();
        error.Should().BeNull();
        path.Should().Be("Scripts/Player.lua");

        string file = Path.Combine(project.Root, "Scripts", "Player.lua");
        File.Exists(file).Should().BeTrue();
        string written = File.ReadAllText(file);
        written.Should().Contain("function update(deltaTime)");

        File.WriteAllText(file, "-- edited by hand");

        ScriptTemplate.TryCreate(project.Root, "Player", out path, out error).Should().BeTrue();
        path.Should().Be("Scripts/Player.lua");
        File.ReadAllText(file).Should().Be("-- edited by hand", "the user's file is never replaced");
    }

    [Fact]
    public void Create_ExplainsWhatItCannotDo()
    {
        ScriptTemplate.TryCreate(null, "Player", out _, out string? noProject).Should().BeFalse();
        noProject.Should().Contain("no project folder");

        using TemporaryProject project = new();
        ScriptTemplate.TryCreate(project.Root, "my script", out _, out string? badName).Should().BeFalse();
        badName.Should().Contain("cannot be a script name");
    }

    [Fact]
    public void Exists_FollowsTheFile()
    {
        using TemporaryProject project = new();

        ScriptTemplate.Exists(project.Root, "Player").Should().BeFalse();

        ScriptTemplate.TryCreate(project.Root, "Player", out _, out _).Should().BeTrue();

        ScriptTemplate.Exists(project.Root, "Player").Should().BeTrue();
        ScriptTemplate.Exists(project.Root, "Player.lua").Should().BeTrue();
        ScriptTemplate.Exists(null, "Player").Should().BeFalse();
    }
}
