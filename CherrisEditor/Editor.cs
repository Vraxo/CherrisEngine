using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Rendering;
using Cherris.Serialization;
using CherrisEditor.Core;
using CherrisEditor.Undo;
using System.Numerics;
using System.Reflection;

namespace CherrisEditor;

public class Editor : Engine
{
    public readonly HistoryManager History = new();
    public readonly ProjectManager ProjectManager;
    public readonly ScriptManager ScriptManager;
    public readonly PlayModeManager PlayModeManager;
    public EditorState State => PlayModeManager.State;
    public IReadOnlyList<Type> AvailableScriptTypes => ScriptManager.AvailableScriptTypes;
    public bool IsViewportHovered { get; set; }

    private EditorAppLogic? _editorAppLogic;
    private Camera? _editorCamera;
    private readonly SceneSerializer _sceneSerializer;

    public Editor(GraphicsAPI api) : base("Cherris Editor", false, api)
    {
        Exposure = 0.5f;
        _sceneSerializer = new SceneSerializer();
        ProjectManager = new ProjectManager();
        ScriptManager = new ScriptManager(SceneLoader);
        PlayModeManager = new PlayModeManager(SceneManager);
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

        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
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

        List<GameObject> rootObjects = SceneLoader.LoadPrefab(path);

        foreach (var root in rootObjects)
        {
            SceneManager.AddGameObject(root);
        }

        SceneManager.ActiveScene.IsDirty = true;
    }

    public void EnterPlayMode()
    {
        PlayModeManager.EnterPlayMode(_editorCamera);
        SetSelectedGameObject(null);
    }

    public void EnterPauseMode()
    {
        PlayModeManager.EnterPauseMode();
    }

    public void EnterEditMode()
    {
        PlayModeManager.Stop();
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

    public void LoadSceneFromFile(string scenePath)
    {
        Scene? existingScene = SceneManager.OpenScenes.FirstOrDefault(s => s.FilePath == scenePath);

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

    public Ray CreateRayFromViewport(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        Camera? camera = SceneManager.MainCamera;
        if (camera is null || viewportSize.X <= 0 || viewportSize.Y <= 0)
        {
            return new Ray(new Vector3(float.MaxValue), Vector3.Zero);
        }

        Vector2 relativeMouse = mousePos - viewportPos;

        bool isOutsideViewport = relativeMouse.X < 0
            || relativeMouse.Y < 0
            || relativeMouse.X > viewportSize.X
            || relativeMouse.Y > viewportSize.Y;

        return isOutsideViewport
            ? new Ray(new Vector3(float.MaxValue), Vector3.Zero)
            : ViewportToWorldRay(relativeMouse, viewportSize, camera);
    }

    public void SetSelectedGameObject(GameObject? go)
    {
        SelectedGameObject = go;
    }

    public GameObject? GetSelectedGameObject()
    {
        return SelectedGameObject;
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

        History.OnHistoryChanged += MarkActiveSceneDirtyIfPresent;
    }

    protected override void OnStart()
    {
        _editorAppLogic = new EditorAppLogic(this, _sceneSerializer);
        OnDrawUI = _editorAppLogic.Draw;
        SceneManager.OnActiveSceneChanged += SetupSceneForEditing;

        LoadInitialSceneIfProjectExists();
        ConfigureWindowInputHandling();

        StartEditorLogic();
    }

    protected override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        _editorAppLogic?.UpdateEditorLogic(deltaTime);

        bool shouldStepPhysics = State == EditorState.Playing;
        SceneManager.Update(deltaTime, shouldStepPhysics);
    }

    private Ray ViewportToWorldRay(Vector2 viewportMousePos, Vector2 viewportSize, Camera camera)
    {
        float normalizedX = (2.0f * viewportMousePos.X / viewportSize.X) - 1.0f;
        float normalizedY = 1.0f - (2.0f * viewportMousePos.Y / viewportSize.Y);
        Vector4 clipSpaceRay = new(normalizedX, normalizedY, 1.0f, 1.0f);

        Matrix4x4 projectionMatrix = camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y);
        Matrix4x4.Invert(projectionMatrix, out Matrix4x4 inverseProjection);
        Vector4 viewSpaceRay = Vector4.Transform(clipSpaceRay, inverseProjection);
        viewSpaceRay.Z = -1.0f;
        viewSpaceRay.W = 0.0f;

        Matrix4x4 viewMatrix = camera.GetViewMatrix();
        Matrix4x4.Invert(viewMatrix, out Matrix4x4 inverseView);
        Vector4 worldSpaceRay = Vector4.Transform(viewSpaceRay, inverseView);

        Vector3 rayDirection = Vector3.Normalize(new Vector3(worldSpaceRay.X, worldSpaceRay.Y, worldSpaceRay.Z));
        Vector3 rayOrigin = camera.GameObject.Transform.Position;

        return new Ray(rayOrigin, rayDirection);
    }

    private void SetupSceneForEditing(Scene scene)
    {
        if (scene is null)
        {
            return;
        }

        ResetEditorState();
        PrepareScriptsForEditing(scene);
        _editorCamera = EnsureEditorCamera(scene);

        if (_editorCamera is null)
        {
            Logger.Error("[Editor] FATAL: No camera found in scene to attach editor controls to.");
            return;
        }

        SceneManager.SetMainCamera(_editorCamera);
    }

    private void ResetEditorState()
    {
        History.Clear();
    }

    private static void PrepareScriptsForEditing(Scene scene)
    {
        foreach (Script? script in scene.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = script is RigidBody;
        }
    }

    private Camera? EnsureEditorCamera(Scene scene)
    {
        GameObject? cameraObject = scene.MainCamera?.GameObject;

        cameraObject ??= scene.GameObjects
                .Select(go => go.GetComponent<Camera>())
                .FirstOrDefault(c => c is not null)?.GameObject;

        if (cameraObject is null)
        {
            return null;
        }

        EditorController? controller = cameraObject.GetComponent<EditorController>();
        controller ??= cameraObject.AddComponent(new EditorController(this));

        controller.Start();
        controller.Enabled = true;

        return cameraObject.GetComponent<Camera>();
    }

    private void ReloadSceneForEditing()
    {
        Scene? activeScene = SceneManager.ActiveScene;

        if (activeScene is null)
        {
            return;
        }

        string path = activeScene.FilePath;
        SceneManager.CloseScene(activeScene);
        LoadSceneFromFile(path);
    }

    private void LoadInitialSceneIfProjectExists()
    {
        if (ProjectManager.CurrentProject is null)
        {
            return;
        }

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

    private void ConfigureWindowInputHandling()
    {
        if (_backend.GameWindow is not IGameWindow window || window.ShouldIgnoreImGuiCapture is null)
        {
            return;
        }

        window.ShouldIgnoreImGuiCapture = () => IsViewportHovered;
    }

    private void StartEditorLogic()
    {
        // Left empty for future editor startup logic
    }

    private void MarkActiveSceneDirtyIfPresent()
    {
        if (SceneManager.ActiveScene is null)
        {
            return;
        }

        SceneManager.ActiveScene.IsDirty = true;
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
}