using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.OpenTK;
using Cherris.Serialization;
using CherrisEditor.Core;
using CherrisEditor.Undo;
using System.Numerics;
using System.Reflection;

namespace CherrisEditor;

public class Editor : Engine
{
    private EditorAppLogic? _editorAppLogic;
    public readonly HistoryManager History = new();
    public EditorState State { get; private set; } = EditorState.Editing;
    public bool IsViewportHovered { get; set; }

    public ProjectManager ProjectManager { get; }
    public ScriptManager ScriptManager { get; }
    public IReadOnlyList<Type> AvailableScriptTypes => ScriptManager.AvailableScriptTypes;

    private Camera? _editorCamera;
    private readonly SceneSerializer _sceneSerializer;

    public Editor(GraphicsAPI api) : base("Cherris Editor", false, api)
    {
        Exposure = 0.5f;
        _sceneSerializer = new SceneSerializer();
        ProjectManager = new ProjectManager();
        ScriptManager = new ScriptManager(SceneLoader);
    }

    public void LoadProject(string projectRoot)
    {
        ProjectManager.LoadProject(projectRoot);
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
        if (ProjectManager.CurrentProject is null)
        {
            Logger.Error("[Editor] No project loaded.");
            return;
        }

        string scriptsPath = Path.Combine(ProjectManager.CurrentProject.RootPath, "Scripts");
        ScriptManager.CreateAndCompileScript(scriptsPath, scriptName);
    }

    protected override void LoadContent()
    {
        ResourceManager.LoadInitialAssets();
        RegisterBuiltInComponents();

        if (ProjectManager.CurrentProject is not null)
        {
            string scriptsPath = Path.Combine(ProjectManager.CurrentProject.RootPath, "Scripts");
            ScriptManager.CompileAndRegisterGameScripts(scriptsPath);
        }

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

        if (ProjectManager.CurrentProject is not null)
        {
            string startScenePath = Path.Combine(ProjectManager.CurrentProject.RootPath, ProjectManager.CurrentProject.StartScene);
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
        ScriptManager.AvailableScriptTypes.Clear();

        Assembly coreAssembly = typeof(Script).Assembly;
        var scriptTypes = coreAssembly.GetTypes()
            .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(Script) && t != typeof(EditorController));

        foreach (var type in scriptTypes)
        {
            ScriptManager.RegisterScriptComponent(type);
        }
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