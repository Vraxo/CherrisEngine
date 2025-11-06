namespace Cherris.Rendering;

public interface IRenderer : IDisposable
{
    void OnWindowResized();
    void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, GameObject selectedObject, float windowWidth, float windowHeight, float exposure);
    void RequestSnapshot(string path);
    void ProcessSnapshot();
}
