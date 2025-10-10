using System;
using System.Collections.Generic;
using System.Numerics;
using Cherris;

namespace Apexverse;

public class Spinner : Script
{
    private float _totalTime;

    public override void Update(float deltaTime)
    {
        _totalTime += deltaTime;
        GameObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _totalTime) *
                                           Quaternion.CreateFromAxisAngle(Vector3.UnitX, _totalTime * 0.7f);
    }
}

public class Game : Engine
{
    public Game() : base("Veldrid Engine Demo")
    {
    }

    protected override void LoadContent()
    {
        // Pre-load assets that the scene will need
        ResourceManager.LoadInitialAssets();

        // Register custom components that the scene can use
        SceneLoader.RegisterComponentFactory("Spinner", _ => new Spinner());

        // Load the scene from the file
        var loadedObjects = SceneLoader.LoadScene("Assets/Scene.yaml");
        Scene.AddRange(loadedObjects);
    }

    protected override void Update(float deltaTime)
    {
        // The base engine Update now handles calling Update on all Scripts
        base.Update(deltaTime);
    }
}