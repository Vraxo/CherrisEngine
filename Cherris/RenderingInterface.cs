using System;
using System.Collections.Generic;
using System.Numerics;
using Veldrid; // We still need this for types like BoundingBox if we don't redefine everything

namespace Cherris.Rendering
{
    public interface ITexture : IDisposable
    {
        object GetBackendHandle();
    }

    public interface IGameWindow : IDisposable
    {
        bool Exists { get; }
        float Width { get; }
        float Height { get; }
        bool IsMouseLocked { get; set; }
        void ProcessEvents();
        void SwapBuffers();
        event Action Resized;
    }

    public interface IResourceManager : IDisposable
    {
        void LoadInitialAssets();
        Mesh GetMesh(string name);
        ITexture GetTexture(string name);
        Skybox GetSkybox(string name);
    }

    public interface IRenderer : IDisposable
    {
        void OnWindowResized();
        void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, GameObject selectedObject, float windowWidth, float windowHeight, float exposure);
        void RequestSnapshot(string path);
        void ProcessSnapshot();
    }

    public interface IGraphicsBackend : IDisposable
    {
        IGameWindow GameWindow { get; }
        IRenderer Renderer { get; }
        IResourceManager ResourceManager { get; }

        void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked);
    }
}