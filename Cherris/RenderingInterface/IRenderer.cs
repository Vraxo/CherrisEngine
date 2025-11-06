using Cherris.Components;
using Cherris.Core;

namespace Cherris.Rendering;

public interface IRenderer : IDisposable
{
    void OnWindowResized();
    void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float windowWidth, float windowHeight, float exposure);
    void RequestSnapshot(string path);
    void ProcessSnapshot();
}