using System.Reflection;

namespace CherrisEditor.Undo.Commands;

public class ChangePropertyCommand : ICommand
{
    private readonly object _target;
    private readonly PropertyInfo _property;
    private readonly object _oldValue;
    private readonly object _newValue;

    public ChangePropertyCommand(object target, PropertyInfo property, object oldValue, object newValue)
    {
        _target = target;
        _property = property;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public void Execute()
    {
        _property.SetValue(_target, _newValue);
    }

    public void Undo()
    {
        _property.SetValue(_target, _oldValue);
    }
}