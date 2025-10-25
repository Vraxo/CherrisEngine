namespace Cherris.Rendering;

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
