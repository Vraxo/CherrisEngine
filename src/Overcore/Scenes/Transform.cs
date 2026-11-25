using Overcore.Core;
using System.Numerics;

namespace Overcore.Components;

/// <summary>
/// An object's place in its parent, Unity's <c>Transform</c>: <see cref="Position"/>,
/// <see cref="Rotation"/> and <see cref="Scale"/> are all local, and the world versions below read through the
/// parent chain.
/// </summary>
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

    /// <summary>The direction the object looks along: its local −Z, in world space.</summary>
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, WorldRotation);

    /// <summary>The object's local +Y, in world space.</summary>
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, WorldRotation);

    /// <summary>The object's local +X, in world space.</summary>
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, WorldRotation);

    /// <summary>How many children the object has.</summary>
    public int ChildCount => Children.Count;

    /// <summary>The topmost transform of the object's chain, which is the object itself when it has no parent.</summary>
    public Transform Root
    {
        get
        {
            Transform root = this;

            while (root.Parent is { } parent)
            {
                root = parent;
            }

            return root;
        }
    }

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

    /// <summary>The object's position in world space.</summary>
    public Vector3 WorldPosition
    {
        get => Vector3.Transform(Vector3.Zero, GetModelMatrix());
        set
        {
            Position = Parent is null
                ? value
                : Vector3.Transform(value, Matrix4x4.Invert(Parent.GetModelMatrix(), out Matrix4x4 inverseParent)
                    ? inverseParent
                    : Matrix4x4.Identity);
        }
    }

    /// <summary>
    /// The object's rotation in world space: every parent's rotation, then its own. Setting it re-expresses the
    /// rotation under the parent, so the object ends up turned the way it was asked for.
    /// </summary>
    public Quaternion WorldRotation
    {
        get => Parent is null ? Rotation : Quaternion.Normalize(Parent.WorldRotation * Rotation);
        set => Rotation = Parent is null
            ? Quaternion.Normalize(value)
            : Quaternion.Normalize(Quaternion.Inverse(Parent.WorldRotation) * value);
    }

    /// <summary>
    /// The object's scale in world space: every scale on the way down multiplied together. A rotated parent
    /// makes that only an approximation, which is the same caveat Unity's own <c>lossyScale</c> carries.
    /// </summary>
    public Vector3 LossyScale => Parent is null ? Scale : Parent.LossyScale * Scale;

    /// <summary>Reads everything the world needs to place the object's geometry: position, rotation and scale.</summary>
    /// <param name="position">Receives the world position.</param>
    /// <param name="rotation">Receives the world rotation.</param>
    /// <param name="scale">Receives the world scale.</param>
    public void GetWorldTransform(out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        position = WorldPosition;
        rotation = WorldRotation;
        scale = LossyScale;
    }

    /// <summary>Moves the object, Unity's <c>Transform.Translate</c>.</summary>
    /// <param name="translation">How far to move, in the axes <paramref name="relativeTo"/> names.</param>
    /// <param name="relativeTo">The object's own axes (the default) or the world's.</param>
    public void Translate(Vector3 translation, Space relativeTo = Space.Self)
    {
        if (relativeTo == Space.World)
        {
            WorldPosition += translation;
        }
        else
        {
            Position += Vector3.Transform(translation, Rotation);
        }
    }

    /// <summary>Turns the object, Unity's <c>Transform.Rotate</c>: the angles are in degrees, around X, then Y, then Z.</summary>
    /// <param name="eulerAngles">Degrees of rotation around each axis.</param>
    /// <param name="relativeTo">The object's own axes (the default) or the world's.</param>
    public void Rotate(Vector3 eulerAngles, Space relativeTo = Space.Self)
    {
        Rotate(Quaternion.CreateFromYawPitchRoll(
            eulerAngles.Y * (MathF.PI / 180.0f),
            eulerAngles.X * (MathF.PI / 180.0f),
            eulerAngles.Z * (MathF.PI / 180.0f)), relativeTo);
    }

    /// <summary>Turns the object by a rotation.</summary>
    /// <param name="rotation">The rotation to apply.</param>
    /// <param name="relativeTo">The object's own axes (the default) or the world's.</param>
    public void Rotate(Quaternion rotation, Space relativeTo = Space.Self)
    {
        if (relativeTo == Space.World)
        {
            Rotation = Quaternion.Normalize(Quaternion.Inverse(Parent?.WorldRotation ?? Quaternion.Identity)
                * rotation
                * WorldRotation);
        }
        else
        {
            Rotation = Quaternion.Normalize(Rotation * rotation);
        }
    }

    /// <summary>Points the object at a place in the world, Unity's <c>Transform.LookAt</c>.</summary>
    /// <param name="worldPosition">The place to look at.</param>
    /// <param name="up">Which way should end up pointing up; the world's +Y by default.</param>
    public void LookAt(Vector3 worldPosition, Vector3 up)
    {
        Vector3 forward = worldPosition - WorldPosition;

        if (forward.LengthSquared() < 1.0e-12f)
        {
            return;
        }

        forward = Vector3.Normalize(forward);
        Vector3 upAxis = up.LengthSquared() < 1.0e-12f ? Vector3.UnitY : Vector3.Normalize(up);

        // The engine looks along −Z, so the basis is built with +Z pointing the other way.
        Quaternion worldRotation = Quaternion.CreateFromRotationMatrix(
            Matrix4x4.CreateWorld(Vector3.Zero, -forward, upAxis));

        Rotation = Parent is null
            ? worldRotation
            : Quaternion.Normalize(Quaternion.Inverse(Parent.WorldRotation) * worldRotation);
    }

    /// <summary>Points the object at a place in the world, keeping the world's +Y up.</summary>
    /// <param name="worldPosition">The place to look at.</param>
    public void LookAt(Vector3 worldPosition) => LookAt(worldPosition, Vector3.UnitY);

    /// <summary>Points the object at another object's place in the world.</summary>
    /// <param name="target">The object to look at.</param>
    public void LookAt(Transform target) => LookAt(target.WorldPosition);

    /// <summary>Points the object at another object's place in the world.</summary>
    /// <param name="target">The object to look at.</param>
    /// <param name="up">Which way should end up pointing up.</param>
    public void LookAt(Transform target, Vector3 up) => LookAt(target.WorldPosition, up);

    /// <summary>
    /// Puts the object under another one, Unity's <c>Transform.SetParent</c>.
    /// </summary>
    /// <param name="newParent">The new parent, or null to leave it loose at the top of the scene.</param>
    /// <param name="worldPositionStays">
    /// True (the default) keeps the object where it is in the world, re-expressing its local position and
    /// rotation under the new parent; false keeps its local values, so it moves with the new parent.
    /// </param>
    public void SetParent(Transform? newParent, bool worldPositionStays = true)
    {
        Vector3 worldPosition = WorldPosition;
        Quaternion worldRotation = WorldRotation;

        Parent = newParent!;

        if (worldPositionStays)
        {
            WorldPosition = worldPosition;

            Rotation = newParent is null
                ? worldRotation
                : Quaternion.Normalize(Quaternion.Inverse(newParent.WorldRotation) * worldRotation);
        }
    }

    /// <summary>The child at an index, in the order children were added.</summary>
    /// <param name="index">The child's index.</param>
    public Transform GetChild(int index) => Children[index];

    /// <summary>Turns a direction in the object's own axes into a direction in the world's.</summary>
    /// <param name="direction">The direction to turn.</param>
    public Vector3 TransformDirection(Vector3 direction) => Vector3.Transform(direction, WorldRotation);

    /// <summary>Turns a place in the object's own axes into a place in the world.</summary>
    /// <param name="position">The local position.</param>
    public Vector3 TransformPoint(Vector3 position) => Vector3.Transform(position, GetModelMatrix());

    /// <summary>Turns a place in the world into a place in the object's own axes.</summary>
    /// <param name="position">The world position.</param>
    public Vector3 InverseTransformPoint(Vector3 position) =>
        Matrix4x4.Invert(GetModelMatrix(), out Matrix4x4 inverse)
            ? Vector3.Transform(position, inverse)
            : position;
}
