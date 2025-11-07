using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.OpenTK;
using Cherris.Serialization;
using CherrisEditor.Undo;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace CherrisEditor;

public class Editor : Engine
{
    private EditorAppLogic? _editorAppLogic;
    public List<Type> AvailableScriptTypes { get; } = new();
    public EditorState State { get; private set; } = EditorState.Editing;
    public bool IsViewportHovered { get; set; }
    public readonly HistoryManager History = new();

    private Camera? _editorCamera;
    private AssemblyLoadContext _gameAssemblyContext;
    private readonly SceneSerializer _sceneSerializer;


    public Editor(GraphicsAPI api) : base("Cherris Editor", false, api)
    {
        Exposure = 0.5f;
        _gameAssemblyContext = new AssemblyLoadContext("GameScriptsContext", isCollectible: true);
        _sceneSerializer = new SceneSerializer();
    }

    public void CreatePrefabFromGameObject(GameObject go, string path)
    {
        if (go is null || string.IsNullOrEmpty(path)) return;
        Console.WriteLine($"[Editor] Creating prefab '{path}' from '{go.Name}'.");
        _sceneSerializer.SavePrefab(go, path);
    }

    public void InstantiatePrefab(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[Editor] Prefab file not found: {path}");
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
        if (State == EditorState.Playing) return;

        if (State == EditorState.Editing)
        {
            // Find any camera in the scene that is NOT the editor's camera.
            var gameCamera = SceneManager.GameObjects
                .Select(g => g.GetComponent<Camera>())
                .FirstOrDefault(c => c is not null && c != _editorCamera);

            if (gameCamera is not null)
            {
                SceneManager.SetMainCamera(gameCamera);
            }
            else
            {
                Console.WriteLine("[Editor] Warning: No game camera found to switch to for play mode. Using the editor camera.");
            }

            // TODO: Snapshot scene state for restoration on Stop.
            SceneManager.Start();
        }

        State = EditorState.Playing;
        SetSelectedGameObject(null);
        SetScriptsEnabledForPlayMode();
    }

    public void EnterPauseMode()
    {
        if (State != EditorState.Playing) return;

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
        if (State == EditorState.Editing) return;

        EnterEditMode();
        EnterPlayMode();
    }

    public void CreateAndCompileScript(string scriptName)
    {
        if (!IsValidCSharpIdentifier(scriptName))
        {
            Console.WriteLine($"[Editor] Error: '{scriptName}' is not a valid C# class name.");
            return;
        }

        string scriptsPath = Path.Combine("Assets", "Scripts");
        Directory.CreateDirectory(scriptsPath);
        string filePath = Path.Combine(scriptsPath, $"{scriptName}.cs");

        if (File.Exists(filePath))
        {
            Console.WriteLine($"[Editor] Error: A script named '{scriptName}.cs' already exists.");
            return;
        }

        string content = ScriptTemplate.GetContent(scriptName);
        File.WriteAllText(filePath, content);
        Console.WriteLine($"[Editor] Created new script at '{filePath}'");

        CompileAndRegisterGameScripts();
    }

