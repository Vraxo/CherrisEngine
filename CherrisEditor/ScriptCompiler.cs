using Cherris.Core;
using Cherris.Core.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Reflection;
using System.Runtime.Loader;

namespace CherrisEditor;

public static class ScriptCompiler
{
    private const string ScriptsDirectoryName = "Scripts";
    private const string ScriptFilePattern = "*.cs";
    private const string CompiledAssemblyName = "GameScriptsAssembly";

    private static readonly string[] RequiredAssemblies =
    {
        "System.Runtime",
        "System.Numerics.Vectors"
    };

    public static Assembly? Compile(string rootAssetPath, AssemblyLoadContext context)
    {
        var compilation = PrepareCompilation(rootAssetPath);
        if (compilation is null)
        {
            return null;
        }

        using MemoryStream stream = new();
        EmitResult result = compilation.Emit(stream);

        if (!result.Success)
        {
            LogCompilationErrors(result);
            return null;
        }

        Logger.Info("[ScriptCompiler] In-memory compilation successful.");
        stream.Position = 0;
        return context.LoadFromStream(stream);
    }

    public static bool CompileToFile(string rootAssetPath, string outputPath)
    {
        var compilation = PrepareCompilation(rootAssetPath);
        if (compilation is null)
        {
            return false;
        }

        // We emit to a file stream instead of memory
        using var stream = new FileStream(outputPath, FileMode.Create);
        EmitResult result = compilation.Emit(stream);

        if (!result.Success)
        {
            LogCompilationErrors(result);
            return false;
        }

        Logger.Info($"[ScriptCompiler] Compiled scripts to '{outputPath}'.");
        return true;
    }

    private static CSharpCompilation? PrepareCompilation(string rootAssetPath)
    {
        string scriptsPath = Path.Combine(rootAssetPath, ScriptsDirectoryName);

        if (!TryGetScriptFiles(scriptsPath, out var files))
        {
            return null;
        }

        List<SyntaxTree> syntaxTrees = ParseSyntaxTrees(files);
        IEnumerable<MetadataReference> references = CreateMetadataReferences();

        return CSharpCompilation.Create(
            CompiledAssemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static bool TryGetScriptFiles(string scriptsPath, out string[] files)
    {
        files = Array.Empty<string>();

        if (!Directory.Exists(scriptsPath))
        {
            // Just a warning for runtime build, but info for editor
            Logger.Info($"[ScriptCompiler] '{ScriptsDirectoryName}' directory not found.");
            return false;
        }

        files = Directory.GetFiles(scriptsPath, ScriptFilePattern, SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            Logger.Info($"[ScriptCompiler] No C# script files found in '{ScriptsDirectoryName}'.");
            return false;
        }

        return true;
    }

    private static List<SyntaxTree> ParseSyntaxTrees(string[] files)
    {
        return [.. files.Select(file =>
        {
            return CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
        })];
    }

    private static IEnumerable<MetadataReference> CreateMetadataReferences()
    {
        HashSet<string> assemblyPaths =
        [
            typeof(object).Assembly.Location,
            typeof(Engine).Assembly.Location,
            typeof(System.Numerics.Vector3).Assembly.Location
        ];

        foreach (string assemblyName in RequiredAssemblies)
        {
            try
            {
                var asm = Assembly.Load(assemblyName);
                _ = assemblyPaths.Add(asm.Location);
            }
            catch
            {
                // Ignore if not found, standard lib might be implicitly loaded
            }
        }

        return assemblyPaths.Select(path => MetadataReference.CreateFromFile(path));
    }

    private static void LogCompilationErrors(EmitResult result)
    {
        Logger.Warning("[ScriptCompiler] Compilation failed!");

        IEnumerable<Diagnostic> failures = result.Diagnostics.Where(d =>
            d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error);

        foreach (Diagnostic diagnostic in failures)
        {
            Logger.Error($"  {diagnostic.Id}: {diagnostic.GetMessage()} at {diagnostic.Location}");
        }
    }
}