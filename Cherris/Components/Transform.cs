using Cherris.Core;
using System.Numerics;

namespace Cherris.Components;

public class Transform
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;
    public GameObject? GameObject { get; } = null;

    public Transform Parent
    {
        get;

        set

        {
            if (field == value)
            {
                return;
            }

            field.Children.Remove(this);
            field = value;
            field.Children.Add(this);
        }
    }

    public readonly List<Transform> Children = new();

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

    public Transform(GameObject gameObject)
    {
        GameObject = gameObject;
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