    private static bool IsValidCSharpIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !Regex.IsMatch(identifier, @"^[_a-zA-Z][_a-zA-Z0-9]*$"))
        {
            return false;
        }
        // A more robust check would involve comparing against all C# keywords.
        return true;
    }

    protected override void LoadContent()
    {
        ResourceManager.LoadInitialAssets();
        RegisterBuiltInComponents();
        CompileAndRegisterGameScripts();
        LoadSceneFromFile("Assets/Scene.yaml");

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
        OnDrawUI = _editorAppLogic.DrawUI();
        SceneManager.OnActiveSceneChanged += SetupSceneForEditing;
        InitializeEditingState();

        if (_backend.GameWindow is OpenTKGameWindow otkWindow)
        {
            otkWindow.ShouldIgnoreImGuiCapture = () => this.IsViewportHovered;
        }
    }

    protected override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        _editorAppLogic?.UpdateEditorLogic(deltaTime);
        SceneManager.Update(deltaTime, State == EditorState.Playing);
    }

    private void InitializeEditingState()
    {
        SetupSceneForEditing(SceneManager.ActiveScene);
    }

    private void ReloadSceneForEditing()
    {
        var activeScene = SceneManager.ActiveScene;
        if (activeScene is null) return;

        string path = activeScene.FilePath;
        SceneManager.CloseScene(activeScene);
        LoadSceneFromFile(path);

        // The OnActiveSceneChanged event will handle calling SetupSceneForEditing automatically.
    }

    private void SetupSceneForEditing(Scene scene)
    {
        if (scene is null) return;

        History.Clear();

        foreach (var script in scene.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = false;
        }

        GameObject? cameraGo = scene.MainCamera?.GameObject;

        // Fallback if the main camera was deleted or doesn't exist.
        if (cameraGo is null)
        {
            cameraGo = scene.GameObjects.Select(go => go.GetComponent<Camera>()).FirstOrDefault(c => c is not null)?.GameObject;
        }

        if (cameraGo is not null)
        {
            EnsureEditorControllerEnabled(cameraGo);
            _editorCamera = cameraGo.GetComponent<Camera>();
            SceneManager.SetMainCamera(_editorCamera);
        }
        else
        {
            _editorCamera = null;
            Console.WriteLine("[Editor] FATAL: No camera found in scene to attach editor controls to.");
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
            Console.WriteLine($"[Editor] Scene file not found: {scenePath}");
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
        // Unload the previous context if it exists and remove its types from our list
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
            Console.WriteLine("[Editor] Unloaded old game assembly.");
        }
        _gameAssemblyContext = new AssemblyLoadContext("GameScriptsContext", isCollectible: true);

        // Compile and add the new types from the user's scripts
        Assembly? gameAssembly = ScriptCompiler.Compile("Assets", _gameAssemblyContext);

        if (gameAssembly is null)
        {
            Console.WriteLine("[Editor] Game script compilation failed. No custom components will be loaded.");
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
            Console.WriteLine($"[Editor] Loaded {count} custom components from runtime-compiled assembly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] FATAL: Error processing runtime-compiled assembly. Reason: {ex.Message}");
        }
    }

    private void RegisterScriptComponent(Type scriptType)
    {
        AvailableScriptTypes.Add(scriptType);
        Component Factory(object _) => (Component)Activator.CreateInstance(scriptType)!;
        SceneLoader.RegisterComponentFactory(scriptType.Name, Factory);
        Console.WriteLine($"[Editor] Registered component: {scriptType.Name}");
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
        if (editorController is null)
        {
            editorController = cameraGo.AddComponent(new EditorController(this));
            // The crucial fix: Call Start() to initialize the controller's state
            // from the transform that was just loaded from the scene file.
            editorController.Start();
        }
        editorController.Enabled = true;
    }

    public Ray CreateRayFromViewport(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        Camera? camera = SceneManager.MainCamera;
        if (camera is null) return new Ray();

        Vector2 relativeMouse = mousePos - viewportPos;
        if (relativeMouse.X < 0 || relativeMouse.Y < 0 || relativeMouse.X > viewportSize.X || relativeMouse.Y > viewportSize.Y)
        {
            return new Ray(new Vector3(float.MaxValue), Vector3.Zero);
        }

        float x = (2.0f * relativeMouse.X) / viewportSize.X - 1.0f;
        float y = 1.0f - (2.0f * relativeMouse.Y) / viewportSize.Y;
        Vector4 ndc = new(x, y, 1.0f, 1.0f);

        Matrix4x4.Invert(camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y), out var invProjection);
        Vector4 viewRay = Vector4.Transform(ndc, invProjection);
        viewRay.Z = -1.0f;
        viewRay.W = 0.0f;

        Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);
        Vector4 worldRay = Vector4.Transform(viewRay, invView);

        Vector3 rayDir = Vector3.Normalize(new Vector3(worldRay.X, worldRay.Y, worldRay.Z));
        Vector3 rayOrigin = camera.GameObject.Transform.Position;

        return new Ray(rayOrigin, rayDir);
    }

    public void SetSelectedGameObject(GameObject? go) => SelectedGameObject = go;
    public GameObject? GetSelectedGameObject() => SelectedGameObject;
}