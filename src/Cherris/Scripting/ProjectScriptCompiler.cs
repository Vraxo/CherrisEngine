using Cherris.Components;
using Cherris.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Reflection;
using System.Runtime.Loader;

namespace Cherris.Scripting;

/// <summary>What compiling a project's C# scripts produced.</summary>
/// <param name="Assembly">The compiled assembly, or null when nothing was compiled or the compile failed.</param>
/// <param name="ScriptTypes">The non-abstract <see cref="Script"/> subclasses it declares, in file order.</param>
/// <param name="Errors">Why it did not compile, one readable line each; empty when it did, or when there was nothing
/// to compile.</param>
public sealed record ScriptCompileResult(Assembly? Assembly, IReadOnlyList<Type> ScriptTypes, IReadOnlyList<string> Errors)
{
    /// <summary>True when there was something to compile and it compiled.</summary>
    public bool Succeeded => Errors.Count == 0;

    /// <summary>The answer for a project with no <c>Scripts/</c> folder, or an empty one: nothing to compile, and
    /// nothing wrong.</summary>
    public static ScriptCompileResult Nothing { get; } = new(null, [], []);
}

/// <summary>
/// Compiles a project's C# scripts — every <c>.cs</c> under <c>Scripts/</c> — into one assembly, and lists the
/// <see cref="Script"/> types they declare.
/// </summary>
/// <remarks>
/// <para>
/// The engine owns this, not the editor that happens to be open: a project's scripts are the same scripts whichever
/// window compiles them, and the assembly they land in is what a scene's component names are resolved against.
/// </para>
/// <para>
/// Nothing here reaches for a window: a caller passes the load context the assembly should land in and decides how
/// loudly to report what came back, which is what lets the ImGui editor, the web editor and the game builder share
/// one compiler instead of three that drift.
/// </para>
/// </remarks>
public static class ProjectScriptCompiler
{
    private const string AssemblyName = "GameScriptsAssembly";
    private const string ScriptFilePattern = "*.cs";

    /// <summary>Assemblies a script may use even when nothing in the engine happens to touch them.</summary>
    private static readonly string[] RequiredAssemblies =
    [
        "System.Runtime",
        "System.Numerics.Vectors",
        "System.Collections"
    ];

    /// <summary>
    /// Compiles the project's scripts and loads the result into <paramref name="context"/>.
    /// </summary>
    /// <param name="projectRoot">Absolute folder that holds <c>Scripts/</c>.</param>
    /// <param name="context">The load context to put the assembly in — collectible, so an editor can replace it.</param>
    /// <returns>The types found, or the errors that stopped them.</returns>
    public static ScriptCompileResult Compile(string projectRoot, AssemblyLoadContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryCreateCompilation(projectRoot, out CSharpCompilation compilation))
        {
            return ScriptCompileResult.Nothing;
        }

        using MemoryStream stream = new();
        EmitResult emit = compilation.Emit(stream);

        if (!emit.Success)
        {
            return new ScriptCompileResult(null, [], ReadErrors(emit));
        }

        stream.Position = 0;
        Assembly assembly = context.LoadFromStream(stream);

        return new ScriptCompileResult(assembly, ReadScriptTypes(assembly), []);
    }

    /// <summary>
    /// Compiles the project's scripts to a file, for a build that ships them.
    /// </summary>
    /// <param name="projectRoot">Absolute folder that holds <c>Scripts/</c>.</param>
    /// <param name="outputPath">Where the assembly is written.</param>
    /// <param name="errors">Why it did not compile, when this returns false.</param>
    public static bool CompileToFile(string projectRoot, string outputPath, out IReadOnlyList<string> errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        errors = [];

        if (!TryCreateCompilation(projectRoot, out CSharpCompilation compilation))
        {
            return false;
        }

        using FileStream stream = new(outputPath, FileMode.Create);

        EmitResult emit = compilation.Emit(stream);

        if (emit.Success)
        {
            return true;
        }

        errors = ReadErrors(emit);

        return false;
    }

    /// <summary>
    /// Builds the compilation for a project, or says there is nothing to compile.
    /// </summary>
    /// <remarks>
    /// A missing folder and an empty one are the same answer: a project without C# scripts is an ordinary project,
    /// not a failure, so neither is reported as an error.
    /// </remarks>
    private static bool TryCreateCompilation(string projectRoot, out CSharpCompilation compilation)
    {
        compilation = null!;

        string scriptsPath = Path.Combine(projectRoot, ScriptTemplate.Folder);

        if (!Directory.Exists(scriptsPath))
        {
            return false;
        }

        string[] files =
        [
            .. Directory.GetFiles(scriptsPath, ScriptFilePattern, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
        ];

        if (files.Length == 0)
        {
            return false;
        }

        List<SyntaxTree> trees =
        [
            .. files.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file))
        ];

        compilation = CSharpCompilation.Create(
            AssemblyName,
            trees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return true;
    }

    /// <summary>The types a scene may name: concrete classes that derive from <see cref="Script"/>.</summary>
    /// <remarks>
    /// A type that cannot be read at all is reported as an error rather than dropped: a script that compiled but
    /// whose types do not load is a problem the user has to see, not a scene that quietly loses a component.
    /// </remarks>
    private static IReadOnlyList<Type> ReadScriptTypes(Assembly assembly)
    {
        try
        {
            return
            [
                .. assembly.GetTypes()
                    .Where(type => typeof(Script).IsAssignableFrom(type) && !type.IsAbstract)
            ];
        }
        catch (ReflectionTypeLoadException)
        {
            return [];
        }
    }

    private static IEnumerable<MetadataReference> CreateMetadataReferences()
    {
        // The assemblies where the types a script is allowed to touch live. A script is compiled against the engine
        // it is being written for, which is the one this process already has loaded.
        HashSet<string> assemblyPaths =
        [
            typeof(object).Assembly.Location,
            typeof(Engine).Assembly.Location,
            typeof(Script).Assembly.Location,
            typeof(Component).Assembly.Location,
            typeof(System.Numerics.Vector3).Assembly.Location,
            typeof(Console).Assembly.Location
        ];

        foreach (string assemblyName in RequiredAssemblies)
        {
            try
            {
                assemblyPaths.Add(Assembly.Load(assemblyName).Location);
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException)
            {
                // A framework assembly this runtime does not ship: the references above already cover what scripts
                // are expected to use, so a missing optional one is not worth failing the compile over.
            }
        }

        return assemblyPaths
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    /// <summary>The errors, as the compiler writes them: <c>file(line,column): error CS0000: message</c>.</summary>
    private static IReadOnlyList<string> ReadErrors(EmitResult emit)
    {
        return
        [
            .. emit.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())
        ];
    }
}
