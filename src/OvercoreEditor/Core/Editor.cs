using Overcore;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.RenderingInterface;
using Overcore.Serialization;
using OvercoreEditor.Core;
using OvercoreEditor.Undo;

namespace OvercoreEditor;

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
    public bool StartWithNewProject { get; }

    private EditorAppLogic? _editorAppLogic;
    private Camera? _editorCamera;
    private readonly SceneSerializer _sceneSerializer;

    public Editor(GraphicsAPI api, bool startWithNewProject = false) : base("Overcore Editor", false, api)
    {
        StartWithNewProject = startWithNewProject;
        AutoUpdateScene = false; // Editor handles scene updates manually (Edit/Play/Pause)

        Exposure = 0.5f;
        _sceneSerializer = new SceneSerializer();
        ProjectManager = new ProjectManager();
        ScriptManager = new ScriptManager(SceneLoader);

        // Pass dependencies to PlayModeManager
        PlayModeManager = new PlayModeManager(
            SceneManager,
            SceneLoader,
            _sceneSerializer,
            ScriptManager,
            ProjectManager);

        SceneOperations = new SceneOperations(SceneLoader, SceneManager, _sceneSerializer);
        Selection = new EditorSelection(SceneManager);
    }

    public void LoadProject(string projectRoot)
    {
        var previousProject = ProjectManager.CurrentProject;
        ProjectManager.LoadProject(projectRoot);

        // Projects selected after startup need the same script and scene setup as
        // projects loaded from the command line or the last-project setting.
        var project = ProjectManager.CurrentProject;
        if (_editorAppLogic is not null && project is not null && !ReferenceEquals(previousProject, project))
        {
            ScriptManager.CompileAndRegisterGameScripts(project.RootPath);
            LoadInitialSceneIfProjectExists();
        }
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

        // After stopping (restoring snapshot), we need to ensure the editor camera is hooked up again
        SetupSceneForEditing(SceneManager.ActiveScene);
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

        ScriptManager.CreateAndCompileScript(ProjectManager.CurrentProject.RootPath, scriptName);
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
            ScriptManager.CompileAndRegisterGameScripts(ProjectManager.CurrentProject.RootPath);
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
            // Only allow RigidBody in editor (for debug viz); hold everything else back until Play. This is
            // the editor's doing, not the author's: Component.Enabled keeps whatever the inspector's checkbox says.
            script.IsSuspended = script is not RigidBody;
        }
    }

    private Camera? EnsureEditorCamera(Scene scene)
    {
        GameObject? cameraObject = scene.GameObjects
            .FirstOrDefault(g => g.GetComponent<EditorController>() is not null);

        cameraObject ??= scene.GameObjects.FirstOrDefault(go => go.GetComponent<Camera>() is not null);

        if (cameraObject is null)
        {
            return null;
        }

        EditorController? controller = cameraObject.GetComponent<EditorController>();
        controller ??= cameraObject.AddComponent(new EditorController(this));
        controller.Start();
        controller.IsSuspended = false;

        return cameraObject.GetComponent<Camera>();
    }

    private void ConfigureWindowInputHandling()
    {
        if (_backend.GameWindow is not IGameWindow window)
        {
            return;
        }

        window.IsViewportActive = () => IsViewportHovered;
    }

    private void MarkActiveSceneDirty()
    {
        SceneManager.ActiveScene?.IsDirty = true;
    }

    private void RegisterBuiltInComponents()
    {
        ScriptManager.AvailableScriptTypes.Clear();

        IEnumerable<Type> scriptTypes = typeof(Script).Assembly.GetTypes()
            .Where(t =>
            {
                return typeof(Script).IsAssignableFrom(t)
                    && !t.IsAbstract && t != typeof(Script)
                    && t != typeof(EditorController);
            });

        foreach (Type type in scriptTypes)
        {
            ScriptManager.RegisterScriptComponent(type);
        }
    }
}