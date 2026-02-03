using Cherris.Core;
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

        Logger.Info($"[GameBuilder] Starting build process for '{project.Name}'...");
        Logger.Info($"[GameBuilder] Mode: {(project.PackAssets ? "Packed (Assets.pak)" : "Loose Files")}");

        string gameName = SanitizeGameName(project.Name);

        await Task.Run(() =>
        {
            try
            {
                if (!PublishRuntime(outputDir, gameName))
                {
                    return;
                }

                CleanupBuildArtifacts(outputDir, gameName);
                if (!CompileScripts(project, outputDir))
                {
                    return;
                }

                CopyEngineResources(outputDir);

                // Asset Handling: Pack or Copy
                if (project.PackAssets)
                {
                    PackAssets(project, outputDir);
                }
                else
                {
                    CopyProjectContent(project, outputDir);
                }

                CopyConfigFile(project, outputDir);

                Logger.Info("[GameBuilder] Build completed successfully!");
                Process.Start("explorer.exe", outputDir);
            }
            catch (Exception ex)
            {
                Logger.Error($"[GameBuilder] Critical Build Error: {ex.Message}");
            }
        });
    }

    private static void PackAssets(Project project, string outputDir)
    {
        Logger.Info("[GameBuilder] Packing assets into Assets.pak...");
        string pakPath = Path.Combine(outputDir, "Assets.pak");
        AssetBundle.Create(pakPath, project.RootPath);
    }

    // ... (SanitizeGameName, PublishRuntime, CleanupBuildArtifacts, CompileScripts, CopyEngineResources remain unchanged) ...
    // Note: I will include the full file content for correctness as per protocol.

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

        Logger.Info($"[GameBuilder] Publishing Runtime as '{gameName}.exe'...");

        string originalContent = File.ReadAllText(runtimeProjPath);
        string modifiedContent = originalContent;

        if (!originalContent.Contains("<AssemblyName>"))
        {
            modifiedContent = originalContent.Replace("<PropertyGroup>", $"<PropertyGroup>\n    <AssemblyName>{gameName}</AssemblyName>");
        }
        else
        {
            modifiedContent = Regex.Replace(originalContent, @"<AssemblyName>.*?</AssemblyName>", $"<AssemblyName>{gameName}</AssemblyName>");
        }

        try
        {
            File.WriteAllText(runtimeProjPath, modifiedContent);

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
            File.WriteAllText(runtimeProjPath, originalContent);
        }
    }

    private static void CleanupBuildArtifacts(string outputDir, string gameName)
    {
        string[] filesToDelete =
        {
            "CherrisEditor.exe", "CherrisEditor.dll", "CherrisEditor.pdb", "CherrisEditor.runtimeconfig.json",
            "ImGuizmo.NET.dll", "NativeFileDialogNET.dll",
            "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll", "Microsoft.CodeAnalysis.VisualBasic.dll"
        };

        foreach (var file in filesToDelete)
        {
            string path = Path.Combine(outputDir, file);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    private static bool CompileScripts(Project project, string outputDir)
    {
        Logger.Info("[GameBuilder] Compiling Game Scripts...");
        string dllPath = Path.Combine(outputDir, "GameScripts.dll");
        return ScriptCompiler.CompileToFile(project.RootPath, dllPath);
    }

    private static void CopyEngineResources(string outputDir)
    {
        string sourceDir = Path.Combine(AppContext.BaseDirectory, "EditorResources");
        string destDir = Path.Combine(outputDir, "EditorResources");
        if (Directory.Exists(sourceDir))
        {
            CopyDirectoryRecursively(sourceDir, destDir);
        }
    }

    private static void CopyProjectContent(Project project, string outputDir)
    {
        Logger.Info("[GameBuilder] Copying Project Content...");
        string assetsDir = Path.Combine(outputDir, "Assets");
        Directory.CreateDirectory(assetsDir);

        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "Builds", "Logs" };
        var excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csproj", ".sln", ".pdb", ".user", ".cs" };
        var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "project.yaml" };

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

                if (excludedFiles.Contains(Path.GetFileName(file)))
                {
                    continue;
                }

                string destFile = Path.Combine(dest, Path.GetFileName(file));
                if (!File.Exists(destFile))
                {
                    File.Copy(file, destFile, true);
                }
            }
            foreach (var subDir in Directory.GetDirectories(source))
            {
                string dirName = Path.GetFileName(subDir);
                if (excludedDirs.Contains(dirName))
                {
                    continue;
                }

                if (fullOutputDir.StartsWith(Path.GetFullPath(subDir), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CopyRecursive(subDir, Path.Combine(dest, dirName));
            }
        }
        CopyRecursive(fullProjectRoot, assetsDir);
    }

    private static void CopyConfigFile(Project project, string outputDir)
    {
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