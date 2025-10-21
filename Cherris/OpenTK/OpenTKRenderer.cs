using System;
using System.Collections.Generic;
using Cherris.Rendering;
using OpenTK.Graphics.OpenGL;

namespace Cherris;

public class OpenTKRenderer : IRenderer
{
    public OpenTKRenderer()
    {
        // Set a default clear color
        GL.ClearColor(0.4f, 0.6f, 0.9f, 1.0f); // Cornflower Blue
    }

    public void OnWindowResized()
    {
        // Viewport is handled by OpenTKGameWindow
    }

    public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
    {
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // --- Rendering logic will be added here in future steps ---
    }

    public void RequestSnapshot(string path)
    {
        Console.WriteLine("[OpenTKRenderer] Snapshots not yet implemented.");
    }

    public void ProcessSnapshot()
    {
        // Not implemented
    }

    public void Dispose()
    {
        // Nothing to dispose yet
    }
}