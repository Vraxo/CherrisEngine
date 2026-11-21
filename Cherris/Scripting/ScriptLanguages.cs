using Cherris.Core;

namespace Cherris.Scripting;

/// <summary>A language adapter creates an isolated instance for one game object.</summary>
public interface IScriptLanguage
{
    IScriptInstance CreateInstance(string source, string sourceName, GameObject gameObject);
}

public interface IScriptInstance
{
    void Start();
    void Update(float deltaTime);
}

/// <summary>Registered adapters are available in both the editor and the game runtime.</summary>
public static class ScriptLanguages
{
    private static readonly Dictionary<string, IScriptLanguage> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lua"] = new LuaScriptLanguage()
    };

    public static void Register(string name, IScriptLanguage language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(language);
        Languages[name] = language;
    }

    public static IScriptLanguage Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Languages.TryGetValue(name, out var language))
            throw new InvalidOperationException($"Unknown script language '{name}'.");
        return language;
    }
}
