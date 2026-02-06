using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;

namespace Cherris.Core.Physics;

public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public void Initialize(Simulation simulation) { }

    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        // Allow all contact generation by default
        // Return true to allow collision detection between these collidables
        return true;
    }

    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
    {
        // Allow contact generation for compound shapes
        return true;
    }

    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial = new()
        {
            FrictionCoefficient = 0.5f,
            MaximumRecoveryVelocity = 2f,
            SpringSettings = new(30f, 1f)
        };

        return true;
    }

    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
    {
        // For child manifold configuration (compound shapes), just return true
        // Material properties are handled by the parent manifold
        return true;
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}