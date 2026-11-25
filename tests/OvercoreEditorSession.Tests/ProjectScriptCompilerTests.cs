using AwesomeAssertions;
using Overcore.Components;
using Overcore.Scripting;
using System.Runtime.Loader;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Covers compiling a project's C# scripts: what a <c>Scripts/</c> folder turns into, what a broken one reports
/// instead, and the names the inspector's menu builds from the result.
/// </summary>
public class ProjectScriptCompilerTests
{
    private const string SpinnerSource = """
        using Overcore.Components;

        public class Spinner : Script
        {
            public override void Update(float deltaTime)
            {
            }
        }
        """;

    [Fact]
    public void Compile_FindsTheScriptTypesAProjectDeclares()
    {
        using TemporaryProject project = new();
        WriteScript(project, "Spinner.cs", SpinnerSource);
        using ScriptContext context = new();

        ScriptCompileResult result = ProjectScriptCompiler.Compile(project.Root, context.Context);

        result.Succeeded.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Assembly.Should().NotBeNull();
        result.ScriptTypes.Should().ContainSingle().Which.Name.Should().Be("Spinner");
        result.ScriptTypes[0].Should().BeAssignableTo<Script>();
    }

    [Fact]
    public void Compile_ReadsScriptsInSubFolders()
    {
        using TemporaryProject project = new();
        WriteScript(project, Path.Combine("Player", "Mover.cs"), SpinnerSource.Replace("Spinner", "Mover"));
        using ScriptContext context = new();

        ScriptCompileResult result = ProjectScriptCompiler.Compile(project.Root, context.Context);

        result.Succeeded.Should().BeTrue();
        result.ScriptTypes.Should().ContainSingle().Which.Name.Should().Be("Mover");
    }

    [Fact]
    public void Compile_ReportsTheErrors_AndLoadsNothing()
    {
        using TemporaryProject project = new();
        WriteScript(project, "Broken.cs", "public class Broken : NoSuchBase { }");
        using ScriptContext context = new();

        ScriptCompileResult result = ProjectScriptCompiler.Compile(project.Root, context.Context);

        result.Succeeded.Should().BeFalse("the script does not compile");
        result.Assembly.Should().BeNull();
        result.ScriptTypes.Should().BeEmpty();
        result.Errors.Should().NotBeEmpty().And.Contain(error => error.Contains("error CS"));
    }

    [Fact]
    public void Compile_WithNoScriptsToCompile_IsNotAFailure()
    {
        using TemporaryProject project = new();
        using ScriptContext context = new();

        ScriptCompileResult result = ProjectScriptCompiler.Compile(project.Root, context.Context);

        result.Succeeded.Should().BeTrue("a project with no C# scripts is an ordinary project");
        result.Assembly.Should().BeNull();
        result.ScriptTypes.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CompileToFile_WritesTheAssembly()
    {
        using TemporaryProject project = new();
        WriteScript(project, "Spinner.cs", SpinnerSource);
        string output = Path.Combine(project.Root, "GameScripts.dll");

        bool compiled = ProjectScriptCompiler.CompileToFile(project.Root, output, out IReadOnlyList<string> errors);

        compiled.Should().BeTrue();
        errors.Should().BeEmpty();
        new FileInfo(output).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ForScriptTypes_NamesThemTheWayTheMenuDoes()
    {
        IReadOnlyList<ComponentCatalog.Entry> entries = ComponentCatalog.ForScriptTypes([typeof(PlayerController)]);

        entries.Should().ContainSingle();
        entries[0].DisplayName.Should().Be("Player Controller", "a class name reads as words in the menu");
        entries[0].ComponentType.Should().Be(typeof(PlayerController));
        entries[0].Create().Should().BeOfType<PlayerController>();
    }

    [Fact]
    public void ForScriptTypes_IgnoresWhatIsNotAConcreteScript()
    {
        ComponentCatalog.ForScriptTypes([typeof(PlayerController), typeof(string), typeof(Script), typeof(Component)])
            .Should().ContainSingle().Which.ComponentType.Should().Be(typeof(PlayerController));
    }

    /// <summary>Writes a script into the project's <c>Scripts/</c> folder, making the folders it needs.</summary>
    private static void WriteScript(TemporaryProject project, string fileName, string source)
    {
        string folder = Path.Combine(project.Root, ScriptTemplate.Folder);
        string path = Path.Combine(folder, fileName);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    /// <summary>A script the menu can offer, for the naming and catalog tests.</summary>
    public sealed class PlayerController : Script
    {
    }

    /// <summary>A throwaway load context, unloaded when the test ends so assemblies do not pile up.</summary>
    private sealed class ScriptContext : IDisposable
    {
        public AssemblyLoadContext Context { get; } =
            new("tests-" + Guid.NewGuid().ToString("N"), isCollectible: true);

        public void Dispose() => Context.Unload();
    }
}
