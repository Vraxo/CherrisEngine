using Cherris.Scripting;
using Cherris.Utils;

namespace Cherris.Components;

/// <summary>A script backed by a registered language adapter and a project-relative source file.</summary>
public sealed class LanguageScript : Script
{
    public string Language { get; set; } = "lua";
    public string SourcePath { get; set; } = string.Empty;
    private IScriptInstance? _instance;

    public override void Start()
    {
        _instance = null;
        string path = ValidateSourcePath(SourcePath);
        using var stream = ProjectFiles.Open(path) ?? throw new FileNotFoundException($"Script source not found: {path}");
        using var reader = new StreamReader(stream);
        var instance = ScriptLanguages.Get(Language).CreateInstance(reader.ReadToEnd(), path,
            GameObject ?? throw new InvalidOperationException("Script is not attached to a game object."));
        instance.Start();
        _instance = instance;
    }

    public override void Update(float deltaTime) => _instance?.Update(deltaTime);

    public static string ValidateSourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Set a script source path under Scripts/.");
        string normalized = path.Replace('\\', '/');
        string[] parts = normalized.Split('/');
        if (parts.Length < 2 || !string.Equals(parts[0], "Scripts", StringComparison.OrdinalIgnoreCase) ||
            parts.Any(part => part is "" or "." or ".." || part.Contains(':')))
            throw new ArgumentException("Script paths must be project-relative and inside Scripts/.");
        return normalized;
    }
}
