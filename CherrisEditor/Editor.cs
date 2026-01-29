using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.OpenTK;
using Cherris.Serialization;
using Cherris.Utils;
using CherrisEditor.Undo;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace CherrisEditor;

public class Editor : Engine
{
    private EditorAppLogic? _editorAppLogic;
    public List<Type> AvailableScriptTypes { get; } = [];
    public EditorState State { get; private set; } = EditorState.Editing;
    public bool IsViewportHovered { get; set; }
    public readonly HistoryManager History = new();
    public Project? CurrentProject { get; private set; }

    private Camera? _editorCamera;
    private AssemblyLoadContext _gameAssemblyContext;
    private readonly SceneSerializer _sceneSerializer;


    public Editor(GraphicsAPI api) : base("Cherris Editor", false, api)
    {
        Exposure = 0.5f;
        _gameAssemblyContext = new AssemblyLoadContext("GameScriptsContext", isCollectible: true);
        _sceneSerializer = new SceneSerializer();
    }

    public void LoadProject(string projectRoot)
    {
        if (!Directory.Exists(projectRoot))
        {
            Logger.Error($"[Editor] Project directory not found: {projectRoot}");
            return;
        }

        CurrentProject = Project.Load(projectRoot);
        ProjectFiles.ProjectRoot = projectRoot;
        ProjectPersistence.SetLastProject(projectRoot);

        Logger.Info($"[Editor] Loaded project: {CurrentProject.Name}");
    }

    public void CreatePrefabFromGameObject(GameObject go, string path)
    {
        if (go is null || string.IsNullOrEmpty(path))
        {
            return;
        }

        Logger.Info($"[Editor] Creating prefab '{path}' from '{go.Name}'.");
        _sceneSerializer.SavePrefab(go, path);
    }

    public void InstantiatePrefab(string path)
    {
        if (!File.Exists(path))
        {
            Logger.Error($"[Editor] Prefab file not found: {path}");
            return;
        }

        var rootObjects = SceneLoader.LoadPrefab(path);
        foreach (var root in rootObjects)
        {
            SceneManager.AddGameObject(root);
        }

        if (rootObjects.Any())
        {
            SetSelectedGameObject(rootObjects.First());
        }
        SceneManager.ActiveScene.IsDirty = true;
    }

    public void EnterPlayMode()
    {
        if (State == EditorState.Playing)
        {
            return;
        }

        if (State == EditorState.Editing)
        {
            var gameCamera = SceneManager.GameObjects
                .Select(g => g.GetComponent<Camera>())
                .FirstOrDefault(c => c is not null && c != _editorCamera);

            if (gameCamera is not null)
            {
                SceneManager.SetMainCamera(gameCamera);
            }
            else
            {
                Logger.Warning("[Editor] No game camera found to switch to for play mode. Using the editor camera.");
            }

            SceneManager.Start();
        }

        State = EditorState.Playing;
        SetSelectedGameObject(null);
        SetScriptsEnabledForPlayMode();
    }

    public void EnterPauseMode()
    {
        if (State != EditorState.Playing)
        {
            return;
        }

        State = EditorState.Paused;
        SetScriptsEnabledForPauseMode();
    }

    public void EnterEditMode()
    {
        State = EditorState.Editing;
        ReloadSceneForEditing();
    }

    public void RestartPlayMode()
    {
        if (State == EditorState.Editing)
        {
            return;
        }

        EnterEditMode();
        EnterPlayMode();
    }

