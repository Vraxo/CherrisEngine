namespace OvercoreEditorSession.Host;

/// <summary>
/// Receives input events decoded from the browser client. Implementations run on the client's receive
/// loop (a thread-pool thread), so they must not touch the engine's static input state directly;
/// <see cref="Overcore.Rendering.Headless.HeadlessInput"/> exists for that hand-off.
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

    /// <summary>
    /// A look drag started or ended, as reported by the button that drives it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="MouseButtonChanged"/> on purpose: the camera follows this, so the drag keeps working
    /// when the button state does not survive in the engine's own input — a pointer capture that moves the focus, or
    /// a focus loss that clears held buttons mid-drag, would otherwise stop the camera dead while the browser is
    /// still sending movement.
    /// </remarks>
    void LookChanged(bool isDown);

    /// <summary>The page lost focus or was hidden; held keys and buttons must be cleared.</summary>
    void FocusLost();

    /// <summary>
    /// A line from the page for the editor's log.
    /// </summary>
    /// <remarks>
    /// The page cannot report anything the host can read — a webview's console is invisible to the app — so input or
    /// drawing problems used to be undebuggable from the outside: a click the canvas never received looked exactly
    /// like a click the engine ignored.
    /// </remarks>
    void PageLog(string text);
}
