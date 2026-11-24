using Cherris.Components;
using Cherris.Core.Logging;
using Cherris.Scripting;
using Cherris.Serialization;
using System.Reflection;
using System.Runtime.Loader;

namespace CherrisEditor.Core;

public class ScriptManager
{
    public List<Type> AvailableScriptTypes { get; } = [];

    private AssemblyLoadContext _gameAssemblyContext;
    private readonly SceneLoader _sceneLoader;

    public ScriptManager(SceneLoader sceneLoader)
    {
        _sceneLoader = sceneLoader;
        _gameAssemblyContext = new AssemblyLoadContext("GameScriptsContext", isCollectible: true);
    }

    public bool CompileAndRegisterGameScripts(string projectRoot)
    {
        if (_gameAssemblyContext.Assemblies.Any())
        {
            var typesToRemove = AvailableScriptTypes
                .Where(t => AssemblyLoadContext.GetLoadContext(t.Assembly) == _gameAssemblyContext)
                .ToList();

            foreach (var type in typesToRemove)
            {
                AvailableScriptTypes.Remove(type);
            }

            _gameAssemblyContext.Unload();
            Logger.Info("[Editor] Unloaded old game assembly.");
        }

        _gameAssemblyContext = new AssemblyLoadContext("GameScriptsContext", isCollectible: true);

        string scriptsPath = Path.Combine(projectRoot, "Scripts");
        if (!Directory.Exists(scriptsPath) || !Directory.EnumerateFiles(scriptsPath, "*.cs", SearchOption.AllDirectories).Any())
        {
            Logger.Info("[Editor] No C# scripts to compile.");
            return true;
        }

        // The project root, not Scripts/: the compiler owns the Scripts/ convention.
        ScriptCompileResult result = ProjectScriptCompiler.Compile(projectRoot, _gameAssemblyContext);

        if (!result.Succeeded)
        {
            Logger.Warning($"[Editor] {result.Errors.Count} error(s) compiling game scripts:");

            foreach (string error in result.Errors)
            {
                Logger.Error($"  {error}");
            }

            Logger.Warning("[Editor] No custom components will be loaded.");
            return false;
        }

        foreach (Type type in result.ScriptTypes)
        {
            RegisterScriptComponent(type);
        }

        Logger.Info($"[Editor] Loaded {result.ScriptTypes.Count} custom components from runtime-compiled assembly.");
        return true;
    }

    public void RegisterScriptComponent(Type scriptType)
    {
        AvailableScriptTypes.Add(scriptType);

        Component Factory(object _)
        {
            return (Component)Activator.CreateInstance(scriptType)!;
        }

        _sceneLoader.RegisterComponentFactory(scriptType.Name, Factory);
        // Logger.Info($"[Editor] Registered component: {scriptType.Name}"); // Reduced spam
    }

    public void CreateAndCompileScript(string projectRoot, string scriptName)
    {
        if (!IsValidCSharpIdentifier(scriptName))
        {
            Logger.Error($"[Editor] '{scriptName}' is not a valid C# class name.");
            return;
        }

        string scriptsPath = Path.Combine(projectRoot, "Scripts");
        Directory.CreateDirectory(scriptsPath);

        string filePath = Path.Combine(scriptsPath, $"{scriptName}.cs");

        if (File.Exists(filePath))
        {
            Logger.Error($"[Editor] A script named '{scriptName}.cs' already exists.");
            return;
        }

        string content = ScriptTemplate.GetContent(scriptName);
        File.WriteAllText(filePath, content);
        Logger.Info($"[Editor] Created new script at '{filePath}'");

        CompileAndRegisterGameScripts(projectRoot);
    }

    private static bool IsValidCSharpIdentifier(string identifier)
    {
        return !string.IsNullOrWhiteSpace(identifier) && System.Text.RegularExpressions.Regex.IsMatch(identifier, @"^[_a-zA-Z][_a-zA-Z0-9]*$");
    }
}