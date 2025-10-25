using Cherris;
using CherrisEditor.Undo;
using System.Numerics;

namespace CherrisEditor.Undo.Commands;

public class ChangeTransformCommand : ICommand
{
    private readonly Transform _target;

    private readonly Vector3 _oldPosition;
    private readonly Quaternion _oldRotation;
    private readonly Vector3 _oldScale;

    private readonly Vector3 _newPosition;
    private readonly Quaternion _newRotation;
    private readonly Vector3 _newScale;

    public ChangeTransformCommand(Transform target, Vector3 oldPosition, Quaternion oldRotation, Vector3 oldScale, Vector3 newPosition, Quaternion newRotation, Vector3 newScale)
    {
        _target = target;
        _oldPosition = oldPosition;
        _oldRotation = oldRotation;
        _oldScale = oldScale;
        _newPosition = newPosition;
        _newRotation = newRotation;
        _newScale = newScale;
    }

    public void Execute()
    {
        _target.Position = _newPosition;
        _target.Rotation = _newRotation;
        _target.Scale = _newScale;
    }

    public void Undo()
    {
        _target.Position = _oldPosition;
        _target.Rotation = _oldRotation;
        _target.Scale = _oldScale;
    }
}