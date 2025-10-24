using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace CherrisEditor;

/// <summary>
/// A service that uses Roslyn to find and compile C# script files from the Assets folder
/// into a dynamic, in-memory assembly at runtime.
/// </summary>
public static class ScriptCompiler
{
    public static Assembly? Compile(string rootAssetPath)
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

        // 1. Parse all script files into Roslyn syntax trees
        var syntaxTrees = scriptFiles
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file))
            .ToList();

        // 2. Define the references our scripts will need to compile
        // This is the crucial part: scripts need to know about the .NET runtime and our engine.
        var assemblyPaths = new HashSet<string>
        {
            // Add core .NET assemblies
            typeof(object).Assembly.Location,
            Assembly.Load("System.Runtime").Location,
            // Add engine assembly
            typeof(Cherris.Engine).Assembly.Location,
            // Add Numerics for Vector3, etc.
            typeof(System.Numerics.Vector3).Assembly.Location,
            // Explicitly add the assembly mentioned in the error log to ensure it's included
            Assembly.Load("System.Numerics.Vectors").Location
        };

        var references = assemblyPaths.Select(path => MetadataReference.CreateFromFile(path)).ToList();

        // 3. Set up the compilation
        var compilation = CSharpCompilation.Create(
            "GameScriptsAssembly", // The name of our dynamic DLL
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 4. Compile into a memory stream
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        // 5. Check for compilation errors
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

        // 6. Load the compiled assembly from the memory stream
        ms.Seek(0, SeekOrigin.Begin);
        return AssemblyLoadContext.Default.LoadFromStream(ms);
    }
}