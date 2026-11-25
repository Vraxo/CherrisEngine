using Overcore.Components;
using System.Numerics;

namespace Overcore.Core.Physics;

/// <summary>What a physics query does with the colliders it finds marked as triggers, Unity's enum of the same name.</summary>
public enum QueryTriggerInteraction
{
    /// <summary>Follow <see cref="Physics.QueriesHitTriggers"/>.</summary>
    UseGlobal = 0,

    /// <summary>Only report colliders that are not triggers.</summary>
    Ignore = 1,

    /// <summary>Report triggers like anything else.</summary>
    Collide = 2
}

/// <summary>
/// What a ray or a cast ran into, Unity's <c>RaycastHit</c>: where it hit, which way the surface faced, and the
/// collider it belongs to.
/// </summary>
public readonly struct RaycastHit
{
    /// <summary>Where the ray met the collider, in world space.</summary>
    public Vector3 Point { get; init; }

    /// <summary>Which way the surface faces at <see cref="Point"/>, in world space.</summary>
    public Vector3 Normal { get; init; }

    /// <summary>How far along the ray the hit is, in world units.</summary>
    public float Distance { get; init; }

    /// <summary>Where the query started, which is the origin of <see cref="Point"/>'s ray.</summary>
    public Vector3 Origin { get; init; }

    /// <summary>The collider that was hit.</summary>
    public Collider Collider { get; init; }

    /// <summary>The object the hit collider is on.</summary>
    public GameObject GameObject => Collider.GameObject
        ?? throw new InvalidOperationException("The hit collider is not attached to a GameObject.");

    /// <summary>The transform of the object the hit collider is on.</summary>
    public Transform Transform => Collider.Transform;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Collider.GetType().Name} on '{Collider.GameObject?.Name}' at {Point} ({Distance:0.###} away)";
}
