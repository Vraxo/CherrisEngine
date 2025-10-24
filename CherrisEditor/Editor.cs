using Cherris;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CherrisEditor;

public class Editor : Engine
{
    private EditorAppLogic? _editorAppLogic;
    public List<Type> AvailableScriptTypes { get; } = new();
    public EditorState State { get; private set; } = EditorState.Editing;

    public Editor(GraphicsAPI api) : base("Cherris Editor", false, api)
    {
        Exposure = 0.5f;
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
        LoadGameComponents();
        LoadScene();
    }

    private void LoadGameComponents()
    {
        // Instead of loading a pre-compiled DLL, we now compile .cs files at runtime.
        var gameAssembly = ScriptCompiler.Compile("Assets");
        AvailableScriptTypes.Clear();

        if (gameAssembly is null)
        {
            Console.WriteLine("[Editor] Game script compilation failed. No custom components will be loaded.");
            return;
        }

        try
        {
            var scriptTypes = gameAssembly.GetTypes()
                .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract);

            int count = 0;
            foreach (var type in scriptTypes)
            {
                AvailableScriptTypes.Add(type);
                // The factory function creates a new instance of the script type.
                Func<object, Component> factory = _ => (Component)Activator.CreateInstance(type);
                SceneLoader.RegisterComponentFactory(type.Name, factory);
                Console.WriteLine($"[Editor] Registered custom component: {type.Name}");
                count++;
            }
            Console.WriteLine($"[Editor] Loaded {count} custom components from runtime-compiled assembly.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] FATAL: Error processing runtime-compiled assembly. Reason: {ex.Message}");
        }
    }

    public void LoadScene()
    {
        string scenePath = "Assets/Scene.yaml";
        List<GameObject> loadedObjects = SceneLoader.LoadScene(scenePath);
        SceneManager.SetScene(loadedObjects);
        CurrentScenePath = scenePath;
    }

    protected override void OnStart()
    {
        _editorAppLogic = new(this);
        OnDrawUI = _editorAppLogic.DrawUI();

        // Set initial state to Editing, which disables all game scripts
        // and sets up the editor camera correctly.
        SetEditingState();
    }

    public void Play()
    {
        if (State == EditorState.Playing) return;

        // If starting from scratch, call Start() on scripts.
        if (State == EditorState.Editing)
        {
            // TODO: Snapshot scene state for restoration on Stop.
            SceneManager.Start();
        }

        State = EditorState.Playing;
        SelectedGameObject = null; // Deselect object when entering play mode.

        // Enable game scripts, disable editor script
        foreach (var script in SceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = script is not EditorController;
        }
    }

    public void Pause()
    {
        if (State != EditorState.Playing) return;
        State = EditorState.Paused;

        // Disable game scripts, enable editor script for camera movement
        foreach (var script in SceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = script is EditorController;
        }
    }

    public void Stop()
    {
        State = EditorState.Editing;
        // Reload the scene to revert any changes made during play mode
        LoadScene();
        SceneManager.Start(); // This is for components that need Start(), not scripts
        SetEditingState();
    }

    public void Restart()
    {
        if (State == EditorState.Editing) return;
        Stop();
        Play();
    }

    private void SetEditingState()
    {
        // Disable all game scripts
        foreach (var script in SceneManager.GameObjects.SelectMany(g => g.GetComponents<Script>()))
        {
            script.Enabled = false;
        }

        // Find the camera and enable its EditorController
        var cameraGo = SceneManager.MainCamera?.GameObject;
        if (cameraGo != null)
        {
            var editorController = cameraGo.GetComponent<EditorController>();
            if (editorController == null)
            {
                editorController = cameraGo.AddComponent(new EditorController());
            }
            editorController.Enabled = true;
        }
    }

    protected override void Update(float deltaTime)
    {
        _editorAppLogic?.UpdateEditorLogic(deltaTime);
        SceneManager.Update(deltaTime);

        base.Update(deltaTime);
    }
}