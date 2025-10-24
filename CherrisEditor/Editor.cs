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

    private void LoadScene()
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

        SetupEditorCamera();
    }

    private void SetupEditorCamera()
    {
        if (SceneManager.MainCamera?.GameObject is null)
        {
            Console.WriteLine("[Editor] No camera found in scene. Editor controller will not be attached.");
            return;
        }

        GameObject cameraGo = SceneManager.MainCamera.GameObject;

        // Remove any game-specific scripts from the camera to replace them with the editor controller.
        var gameScripts = cameraGo.GetComponents<Script>()
            .Where(s => s.GetType() != typeof(EditorController))
            .ToList(); // Use ToList to create a copy for safe removal.

        foreach (var script in gameScripts)
        {
            cameraGo.RemoveComponent(script);
        }

        // Ensure an EditorController is present.
        if (cameraGo.GetComponent<EditorController>() is null)
        {
            cameraGo.AddComponent(new EditorController());
        }
    }

    protected override void Update(float deltaTime)
    {
        _editorAppLogic?.UpdateEditorLogic(deltaTime);
        SceneManager.Update(deltaTime);

        base.Update(deltaTime);
    }
}