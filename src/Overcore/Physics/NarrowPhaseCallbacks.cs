using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;

namespace Overcore.Core.Physics;

/// <summary>
/// What the solver asks the engine while it looks for contacts: whether a pair may touch at all, and what the pair
/// is made of — friction and bounce, which used to be hardcoded for every surface.
/// </summary>
/// <remarks>
/// <para>
/// Every answer comes from the world that owns these callbacks: two colliders on layers told to ignore each other,
/// or a pair with a trigger in it, are refused here, which is what makes a trigger a volume rather than a wall.
/// </para>
/// <para>
/// The manifolds that are accepted are recorded as they are found, so the objects behind them can be told once the
/// step is over — this is where <see cref="Components.Script.OnCollisionEnter(Components.Collision)"/> comes from.
/// A worker thread may be inside this struct at the time, so it only ever touches its own buffer through the world.
/// </para>
/// </remarks>
public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    private readonly PhysicsSystem _world;

    /// <summary>Creates the callbacks that answer for a world.</summary>
    /// <param name="world">The world whose colliders and surfaces these callbacks speak for.</param>
    public NarrowPhaseCallbacks(PhysicsSystem world) => _world = world;

    public void Initialize(Simulation simulation) { }

    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin) =>
        _world.ContactsAllowed(a, b);

    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) =>
        _world.ContactsAllowed(pair.A, pair.B);

    public bool ConfigureContactManifold<TManifold>(
        int workerIndex,
        CollidablePair pair,
        ref TManifold manifold,
        out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial = _world.MaterialFor(pair.A, pair.B);
        _world.RecordContact(workerIndex, pair, ref manifold);

        return true;
    }

    public bool ConfigureContactManifold(
        int workerIndex,
        CollidablePair pair,
        int childIndexA,
        int childIndexB,
        ref ConvexContactManifold manifold)
    {
        // Compound children: the pair's material and its contacts were already taken from the parent manifold.
        return true;
    }

    // The callbacks own no resources; the simulation disposes its own allocations.
    public void Dispose() { }
}
