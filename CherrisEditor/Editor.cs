using Cherris;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
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

    public Ray CreateRayFromViewport(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        Camera camera = SceneManager.MainCamera;
        if (camera is null) return new Ray();

        Vector2 relativeMouse = mousePos - viewportPos;

        // Check if mouse is inside viewport. If not, return an invalid ray.
        if (relativeMouse.X < 0 || relativeMouse.Y < 0 || relativeMouse.X > viewportSize.X || relativeMouse.Y > viewportSize.Y)
        {
            return new Ray(new Vector3(float.MaxValue), Vector3.Zero);
        }

        // Normalize mouse coordinates to NDC [-1, 1] for X and [1, -1] for Y
        float x = (2.0f * relativeMouse.X) / viewportSize.X - 1.0f;
        float y = 1.0f - (2.0f * relativeMouse.Y) / viewportSize.Y;
        Vector4 ndc = new(x, y, 1.0f, 1.0f);

        // We need the correct projection matrix for the viewport's aspect ratio
        Matrix4x4.Invert(camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y), out var invProjection);

        Vector4 viewRay = Vector4.Transform(ndc, invProjection);
        viewRay.Z = -1.0f;
        viewRay.W = 0.0f;

        Matrix4x4.Invert(camera.GetViewMatrix(), out var invView);
        Vector4 worldRay = Vector4.Transform(viewRay, invView);

        Vector3 rayDir = Vector3.Normalize(new(worldRay.X, worldRay.Y, worldRay.Z));
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
        // Call base first to update ImGui inputs and draw the UI shell.
        base.Update(deltaTime);

        // Now update editor/game logic which might depend on ImGui state from this frame.
        _editorAppLogic?.UpdateEditorLogic(deltaTime);
        SceneManager.Update(deltaTime);
    }
}