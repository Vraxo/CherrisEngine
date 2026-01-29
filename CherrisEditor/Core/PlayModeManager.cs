using Cherris;
using Cherris.Components;
using Cherris.Core.Logging;

namespace CherrisEditor.Core;

public class PlayModeManager
{
    private readonly SceneManager _sceneManager;

    public EditorState State { get; private set; } = EditorState.Editing;

    public PlayModeManager(SceneManager sceneManager)
    {
        _sceneManager = sceneManager;
    }

    public void EnterPlayMode(Camera? editorCamera)
    {
        if (State == EditorState.Playing)
        {
            return;
        }

        if (State == EditorState.Editing)
        {
            var gameCamera = _sceneManager.GameObjects
                .Select(g => g.GetComponent<Camera>())
                .FirstOrDefault(c => c is not null && c != editorCamera);

            if (gameCamera is not null)
            {
                _sceneManager.SetMainCamera(gameCamera);
            }
            else
            {
                Logger.Warning("[Editor] No game camera found to switch to for play mode. Using the editor camera.");
            }

            _sceneManager.Start();
        }

        State = EditorState.Playing;
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

    public void Stop()
    {
        State = EditorState.Editing;
    }

    private void SetScriptsEnabledForPlayMode()
    {
        foreach (var script in _sceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = script is not EditorController;
        }
    }

    private void SetScriptsEnabledForPauseMode()
    {
        foreach (var script in _sceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = script is EditorController;
        }
    }
}