    public void CreateAndCompileScript(string scriptName)
    {
        if (!IsValidCSharpIdentifier(scriptName))
        {
            Logger.Error($"[Editor] '{scriptName}' is not a valid C# class name.");
            return;
        }

        if (CurrentProject is null)
        {
            Logger.Error("[Editor] No project loaded.");
            return;
        }

        string scriptsPath = Path.Combine(CurrentProject.RootPath, "Scripts");
        _ = Directory.CreateDirectory(scriptsPath);
        string filePath = Path.Combine(scriptsPath, $"{scriptName}.cs");

        if (File.Exists(filePath))
        {
            Logger.Error($"[Editor] A script named '{scriptName}.cs' already exists.");
            return;
        }

        string content = ScriptTemplate.GetContent(scriptName);
        File.WriteAllText(filePath, content);
        Logger.Info($"[Editor] Created new script at '{filePath}'");

        CompileAndRegisterGameScripts();
    }

    private static bool IsValidCSharpIdentifier(string identifier)
    {
        return !string.IsNullOrWhiteSpace(identifier) && Regex.IsMatch(identifier, @"^[_a-zA-Z][_a-zA-Z0-9]*$");
    }

    protected override void LoadContent()
    {
        ResourceManager.LoadInitialAssets();
        RegisterBuiltInComponents();
        CompileAndRegisterGameScripts();

        History.OnHistoryChanged += () =>
        {
            if (SceneManager.ActiveScene is not null)
            {
                SceneManager.ActiveScene.IsDirty = true;
            }
        };
    }

    protected override void OnStart()
    {
        _editorAppLogic = new(this, _sceneSerializer);
        OnDrawUI = _editorAppLogic.Draw;
        SceneManager.OnActiveSceneChanged += SetupSceneForEditing;

        if (CurrentProject is not null)
        {
            string startScenePath = Path.Combine(CurrentProject.RootPath, CurrentProject.StartScene);
            if (File.Exists(startScenePath))
            {
                LoadSceneFromFile(startScenePath);
            }
            else
            {
                Logger.Error($"[Editor] Start scene not found: {startScenePath}");
            }
        }

        if (_backend.GameWindow is OpenTKGameWindow otkWindow)
        {
            otkWindow.ShouldIgnoreImGuiCapture = () => IsViewportHovered;
        }
    }

