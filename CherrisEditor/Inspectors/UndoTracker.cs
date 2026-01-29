using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;

namespace CherrisEditor.Inspectors;

public sealed class UndoTracker
{
    private readonly HistoryManager _history;
    private readonly Stack<object> _stack = new();

    public UndoTracker(HistoryManager history)
    {
        _history = history;
    }

    public void Track(object target, string propertyName, bool activated, bool deactivated)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return;
        }

        var property = target.GetType().GetProperty(propertyName);
        if (property == null || !property.CanWrite)
        {
            return;
        }

        if (activated)
        {
            var value = property.GetValue(target);
            _stack.Push(value ?? new object());
        }

        if (deactivated && _stack.Count > 0)
        {
            var initial = _stack.Pop();
            var currentValue = property.GetValue(target);

            if (!Equals(initial, currentValue))
            {
                property.SetValue(target, initial);
                _history.Execute(new ChangePropertyCommand(target, property, initial, currentValue ?? new object()));
            }
        }
    }

    public void Reset()
    {
        _stack.Clear();
    }
}