namespace CherrisEditorWeb.Host;

/// <summary>
/// Receives input events decoded from the browser client. Implementations run on the client's receive
/// loop (a thread-pool thread), so they must not touch the engine's static input state directly;
/// <see cref="Cherris.Rendering.Headless.HeadlessInput"/> exists for that hand-off.
/// </summary>
public interface IViewportInputSink
{
    /// <summary>The client viewport changed size, in device pixels.</summary>
    void Resize(int width, int height);

    /// <summary>A <c>KeyboardEvent.code</c> was pressed or released.</summary>
    void KeyChanged(string code, bool isDown);

    /// <summary>A <c>MouseEvent.button</c> index was pressed or released.</summary>
    void MouseButtonChanged(int button, bool isDown);

    /// <summary>Relative pointer movement from a pointer-locked mouse.</summary>
    void MouseMoved(float deltaX, float deltaY);

    /// <summary>Absolute pointer position inside the viewport, in device pixels.</summary>
    void MouseMovedTo(float x, float y);

    /// <summary>Wheel movement, in notches.</summary>
    void Wheel(float x, float y);

    /// <summary>Pointer lock was acquired or released.</summary>
    void MouseLockChanged(bool locked);

    /// <summary>The page lost focus or was hidden; held keys and buttons must be cleared.</summary>
    void FocusLost();
}