    protected override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        _editorAppLogic?.UpdateEditorLogic(deltaTime);
        SceneManager.Update(deltaTime, State == EditorState.Playing);
    }

    private void ReloadSceneForEditing()
    {
        var activeScene = SceneManager.ActiveScene;
        if (activeScene is null)
        {
            return;
        }

        string path = activeScene.FilePath;
        SceneManager.CloseScene(activeScene);
        LoadSceneFromFile(path);
    }

    private void SetupSceneForEditing(Scene scene)
    {
        if (scene is null)
        {
            return;
        }

        History.Clear();

        foreach (var script in scene.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = script is RigidBody;
        }

        GameObject? cameraGo = scene.MainCamera?.GameObject;

        cameraGo ??= scene.GameObjects.Select(go => go.GetComponent<Camera>()).FirstOrDefault(c => c is not null)?.GameObject;

        if (cameraGo is not null)
        {
            EnsureEditorControllerEnabled(cameraGo);
            _editorCamera = cameraGo.GetComponent<Camera>();
            SceneManager.SetMainCamera(_editorCamera);
        }
        else
        {
            _editorCamera = null;
            Logger.Error("[Editor] FATAL: No camera found in scene to attach editor controls to.");
        }
    }


    public void LoadSceneFromFile(string scenePath)
    {
        var existingScene = SceneManager.OpenScenes.FirstOrDefault(s => s.FilePath == scenePath);
        if (existingScene is not null)
        {
            SceneManager.SetActiveScene(existingScene);
            return;
        }

        if (!File.Exists(scenePath))
        {
            Logger.Error($"[Editor] Scene file not found: {scenePath}");
            return;
        }

        List<GameObject> loadedObjects = SceneLoader.LoadScene(scenePath);
        var newScene = new Scene(scenePath, loadedObjects);
        SceneManager.OpenScene(newScene);
    }

    private void RegisterBuiltInComponents()
    {
        AvailableScriptTypes.Clear();

        Assembly coreAssembly = typeof(Script).Assembly;
        var scriptTypes = coreAssembly.GetTypes()
            .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(Script) && t != typeof(EditorController));

        foreach (var type in scriptTypes)
        {
            RegisterScriptComponent(type);
        }
    }

    private void CompileAndRegisterGameScripts()
    {
        if (_gameAssemblyContext.Assemblies.Any())
        {
            var typesToRemove = AvailableScriptTypes
                .Where(t => AssemblyLoadContext.GetLoadContext(t.Assembly) == _gameAssemblyContext)
                .ToList();

            foreach (var type in typesToRemove)
            {
                _ = AvailableScriptTypes.Remove(type);
            }

            _gameAssemblyContext.Unload();
            Logger.Info("[Editor] Unloaded old game assembly.");
        }
        _gameAssemblyContext = new AssemblyLoadContext("GameScriptsContext", isCollectible: true);

        string scriptsPath = CurrentProject is not null
            ? Path.Combine(CurrentProject.RootPath, "Scripts")
            : "Scripts";

        Assembly? gameAssembly = ScriptCompiler.Compile(scriptsPath, _gameAssemblyContext);

        if (gameAssembly is null)
        {
            Logger.Warning("[Editor] Game script compilation failed. No custom components will be loaded.");
            return;
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
        }
        catch (Exception ex)
        {
            Logger.Error($"[Editor] FATAL: Error processing runtime-compiled assembly. Reason: {ex.Message}");
        }
    }

    private void RegisterScriptComponent(Type scriptType)
    {
        AvailableScriptTypes.Add(scriptType);
        Component Factory(object _)
        {
            return (Component)Activator.CreateInstance(scriptType)!;
        }

        SceneLoader.RegisterComponentFactory(scriptType.Name, Factory);
        Logger.Info($"[Editor] Registered component: {scriptType.Name}");
    }

    private void SetScriptsEnabledForPlayMode()
    {
        foreach (var script in SceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = script is not EditorController;
        }
    }

    private void SetScriptsEnabledForPauseMode()
    {
        foreach (var script in SceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = script is EditorController;
        }
    }

    private void EnsureEditorControllerEnabled(GameObject cameraGo)
    {
        EditorController? editorController = cameraGo.GetComponent<EditorController>();
        editorController ??= cameraGo.AddComponent(new EditorController(this));
        editorController.Start();
        editorController.Enabled = true;
    }

    public Ray CreateRayFromViewport(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        Camera? camera = SceneManager.MainCamera;
        if (camera is null)
        {
            return new Ray();
        }

        Vector2 relativeMouse = mousePos - viewportPos;
        if (relativeMouse.X < 0 || relativeMouse.Y < 0 || relativeMouse.X > viewportSize.X || relativeMouse.Y > viewportSize.Y)
        {
            return new Ray(new Vector3(float.MaxValue), Vector3.Zero);
        }

        float x = (2.0f * relativeMouse.X / viewportSize.X) - 1.0f;
        float y = 1.0f - (2.0f * relativeMouse.Y / viewportSize.Y);
        Vector4 ndc = new(x, y, 1.0f, 1.0f);

        _ = Matrix4x4.Invert(camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y), out var invProjection);
        Vector4 viewRay = Vector4.Transform(ndc, invProjection);
        viewRay.Z = -1.0f;
        viewRay.W = 0.0f;

        _ = Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);
        Vector4 worldRay = Vector4.Transform(viewRay, invView);

        Vector3 rayDir = Vector3.Normalize(new Vector3(worldRay.X, worldRay.Y, worldRay.Z));
        Vector3 rayOrigin = camera.GameObject.Transform.Position;

        return new Ray(rayOrigin, rayDir);
    }

    public void SetSelectedGameObject(GameObject? go)
    {
        SelectedGameObject = go;
    }

    public GameObject? GetSelectedGameObject()
    {
        return SelectedGameObject;
    }
}