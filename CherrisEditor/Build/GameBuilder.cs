using Cherris.Core.Logging;
using NativeFileDialogNET;
using System.Diagnostics;

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

        await Task.Run(() =>
        {
            try
            {
                if (!PublishRuntime(outputDir))
                {
                    Logger.Error("[GameBuilder] Build aborted due to Runtime publish failure.");
                    return;
                }

                if (!CompileScripts(project, outputDir))
                {
                    Logger.Error("[GameBuilder] Build aborted due to Script compilation failure.");
                    return;
                }

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

    private static bool PublishRuntime(string outputDir)
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

        Logger.Info("[GameBuilder] Step 1/5: Publishing Runtime...");

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

    private static bool CompileScripts(Project project, string outputDir)
    {
        Logger.Info("[GameBuilder] Step 2/5: Compiling Game Scripts...");
        string dllPath = Path.Combine(outputDir, "GameScripts.dll");
        return ScriptCompiler.CompileToFile(project.RootPath, dllPath);
    }

    private static void CopyEngineResources(string outputDir)
    {
        // Copy built-in resources (Shaders, Fonts, Default Textures) from the Editor's execution directory
        Logger.Info("[GameBuilder] Step 3/5: Copying Engine Resources...");

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
        Logger.Info("[GameBuilder] Step 4/5: Copying Project Content...");

        // Exclude build artifacts, source control, and source code
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

                // Don't overwrite the main executable or dlls we just built
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

                // Prevent recursive copy if output dir is inside project
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
        Logger.Info("[GameBuilder] Step 5/5: Copying Configuration...");
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