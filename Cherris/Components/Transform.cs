using Cherris.Core;
using System.Numerics;

namespace Cherris.Components;

public class Transform
{
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Scale { get; set; }
    public GameObject GameObject { get; }

    private Transform _parent;
    public Transform Parent
    {
        get => _parent;
        set
        {
            if (_parent == value) return;

            _parent?.Children.Remove(this);
            _parent = value;
            _parent?.Children.Add(this);
        }
    }

    public readonly List<Transform> Children = new();

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

    public Transform(GameObject gameObject)
    {
        GameObject = gameObject;
        Position = Vector3.Zero;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
    }

    public Matrix4x4 GetModelMatrix()
    {
        Matrix4x4 localMatrix = Matrix4x4.CreateScale(Scale) *
                                Matrix4x4.CreateFromQuaternion(Rotation) *
                                Matrix4x4.CreateTranslation(Position);

        if (Parent is not null)
        {
            return localMatrix * Parent.GetModelMatrix();
        }

        return localMatrix;
    }
}