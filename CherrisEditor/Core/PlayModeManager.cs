using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Serialization;

namespace CherrisEditor.Core;

public class PlayModeManager
{
    private readonly SceneManager _sceneManager;
    private readonly SceneLoader _sceneLoader;
    private readonly SceneSerializer _sceneSerializer;
    private readonly ScriptManager _scriptManager;
    private readonly ProjectManager _projectManager;

    // Stores the YAML state of the scene before entering Play Mode.
    private string? _editModeSceneSnapshot;
    private string _activeScenePath = "";

    public EditorState State { get; private set; } = EditorState.Editing;

    public PlayModeManager(
        SceneManager sceneManager,
        SceneLoader sceneLoader,
        SceneSerializer sceneSerializer,
        ScriptManager scriptManager,
        ProjectManager projectManager)
    {
        _sceneManager = sceneManager;
        _sceneLoader = sceneLoader;
        _sceneSerializer = sceneSerializer;
        _scriptManager = scriptManager;
        _projectManager = projectManager;
    }

    public void EnterPlayMode(Camera? editorCamera)
    {
        if (State == EditorState.Playing)
        {
            return;
        }

        if (State == EditorState.Editing)
        {
            Scene? activeScene = _sceneManager.ActiveScene;
            if (activeScene is null)
            {
                Logger.Warning("[Editor] No active scene to play.");
                return;
            }

            // 1. Snapshot the current scene state (in-memory save)
            Logger.Info("[PlayMode] Snapshotting scene state...");
            _editModeSceneSnapshot = _sceneSerializer.SerializeSceneToString(activeScene.GameObjects);
            _activeScenePath = activeScene.FilePath;

            // 2. Compile Scripts (Hot-Reload)
            // We compile AFTER snapshotting but BEFORE reloading.
            // This ensures we save the data of the old types, but instantiate the new types.
            if (_projectManager.CurrentProject != null)
            {
                Logger.Info("[PlayMode] Compiling scripts...");
                bool success = _scriptManager.CompileAndRegisterGameScripts(_projectManager.CurrentProject.RootPath);
                if (!success)
                {
                    Logger.Error("[PlayMode] Compilation failed. Aborting Play Mode.");
                    _editModeSceneSnapshot = null; // Discard snapshot
                    return;
                }
            }

            // 3. Hot-reload the scene from the snapshot using NEW script types.
            ReloadSceneFromSnapshot(_editModeSceneSnapshot, _activeScenePath);

            // 4. Find/Set Main Camera for Game
            var gameCamera = _sceneManager.ActiveScene.GameObjects
                .Select(g => g.GetComponent<Camera>())
                .FirstOrDefault(c => c is not null && c != editorCamera);

            if (gameCamera is not null)
            {
                _sceneManager.SetMainCamera(gameCamera);
            }
            else
            {
                Logger.Warning("[Editor] No game camera found. Using default/editor camera.");
            }

            // 5. Start the Scene (Calls Start() on all scripts)
            _sceneManager.Start();
        }

        State = EditorState.Playing;
        SetScriptsEnabledForPlayMode();

        Logger.Info("[PlayMode] Game Started.");
    }

    public void EnterPauseMode()
    {
        if (State != EditorState.Playing)
        {
            return;
        }

        State = EditorState.Paused;
        SetScriptsEnabledForPauseMode();
        Logger.Info("[PlayMode] Game Paused.");
    }

    public void Stop()
    {
        if (State == EditorState.Editing)
        {
            return;
        }

        // Restore the scene to the state it was in before Play Mode
        if (!string.IsNullOrEmpty(_editModeSceneSnapshot))
        {
            Logger.Info("[PlayMode] Restoring edit-mode scene state...");
            ReloadSceneFromSnapshot(_editModeSceneSnapshot, _activeScenePath);
            _editModeSceneSnapshot = null;
        }

        State = EditorState.Editing;
        Logger.Info("[PlayMode] Stopped.");
    }

    private void ReloadSceneFromSnapshot(string snapshotYaml, string filePath)
    {
        // Close the current scene (cleans up resources like Physics bodies)
        if (_sceneManager.ActiveScene is not null)
        {
            _sceneManager.CloseScene(_sceneManager.ActiveScene);
        }

        // Load objects from the snapshot string
        List<GameObject> objects = _sceneLoader.LoadSceneFromYaml(snapshotYaml);

        // Create new scene instance
        Scene newScene = new(filePath, objects);
        _sceneManager.OpenScene(newScene);
    }

    private void SetScriptsEnabledForPlayMode()
    {
        int count = 0;
        foreach (var script in _sceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            // Disable EditorController, Enable everything else
            bool isEditorScript = script.GetType().Name == "EditorController";
            script.Enabled = !isEditorScript;

            if (script.Enabled)
            {
                count++;
            }
        }
        Logger.Info($"[PlayMode] Enabled {count} scripts.");
    }

    private void SetScriptsEnabledForPauseMode()
    {
        foreach (var script in _sceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            // In pause mode, we might want to enable the EditorCamera to fly around frozen time
            bool isEditorScript = script.GetType().Name == "EditorController";
            script.Enabled = isEditorScript;
        }
    }
}