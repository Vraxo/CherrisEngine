using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Veldrid.StartupUtilities;
using Veldrid;

namespace Cherris;

public class SceneManager
{
    private readonly List<GameObject> _gameObjects = new();

    public Camera MainCamera { get; private set; }
    public Skybox Skybox { get; private set; }
    public IEnumerable<GameObject> GameObjects => _gameObjects;

    public void AddGameObject(GameObject go)
    {
        _gameObjects.Add(go);
    }

    public void RemoveGameObject(GameObject go)
    {
        // Recursively remove children first to avoid modifying collection during iteration
        foreach (var childTransform in go.Transform.Children.ToList())
        {
            RemoveGameObject(childTransform.GameObject);
        }

        // Remove the object from its parent's list
        go.Transform.Parent = null;

        // Remove from the root scene list
        _gameObjects.Remove(go);

        // Dispose its managed resources
        go.GetComponent<MeshRenderer>()?.Dispose();
    }

    public void SetScene(List<GameObject> gameObjects)
    {
        _gameObjects.Clear();
        _gameObjects.AddRange(gameObjects);
        MainCamera = null;
        Skybox = null;
    }

    public void Start()
    {
        // First pass: find essential components
        foreach (var gameObject in _gameObjects)
        {
            if (MainCamera is null) MainCamera = gameObject.GetComponent<Camera>();
            if (Skybox is null) Skybox = gameObject.GetComponent<Skybox>();
        }

        // Second pass: initialize scripts
        foreach (var gameObject in _gameObjects)
        {
            foreach (var script in gameObject.GetComponents<Script>())
            {
                script.Start();
            }
        }

        // Handle case where no camera was found in the scene
        if (MainCamera is null)
        {
            CreateDefaultCamera();
        }
    }

    private void CreateDefaultCamera()
    {
        Console.WriteLine("Warning: No active camera found in scene. Creating a default one.");
        var go = new GameObject("Default Camera");
        go.Transform.Position = new Vector3(0, 1, 3);
        MainCamera = go.AddComponent(new Camera());
        _gameObjects.Add(go);
    }

    public void Update(float deltaTime)
    {
        foreach (var gameObject in _gameObjects)
        {
            foreach (var script in gameObject.GetComponents<Script>())
            {
                if (script.Enabled)
                {
                    script.Update(deltaTime);
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var gameObject in _gameObjects)
        {
            gameObject.GetComponent<MeshRenderer>()?.Dispose();
        }
    }
}