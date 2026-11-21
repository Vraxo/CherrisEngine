using System.Diagnostics;
using System.Reflection;

namespace CherrisEditor;

/// <summary>Opens the launcher in another editor process, leaving the current project untouched.</summary>
public static class ProjectLauncherProcess
{
    public static void Start(bool createProject)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the current editor executable.");
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        var startInfo = CreateStartInfo(processPath, entryAssembly, createProject);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the project launcher.");
    }

    public static ProcessStartInfo CreateStartInfo(string processPath, string? entryAssembly, bool createProject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        var startInfo = new ProcessStartInfo(processPath) { UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entryAssembly))
                throw new InvalidOperationException("Could not determine the editor assembly to launch.");
            startInfo.ArgumentList.Add(entryAssembly);
        }
        startInfo.ArgumentList.Add(createProject ? "--new-project" : "--launcher");
        return startInfo;
    }
}
