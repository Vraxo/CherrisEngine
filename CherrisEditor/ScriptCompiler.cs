using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;

namespace CherrisEditor;

/// <summary>
/// A service that uses Roslyn to find and compile C# script files from the Assets folder
/// into a dynamic, in-memory assembly at runtime.
/// </summary>
public static class ScriptCompiler
{
    public static Assembly? Compile(string rootAssetPath, AssemblyLoadContext context)
    {
        string scriptsPath = Path.Combine(rootAssetPath, "Scripts");
        if (!Directory.Exists(scriptsPath))
        {
            Console.WriteLine("[ScriptCompiler] 'Assets/Scripts' directory not found. No scripts to compile.");
            return null;
        }

        var scriptFiles = Directory.GetFiles(scriptsPath, "*.cs", SearchOption.AllDirectories);
        if (scriptFiles.Length == 0)
        {
            Console.WriteLine("[ScriptCompiler] No C# script files found in 'Assets/Scripts'.");
            return null;
        }

        Console.WriteLine($"[ScriptCompiler] Found {scriptFiles.Length} script(s). Starting compilation...");

        var syntaxTrees = scriptFiles
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file))
            .ToList();

        var assemblyPaths = new HashSet<string>
        {
            typeof(object).Assembly.Location,
            Assembly.Load("System.Runtime").Location,
            typeof(Cherris.Engine).Assembly.Location,
            typeof(System.Numerics.Vector3).Assembly.Location,
            Assembly.Load("System.Numerics.Vectors").Location
        };

        var references = assemblyPaths.Select(path => MetadataReference.CreateFromFile(path)).ToList();

        var compilation = CSharpCompilation.Create(
            "GameScriptsAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            Console.WriteLine("[ScriptCompiler] Compilation failed!");
            var failures = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

            foreach (var diagnostic in failures)
            {
                Console.Error.WriteLine($"  {diagnostic.Id}: {diagnostic.GetMessage()} at {diagnostic.Location}");
            }
            return null;
        }

        Console.WriteLine("[ScriptCompiler] Compilation successful.");

        ms.Seek(0, SeekOrigin.Begin);
        return context.LoadFromStream(ms);
    }
}