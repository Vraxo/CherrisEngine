namespace CherrisEditorPhotino.Services;

/// <summary>
/// The object the hierarchy selected, shared by the hierarchy and inspector panels.
/// </summary>
public sealed class SceneSelection
{
    private Guid? _selectedId;

    /// <summary>Raised on whichever thread called <see cref="Select"/>.</summary>
    public event Action? Changed;

    /// <summary>Selected object id, or null when nothing is selected.</summary>
    public Guid? SelectedId => _selectedId;

    /// <summary>Selects an object, or clears the selection when passed null.</summary>
    public void Select(Guid? id)
    {
        if (_selectedId == id)
        {
            return;
        }

        _selectedId = id;
        Changed?.Invoke();
    }
}
