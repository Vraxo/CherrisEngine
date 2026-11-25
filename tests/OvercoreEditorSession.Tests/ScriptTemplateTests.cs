using AwesomeAssertions;
using Overcore.Scripting;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Covers the rules behind "New lua script: …" and "New C# script: …" in the Add Component menu: which names can be
/// script names, where a script lives, and the file that gets written for each language.
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
    public void Normalize_TrimsAndForgivesEitherExtension()
    {
        ScriptTemplate.Normalize("  Player  ").Should().Be("Player");
        ScriptTemplate.Normalize("Player.lua").Should().Be("Player");
        ScriptTemplate.Normalize("Player.LUA").Should().Be("Player");

        // A C# name is the same name: the menu shows one box, and pasting a file name into it should work.
        ScriptTemplate.Normalize("Player.cs").Should().Be("Player");
    }

    [Fact]
    public void Paths_AreProjectRelative_InsideTheScriptsFolder()
    {
        ScriptTemplate.RelativePath("Player", ScriptKind.Lua).Should().Be("Scripts/Player.lua");
        ScriptTemplate.RelativePath("Player", ScriptKind.CSharp).Should().Be("Scripts/Player.cs");

        ScriptTemplate.FullPath("/tmp/project", "Player", ScriptKind.Lua).Should().Be(
            Path.Combine("/tmp/project", "Scripts", "Player.lua"));
        ScriptTemplate.FullPath("/tmp/project", "Player.lua", ScriptKind.Lua).Should().Be(
            Path.Combine("/tmp/project", "Scripts", "Player.lua"));
        ScriptTemplate.FullPath("/tmp/project", "Player.cs", ScriptKind.CSharp).Should().Be(
            Path.Combine("/tmp/project", "Scripts", "Player.cs"));

        ScriptTemplate.FullPath(null, "Player", ScriptKind.Lua).Should().BeNull("the demo scene has no project");
        ScriptTemplate.FullPath("/tmp/project", "my script", ScriptKind.CSharp).Should().BeNull();
    }

    [Fact]
    public void LuaSource_StartsWithTheHandlersTheAdapterCalls()
    {
        string source = ScriptTemplate.Source("Player", ScriptKind.Lua);

        source.Should().Contain("Player.lua").And.Contain("function start()").And.Contain("function update(deltaTime)");
    }

    [Fact]
    public void CSharpSource_IsAComponentNamedAfterTheFile()
    {
        string source = ScriptTemplate.Source("Player", ScriptKind.CSharp);

        // The using that resolves Script: the ImGui template emitted "using Overcore;" and did not compile.
        source.Should().Contain("using Overcore.Components;");
        source.Should().NotContain("using Overcore;");

        // The class name is what the file name becomes in a scene, so it has to match.
        source.Should().Contain("public class Player : Script");
        source.Should().Contain("public override void Start()");
        source.Should().Contain("public override void Update(float deltaTime)");
    }

    [Fact]
    public void Kind_DecidesTheExtensionAndTheLanguageName()
    {
        ScriptTemplate.ExtensionOf(ScriptKind.Lua).Should().Be(".lua");
        ScriptTemplate.ExtensionOf(ScriptKind.CSharp).Should().Be(".cs");

        ScriptTemplate.DisplayName(ScriptKind.Lua).Should().Be("Lua");
        ScriptTemplate.DisplayName(ScriptKind.CSharp).Should().Be("C#");

        // What LanguageScript is told to hand a Lua file to; a C# file never reaches an adapter.
        ScriptTemplate.LuaLanguage.Should().Be("lua");
    }

    [Fact]
    public void Create_WritesTheFileOnce_AndNeverOverwritesIt()
    {
        using TemporaryProject project = new();

        ScriptTemplate.TryCreate(project.Root, "Player", ScriptKind.Lua, out string path, out string? error)
            .Should().BeTrue();
        error.Should().BeNull();
        path.Should().Be("Scripts/Player.lua");

        string file = Path.Combine(project.Root, "Scripts", "Player.lua");
        File.Exists(file).Should().BeTrue();
        string written = File.ReadAllText(file);
        written.Should().Contain("function update(deltaTime)");

        File.WriteAllText(file, "-- edited by hand");

        ScriptTemplate.TryCreate(project.Root, "Player", ScriptKind.Lua, out path, out error).Should().BeTrue();
        path.Should().Be("Scripts/Player.lua");
        File.ReadAllText(file).Should().Be("-- edited by hand", "the user's file is never replaced");
    }

    [Fact]
    public void Create_WritesTheCSharpFileNextToTheLuaOne()
    {
        using TemporaryProject project = new();

        ScriptTemplate.TryCreate(project.Root, "Player", ScriptKind.CSharp, out string path, out string? error)
            .Should().BeTrue();

        error.Should().BeNull();
        path.Should().Be("Scripts/Player.cs");

        string file = Path.Combine(project.Root, "Scripts", "Player.cs");
        File.ReadAllText(file).Should().Contain("public class Player : Script");

        // The two languages are separate files with the same name; one does not stand in for the other.
        ScriptTemplate.Exists(project.Root, "Player", ScriptKind.CSharp).Should().BeTrue();
        ScriptTemplate.Exists(project.Root, "Player", ScriptKind.Lua).Should().BeFalse();
    }

    [Fact]
    public void Create_ExplainsWhatItCannotDo()
    {
        ScriptTemplate.TryCreate(null, "Player", ScriptKind.Lua, out _, out string? noProject).Should().BeFalse();
        noProject.Should().Contain("no project folder");

        ScriptTemplate.TryCreate(null, "Player", ScriptKind.CSharp, out _, out string? noProjectCSharp)
            .Should().BeFalse();
        noProjectCSharp.Should().Contain("no project folder");

        using TemporaryProject project = new();

        ScriptTemplate.TryCreate(project.Root, "my script", ScriptKind.CSharp, out _, out string? badName)
            .Should().BeFalse();
        badName.Should().Contain("cannot be a script name");
    }

    [Fact]
    public void Exists_FollowsTheFile()
    {
        using TemporaryProject project = new();

        ScriptTemplate.Exists(project.Root, "Player", ScriptKind.Lua).Should().BeFalse();

        ScriptTemplate.TryCreate(project.Root, "Player", ScriptKind.Lua, out _, out _).Should().BeTrue();

        ScriptTemplate.Exists(project.Root, "Player", ScriptKind.Lua).Should().BeTrue();
        ScriptTemplate.Exists(project.Root, "Player.lua", ScriptKind.Lua).Should().BeTrue();
        ScriptTemplate.Exists(project.Root, "Player", ScriptKind.CSharp).Should().BeFalse();
        ScriptTemplate.Exists(null, "Player", ScriptKind.Lua).Should().BeFalse();
    }
}
