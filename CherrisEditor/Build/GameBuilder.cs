using Cherris.Core.Logging;
using NativeFileDialogNET;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CherrisEditor.Build;

public static class GameBuilder
{
    public static async void BuildGame(Project project)
    {
        if (project is null)
        {
            Logger.Error("[GameBuilder] No project loaded.");
            return;
        }

        using var dialog = new NativeFileDialog().SelectFolder();
        if (dialog.Open(out string? outputDir) != DialogResult.Okay || string.IsNullOrEmpty(outputDir))
        {
            return;
        }

        Logger.Info($"[GameBuilder] Starting build process...");
        Logger.Info($"[GameBuilder] Output Directory: {outputDir}");

        string gameName = SanitizeGameName(project.Name);

        await Task.Run(() =>
        {
            try
            {
                // Step 1: Publish the runtime with the custom assembly name
                // We assume the runtime project is where we expect it relative to the editor
                if (!PublishRuntime(outputDir, gameName))
                {
                    Logger.Error("[GameBuilder] Build aborted due to Runtime publish failure.");
                    return;
                }

                // Step 2: Clean up editor-only files
                CleanupBuildArtifacts(outputDir, gameName);

                // Step 3: Compile game scripts
                if (!CompileScripts(project, outputDir))
                {
                    Logger.Error("[GameBuilder] Build aborted due to Script compilation failure.");
                    return;
                }

                // Step 4: Copy assets
                CopyEngineResources(outputDir);
                CopyProjectContent(project, outputDir);
                CopyConfigFile(project, outputDir);

                Logger.Info("[GameBuilder] Build completed successfully!");
                Logger.Info($"[GameBuilder] Opening folder: {outputDir}");

                Process.Start("explorer.exe", outputDir);
            }
            catch (Exception ex)
            {
                Logger.Error($"[GameBuilder] Critical Build Error: {ex.Message}");
            }
        });
    }

    private static string SanitizeGameName(string name)
    {
        string cleanName = Regex.Replace(name, @"[^a-zA-Z0-9_]", "");
        return string.IsNullOrEmpty(cleanName) ? "Game" : cleanName;
    }

    private static bool PublishRuntime(string outputDir, string gameName)
    {
        string runtimeProjPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../CherrisRuntime/CherrisRuntime.csproj"));

        if (!File.Exists(runtimeProjPath))
        {
            runtimeProjPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../CherrisRuntime/CherrisRuntime.csproj"));
        }

        if (!File.Exists(runtimeProjPath))
        {
            Logger.Error($"[GameBuilder] Could not find CherrisRuntime.csproj at '{runtimeProjPath}'.");
            return false;
        }

        Logger.Info($"[GameBuilder] Step 1/5: Publishing Runtime as '{gameName}.exe'...");

        string originalContent = File.ReadAllText(runtimeProjPath);
        string modifiedContent = originalContent;

        // Inject AssemblyName into the project file temporarily.
        // We look for the first PropertyGroup to inject the name.
        if (!originalContent.Contains("<AssemblyName>"))
        {
            modifiedContent = originalContent.Replace("<PropertyGroup>", $"<PropertyGroup>\n    <AssemblyName>{gameName}</AssemblyName>");
        }
        else
        {
            // If it already exists (unlikely given our codebase), regex replace it
            modifiedContent = Regex.Replace(originalContent, @"<AssemblyName>.*?</AssemblyName>", $"<AssemblyName>{gameName}</AssemblyName>");
        }

        try
        {
            File.WriteAllText(runtimeProjPath, modifiedContent);

            // Publish WITHOUT the global /p flag, relying on the project file modification
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{runtimeProjPath}\" -c Release -o \"{outputDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { Logger.Info($"[DotNet] {e.Data}"); } };
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { Logger.Warning($"[DotNet] {e.Data}"); } };

            if (!process.Start())
            {
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        finally
        {
            // Always restore the project file, even if the build fails
            File.WriteAllText(runtimeProjPath, originalContent);
        }
    }

    private static void CleanupBuildArtifacts(string outputDir, string gameName)
    {
        Logger.Info("[GameBuilder] Step 2/5: Cleaning up editor artifacts...");

        // We clean up files that shouldn't be there.
        // Note: CherrisRuntime.dll is now named {gameName}.dll by the build process itself.
        // We must be careful not to delete the game executable.

        string[] filesToDelete =
        {
            "CherrisEditor.exe",
            "CherrisEditor.dll",
            "CherrisEditor.pdb",
            "CherrisEditor.runtimeconfig.json",
            "ImGuizmo.NET.dll",
            "NativeFileDialogNET.dll",
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "Microsoft.CodeAnalysis.VisualBasic.dll"
        };

        foreach (var file in filesToDelete)
        {
            string path = Path.Combine(outputDir, file);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    Logger.Warning($"[GameBuilder] Failed to delete artifact: {file}");
                }
            }
        }
    }

    private static bool CompileScripts(Project project, string outputDir)
    {
        Logger.Info("[GameBuilder] Step 3/5: Compiling Game Scripts...");
        string dllPath = Path.Combine(outputDir, "GameScripts.dll");
        return ScriptCompiler.CompileToFile(project.RootPath, dllPath);
    }

    private static void CopyEngineResources(string outputDir)
    {
        Logger.Info("[GameBuilder] Step 4/5: Copying Engine Resources...");

        string sourceDir = Path.Combine(AppContext.BaseDirectory, "EditorResources");
        string destDir = Path.Combine(outputDir, "EditorResources");

        if (Directory.Exists(sourceDir))
        {
            CopyDirectoryRecursively(sourceDir, destDir);
        }
        else
        {
            Logger.Warning("[GameBuilder] 'EditorResources' folder not found in Editor directory. Runtime visuals might fail.");
        }
    }

    private static void CopyProjectContent(Project project, string outputDir)
    {
        Logger.Info("[GameBuilder] Step 5/5: Copying Project Content...");

        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", ".vs", ".idea", "Builds", "Logs"
        };

        var excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".csproj", ".sln", ".pdb", ".user", ".cs"
        };

        string fullOutputDir = Path.GetFullPath(outputDir);
        string fullProjectRoot = Path.GetFullPath(project.RootPath);

        void CopyRecursive(string source, string dest)
        {
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(source))
            {
                if (excludedExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                string destFile = Path.Combine(dest, Path.GetFileName(file));
                if (File.Exists(destFile))
                {
                    continue;
                }

                File.Copy(file, destFile, true);
            }

            foreach (var subDir in Directory.GetDirectories(source))
            {
                string dirName = Path.GetFileName(subDir);
                if (excludedDirs.Contains(dirName))
                {
                    continue;
                }

                string fullSubPath = Path.GetFullPath(subDir);
                if (fullOutputDir.StartsWith(fullSubPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CopyRecursive(subDir, Path.Combine(dest, dirName));
            }
        }

        CopyRecursive(fullProjectRoot, outputDir);
    }

    private static void CopyConfigFile(Project project, string outputDir)
    {
        Logger.Info("[GameBuilder] Finalizing...");
        string destPath = Path.Combine(outputDir, "project.yaml");
        File.Copy(project.ConfigPath, destPath, true);
    }

    private static void CopyDirectoryRecursively(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursively(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }
    }
}