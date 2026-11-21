using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Serialization;
using CherrisEditor.Build;

namespace CherrisEditor.Core;

public class SceneOperations
{
    private readonly SceneLoader _sceneLoader;
    private readonly SceneManager _sceneManager;
    private readonly SceneSerializer _sceneSerializer;

    public SceneOperations(SceneLoader sceneLoader, SceneManager sceneManager, SceneSerializer sceneSerializer)
    {
        _sceneLoader = sceneLoader;
        _sceneManager = sceneManager;
        _sceneSerializer = sceneSerializer;
    }

    public void LoadSceneFromFile(string scenePath)
    {
        Scene? existingScene = _sceneManager.OpenScenes.FirstOrDefault(s => s.FilePath == scenePath);

        if (existingScene is not null)
        {
            _sceneManager.SetActiveScene(existingScene);
            return;
        }

        if (!File.Exists(scenePath))
        {
            Logger.Error($"[Editor] Scene file not found: {scenePath}");
            return;
        }

        List<GameObject> loadedObjects = _sceneLoader.LoadScene(scenePath);
        var newScene = new Scene(scenePath, loadedObjects);
        _sceneManager.OpenScene(newScene);
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

        List<GameObject> rootObjects = _sceneLoader.LoadPrefab(path);

        foreach (var root in rootObjects)
        {
            _sceneManager.AddGameObject(root);
        }

        _sceneManager.ActiveScene.IsDirty = true;
    }

    public void ReloadSceneForEditing(Scene? activeScene)
    {
        if (activeScene is null)
        {
            return;
        }

        string path = activeScene.FilePath;
        _sceneManager.CloseScene(activeScene);
        LoadSceneFromFile(path);
    }
}