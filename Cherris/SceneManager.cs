using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

    public void SetScene(List<GameObject> gameObjects)
    {
        _gameObjects.Clear();
        _gameObjects.AddRange(gameObjects);
        MainCamera = null;
        Skybox = null;
    }

    public void Start()
    {
        foreach (var gameObject in _gameObjects)
        {
            if (MainCamera is null)
            {
                MainCamera = gameObject.GetComponent<Camera>();
            }

            if (Skybox is null)
            {
                Skybox = gameObject.GetComponent<Skybox>();
            }

            foreach (var script in gameObject.GetComponents<Script>())
            {
                script.Start();
            }
        }

        if (MainCamera is null)
        {
            Console.WriteLine("Warning: No camera found in scene. Creating a default one.");
            var go = new GameObject("Default Camera");
            go.Transform.Position = new Vector3(0, 1, 3);
            MainCamera = go.AddComponent(new Camera());
            _gameObjects.Add(go);
        }
    }

    public void Update(float deltaTime)
    {
        foreach (var gameObject in _gameObjects)
        {
            foreach (var script in gameObject.GetComponents<Script>())
            {
                script.Update(deltaTime);
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