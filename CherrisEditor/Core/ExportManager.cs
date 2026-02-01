using Cherris.Core.Logging;
using System.Reflection;

namespace CherrisEditor.Core;

public static class ExportManager
{
    public static void Export(Project? project)
    {
        if (project == null)
        {
            Logger.Error("[Export] No project loaded.");
            return;
        }

        string buildPath = Path.Combine(project.RootPath, "Build");
        Directory.CreateDirectory(buildPath);

        CopyAssets(project.RootPath, buildPath);
        CompileScripts(project.RootPath, buildPath);
        CopyConfiguration(project.RootPath, buildPath);
        CopyRuntimeFiles(buildPath);
        CreateLauncher(buildPath, project.Name);

        Logger.Info($"[Export] Successfully exported to: {buildPath}");
    }

    private static void CopyAssets(string sourceRoot, string buildRoot)
    {
        string sourceAssets = Path.Combine(sourceRoot, "Assets");
        string destAssets = Path.Combine(buildRoot, "Assets");

        if (!Directory.Exists(sourceAssets))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(sourceAssets, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceAssets, dir);
            string destDir = Path.Combine(destAssets, relativePath);
            Directory.CreateDirectory(destDir);
        }

        foreach (var file in Directory.GetFiles(sourceAssets, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceAssets, file);
            string destFile = Path.Combine(destAssets, relativePath);
            File.Copy(file, destFile, true);
        }
    }

    private static void CompileScripts(string sourceRoot, string buildRoot)
    {
        string scriptsPath = Path.Combine(sourceRoot, "Scripts");

        if (!Directory.Exists(scriptsPath) || !Directory.GetFiles(scriptsPath, "*.cs").Any())
        {
            return;
        }

        string outputPath = Path.Combine(buildRoot, "GameScripts.dll");

        if (ScriptCompiler.CompileToFile(sourceRoot, outputPath))
        {
            Logger.Info("[Export] Scripts compiled successfully.");
        }
        else
        {
            Logger.Error("[Export] Failed to compile scripts.");
        }
    }

    private static void CopyConfiguration(string sourceRoot, string buildRoot)
    {
        string configSource = Path.Combine(sourceRoot, "project.yaml");
        string configDest = Path.Combine(buildRoot, "project.yaml");

        if (File.Exists(configSource))
        {
            File.Copy(configSource, configDest, true);
        }
    }

    private static void CopyRuntimeFiles(string buildPath)
    {
        string? currentExe = Assembly.GetEntryAssembly()?.Location;
        if (currentExe == null)
        {
            Logger.Warning("[Export] Could not locate current executable.");
            return;
        }

        string sourceDir = Path.GetDirectoryName(currentExe)!;
        string exeName = Path.GetFileName(currentExe);

        try
        {
            // Copy all files from source directory (preserving structure)
            CopyDirectory(sourceDir, buildPath);

            Logger.Info($"[Export] Copied runtime files from {sourceDir}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[Export] Failed to copy runtime files: {ex.Message}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        // Copy files
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destPath = Path.Combine(destDir, fileName);

            // Skip if it's a .pdb file to save space (optional)
            if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, destPath, true);
        }

        // Copy subdirectories (like runtimes/)
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(dir);

            // Skip certain directories
            if (dirName.Equals("Build", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destSubDir = Path.Combine(destDir, dirName);
            Directory.CreateDirectory(destSubDir);
            CopyDirectory(dir, destSubDir);
        }
    }

    private static void CreateLauncher(string buildPath, string projectName)
    {
        string exeName = Path.GetFileName(Assembly.GetEntryAssembly()?.Location ?? "CherrisEditor.exe");
        string batchPath = Path.Combine(buildPath, $"Play {projectName}.bat");

        File.WriteAllText(batchPath,
            $"@echo off\n" +
            $"cd /d \"%~dp0\"\n" +
            $"if not exist \"{exeName}\" (\n" +
            $"  echo Error: {exeName} not found in build folder.\n" +
            $"  pause\n" +
            $"  exit /b 1\n" +
            $")\n" +
            $"\n" +
            $"echo Starting {projectName}...\n" +
            $"\"{exeName}\" --player \"%~dp0\"\n" +
            $"\n" +
            $"if errorlevel 1 (\n" +
            $"  echo.\n" +
            $"  echo Game exited with error code %errorlevel%.\n" +
            $"  pause\n" +
            $")\n");
    }
}