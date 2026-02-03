using Cherris.Components;
using Cherris.Core.Logging;
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

        // ScriptCompiler.Compile expects the Project Root (parent of Scripts folder)
        Assembly? gameAssembly = ScriptCompiler.Compile(projectRoot, _gameAssemblyContext);

        if (gameAssembly is null)
        {
            Logger.Warning("[Editor] Game script compilation failed or no scripts found. No custom components will be loaded.");
            return false;
        }

        try
        {
            IEnumerable<Type> scriptTypes = gameAssembly.GetTypes()
                .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract);

            int count = 0;
            foreach (Type type in scriptTypes)
            {
                RegisterScriptComponent(type);
                count++;
            }
            Logger.Info($"[Editor] Loaded {count} custom components from runtime-compiled assembly.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[Editor] FATAL: Error processing runtime-compiled assembly. Reason: {ex.Message}");
            return false;
        }
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