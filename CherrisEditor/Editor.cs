using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Rendering;
using Cherris.Serialization;
using CherrisEditor.Core;
using CherrisEditor.Undo;

namespace CherrisEditor;

public class Editor : Engine
{
    public readonly HistoryManager History = new();
    public readonly ProjectManager ProjectManager;
    public readonly ScriptManager ScriptManager;
    public readonly PlayModeManager PlayModeManager;
    public readonly SceneOperations SceneOperations;
    public readonly EditorSelection Selection;

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
        SceneOperations = new SceneOperations(SceneLoader, SceneManager, _sceneSerializer);
        Selection = new EditorSelection(SceneManager);
    }

    public void LoadProject(string projectRoot)
    {
        ProjectManager.LoadProject(projectRoot);
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
        SceneOperations.ReloadSceneForEditing(SceneManager.ActiveScene);
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

        History.OnHistoryChanged += MarkActiveSceneDirty;
    }

    protected override void OnStart()
    {
        _editorAppLogic = new EditorAppLogic(this, _sceneSerializer);
        OnDrawUI = _editorAppLogic.Draw;
        SceneManager.OnActiveSceneChanged += SetupSceneForEditing;

        LoadInitialSceneIfProjectExists();
        ConfigureWindowInputHandling();
    }

    protected override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        _editorAppLogic?.UpdateEditorLogic(deltaTime);

        bool shouldStepPhysics = State == EditorState.Playing;
        SceneManager.Update(deltaTime, shouldStepPhysics);
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
            SceneOperations.LoadSceneFromFile(startScenePath);
        }
        else
        {
            Logger.Error($"[Editor] Start scene not found: {startScenePath}");
        }
    }

    private void SetupSceneForEditing(Scene scene)
    {
        if (scene is null)
        {
            return;
        }

        History.Clear();
        PrepareScriptsForEditing(scene);
        _editorCamera = EnsureEditorCamera(scene);

        if (_editorCamera is null)
        {
            Logger.Error("[Editor] FATAL: No camera found in scene to attach editor controls to.");
            return;
        }

        SceneManager.SetMainCamera(_editorCamera);
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
        GameObject? cameraObject = scene.MainCamera?.GameObject
            ?? scene.GameObjects.Select(go => go.GetComponent<Camera>()).FirstOrDefault(c => c is not null)?.GameObject;

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

    private void ConfigureWindowInputHandling()
    {
        if (_backend.GameWindow is not IGameWindow window || window.ShouldIgnoreImGuiCapture is null)
        {
            return;
        }

        window.ShouldIgnoreImGuiCapture = () => IsViewportHovered;
    }

    private void MarkActiveSceneDirty()
    {
        if (SceneManager.ActiveScene is not null)
        {
            SceneManager.ActiveScene.IsDirty = true;
        }
    }

    private void RegisterBuiltInComponents()
    {
        ScriptManager.AvailableScriptTypes.Clear();

        var scriptTypes = typeof(Script).Assembly.GetTypes()
            .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(Script) && t != typeof(EditorController));

        foreach (var type in scriptTypes)
        {
            ScriptManager.RegisterScriptComponent(type);
        }
    }
}