namespace OvercoreEditorPhotino.Services;

/// <summary>Lifecycle of the editor's render session.</summary>
public enum EditorSessionState
{
    /// <summary>No session: the launcher is showing.</summary>
    Idle,

    /// <summary>A session is being prepared (graphics context, listener, render thread).</summary>
    Starting,

    /// <summary>The render thread is running.</summary>
    Running,

    /// <summary>The last attempt failed; <see cref="EditorHostService.FailureMessage"/> says why.</summary>
    Failed
}
