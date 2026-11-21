using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using System.Reflection;

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

        PropertyInfo? property = target.GetType().GetProperty(propertyName);

        if (property == null || !property.CanWrite)
        {
            return;
        }

        if (activated)
        {
            object? value = property.GetValue(target);
            _stack.Push(value ?? new object());
        }

        if (!deactivated || _stack.Count <= 0)
        {
            return;
        }

        object initial = _stack.Pop();
        object? currentValue = property.GetValue(target);

        if (Equals(initial, currentValue))
        {
            return;
        }

        property.SetValue(target, initial);
        _history.Execute(new ChangePropertyCommand(target, property, initial, currentValue ?? new object()));
    }

    public void Reset()
    {
        _stack.Clear();
    }
}