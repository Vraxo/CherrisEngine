namespace OvercoreEditorPhotino.Services;

/// <summary>
/// The object that is selected, shared by the hierarchy, the inspector and the viewport.
/// </summary>
/// <remarks>
/// Two threads select: the UI thread when a hierarchy row is clicked, and the render thread when the viewport
/// picks what is under the pointer. The id is therefore read and written under a lock, and <see cref="Changed"/>
/// is still raised on whichever thread did the selecting — panels marshal with <c>InvokeAsync</c>, as they do for
/// the scene store.
/// </remarks>
public sealed class SceneSelection
{
    private readonly object _gate = new();

    private Guid? _selectedId;

    /// <summary>Raised on whichever thread called <see cref="Select"/>.</summary>
    public event Action? Changed;

    /// <summary>Selected object id, or null when nothing is selected.</summary>
    public Guid? SelectedId
    {
        get
        {
            lock (_gate)
            {
                return _selectedId;
            }
        }
    }

    /// <summary>Selects an object, or clears the selection when passed null.</summary>
    public void Select(Guid? id)
    {
        lock (_gate)
        {
            if (_selectedId == id)
            {
                return;
            }

            _selectedId = id;
        }

        Changed?.Invoke();
    }
}
