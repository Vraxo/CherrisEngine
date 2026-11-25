namespace Overcore.Rendering;

public interface IUIController : IDisposable
{
    void Update(float deltaTime);
    void Render();
}
