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
        string scriptsPath = Path.Combine(rootAssetPath, ScriptsDirectoryName);

        if (!TryGetScriptFiles(scriptsPath, out var files))
        {
            return null;
        }

        List<SyntaxTree> syntaxTrees = ParseSyntaxTrees(files);
        IEnumerable<MetadataReference> references = CreateMetadataReferences();
        CSharpCompilation compilation = CreateCompilation(syntaxTrees, references);

        return EmitAssembly(compilation, context);
    }

    public static bool CompileToFile(string rootAssetPath, string outputPath)
    {
        string scriptsPath = Path.Combine(rootAssetPath, ScriptsDirectoryName);

        if (!TryGetScriptFiles(scriptsPath, out var files))
        {
            return false;
        }

        List<SyntaxTree> syntaxTrees = ParseSyntaxTrees(files);
        IEnumerable<MetadataReference> references = CreateMetadataReferences();
        CSharpCompilation compilation = CreateCompilation(syntaxTrees, references);

        try
        {
            EmitResult result = compilation.Emit(outputPath);

            if (!result.Success)
            {
                LogCompilationErrors(result);
                return false;
            }

            Logger.Info($"[ScriptCompiler] Successfully compiled to {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[ScriptCompiler] Failed to emit assembly: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetScriptFiles(string scriptsPath, out string[] files)
    {
        files = Array.Empty<string>();

        if (!Directory.Exists(scriptsPath))
        {
            Logger.Info($"[ScriptCompiler] '{ScriptsDirectoryName}' directory not found. No scripts to compile.");
            return false;
        }

        files = Directory.GetFiles(scriptsPath, ScriptFilePattern, SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            Logger.Info($"[ScriptCompiler] No C# script files found in '{ScriptsDirectoryName}'.");
            return false;
        }

        Logger.Info($"[ScriptCompiler] Found {files.Length} script(s). Starting compilation...");
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
            _ = assemblyPaths.Add(Assembly.Load(assemblyName).Location);
        }

        return assemblyPaths.Select(path => MetadataReference.CreateFromFile(path));
    }

    private static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> syntaxTrees, IEnumerable<MetadataReference> references)
    {
        return CSharpCompilation.Create(
            CompiledAssemblyName,
            syntaxTrees,
            references,
            new(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Assembly? EmitAssembly(CSharpCompilation compilation, AssemblyLoadContext context)
    {
        using MemoryStream stream = new();
        EmitResult result = compilation.Emit(stream);

        if (!result.Success)
        {
            LogCompilationErrors(result);
            return null;
        }

        Logger.Info("[ScriptCompiler] Compilation successful.");
        stream.Position = 0;
        return context.LoadFromStream(stream);
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