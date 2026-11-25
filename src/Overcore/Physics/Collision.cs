using Overcore.Core;
using System.Numerics;

namespace Overcore.Components;

/// <summary>
/// One place two colliders meet, Unity's <c>ContactPoint</c>: the spot, the surface direction, and how far the
/// shapes had sunk into each other there.
/// </summary>
public readonly struct ContactPoint
{
    /// <summary>The collider the collision was reported on — the one whose script was called.</summary>
    public Collider ThisCollider { get; init; }

    /// <summary>The collider that was hit.</summary>
    public Collider OtherCollider { get; init; }

    /// <summary>Where the two touch, in world space.</summary>
    public Vector3 Point { get; init; }

    /// <summary>The surface normal at <see cref="Point"/>, pointing away from <see cref="OtherCollider"/>.</summary>
    public Vector3 Normal { get; init; }

    /// <summary>How far the shapes overlap at <see cref="Point"/>: zero when they merely touch.</summary>
    public float Separation { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"contact at {Point} (normal {Normal}, depth {Separation:0.###})";
}

/// <summary>
/// What <see cref="Script.OnCollisionEnter(Collision)"/> is handed, Unity's <c>Collision</c>: the collider that was
/// run into, the points where the shapes met, and how fast they were closing.
/// </summary>
/// <remarks>
/// A collision is reported to both objects, each with its own copy: <see cref="Collider"/> is always the
/// <em>other</em> collider, and each contact's normal points away from it, so a script can push itself out along
/// <c>Contacts[0].Normal</c> whichever side of the impact it is on.
/// </remarks>
public sealed class Collision
{
    /// <summary>The collider that was run into.</summary>
    public Collider Collider { get; }

    /// <summary>The contact points, at most four, in the order the physics solver found them.</summary>
    public IReadOnlyList<ContactPoint> Contacts { get; }

    /// <summary>How fast the two shapes were closing, in world units per second.</summary>
    public Vector3 RelativeVelocity { get; }

    /// <summary>How many contact points there are: at most four.</summary>
    public int ContactCount => Contacts.Count;

    /// <summary>The object the other collider is on.</summary>
    public GameObject GameObject => Collider.GameObject
        ?? throw new InvalidOperationException("The collider this collision reports is not attached to a GameObject.");

    /// <summary>The transform of the object the other collider is on.</summary>
    public Transform Transform => Collider.Transform;

    internal Collision(Collider collider, IReadOnlyList<ContactPoint> contacts, Vector3 relativeVelocity)
    {
        Collider = collider;
        Contacts = contacts;
        RelativeVelocity = relativeVelocity;
    }
}
