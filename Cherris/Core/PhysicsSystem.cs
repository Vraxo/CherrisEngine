using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Cherris.Components;
using System.Numerics;

namespace Cherris.Core;

public class PhysicsSystem : IDisposable
{
    public Simulation Simulation { get; private set; }

    private readonly BufferPool _bufferPool;
    private readonly ThreadDispatcher _threadDispatcher;
    private readonly Dictionary<BodyHandle, RigidBody> _bodyHandleToComponent = [];
    private readonly Dictionary<StaticHandle, RigidBody> _staticHandleToComponent = [];

    private float _accumulator = 0.0f;
    private const float FixedTimeStep = 1.0f / 60.0f;
    private const float MaxDeltaTime = 0.25f;

    public PhysicsSystem()
    {
        _bufferPool = new BufferPool();

        int threadCount = Math.Max(1, Environment.ProcessorCount - 1);
        _threadDispatcher = new ThreadDispatcher(threadCount);

        Simulation = Simulation.Create(
            _bufferPool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks(new Vector3(0, -9.81f, 0)),
            new SolveDescription(8, 1));
    }

    public void RegisterBody(RigidBody bodyComponent)
    {
        if (bodyComponent.BepuBodyHandle.HasValue)
        {
            _bodyHandleToComponent[bodyComponent.BepuBodyHandle.Value] = bodyComponent;
        }
        else if (bodyComponent.BepuStaticHandle.HasValue)
        {
            _staticHandleToComponent[bodyComponent.BepuStaticHandle.Value] = bodyComponent;
        }
    }

    public void UnregisterBody(RigidBody bodyComponent)
    {
        if (bodyComponent.BepuBodyHandle.HasValue)
        {
            _bodyHandleToComponent.Remove(bodyComponent.BepuBodyHandle.Value);
        }
        else if (bodyComponent.BepuStaticHandle.HasValue)
        {
            _staticHandleToComponent.Remove(bodyComponent.BepuStaticHandle.Value);
        }
    }

    public void Update(float deltaTime)
    {
        float effectiveDeltaTime = Math.Min(deltaTime, MaxDeltaTime);
        _accumulator += effectiveDeltaTime;

        while (_accumulator >= FixedTimeStep)
        {
            Simulation.Timestep(FixedTimeStep, _threadDispatcher);
            _accumulator -= FixedTimeStep;
        }

        SyncTransforms();
    }

    private void SyncTransforms()
    {
        foreach (var (handle, bodyComponent) in _bodyHandleToComponent)
        {
            if (bodyComponent.IsStatic)
            {
                continue;
            }

            var bodyReference = Simulation.Bodies.GetBodyReference(handle);
            if (!bodyReference.Exists)
            {
                continue;
            }

            bodyComponent.GameObject.Transform.Position = bodyReference.Pose.Position;
            bodyComponent.GameObject.Transform.Rotation = bodyReference.Pose.Orientation;
        }
    }

    public void Dispose()
    {
        Simulation.Dispose();
        _threadDispatcher.Dispose();
        _bufferPool.Clear();
    }

    private struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
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
            pairMaterial = new PairMaterialProperties
            {
                FrictionCoefficient = 0.5f,
                MaximumRecoveryVelocity = 2f,
                SpringSettings = new SpringSettings(30f, 1f)
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

    private struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        private Vector3 _gravity;
        private readonly float _linearDamping;
        private readonly float _angularDamping;

        // FIX: These properties must return values, not throw exceptions
        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentum;
        public bool AllowSubstepsForUnconstrainedBodies => false;
        public bool IntegrateVelocityForKinematics => false;  // Line 153 - was throwing NotImplementedException

        public PoseIntegratorCallbacks(Vector3 gravity, float linearDamping = 0.03f, float angularDamping = 0.03f)
        {
            _gravity = gravity;
            _linearDamping = linearDamping;
            _angularDamping = angularDamping;
        }

        public void Initialize(Simulation simulation) { }

        public void PrepareForIntegration(float dt) { }

        public void IntegrateVelocity(int bodyIndex, in RigidPose pose, in BodyInertia localInertia, int workerIndex, ref BodyVelocity velocity)
        {
            velocity.Linear += _gravity * 0.016f; // Assuming 60fps, use your actual timestep
            velocity.Linear *= MathF.Pow(1 - _linearDamping, 0.016f);
            velocity.Angular *= MathF.Pow(1 - _angularDamping, 0.016f);
        }

        // Wide vector version - also must be implemented
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
            ref BodyVelocityWide velocity)
        {
            // Create gravity wide vector by setting fields directly
            Vector<float> gravityX = new(_gravity.X);
            Vector<float> gravityY = new(_gravity.Y);
            Vector<float> gravityZ = new(_gravity.Z);

            // Apply gravity: velocity += gravity * dt
            velocity.Linear.X += gravityX;
            velocity.Linear.Y += gravityY;
            velocity.Linear.Z += gravityZ;

            // Apply damping
            Vector<float> linearDamping = new(MathF.Pow(1 - _linearDamping, 0.016f));
            Vector<float> angularDamping = new(MathF.Pow(1 - _angularDamping, 0.016f));

            velocity.Linear.X *= linearDamping;
            velocity.Linear.Y *= linearDamping;
            velocity.Linear.Z *= linearDamping;

            velocity.Angular.X *= angularDamping;
            velocity.Angular.Y *= angularDamping;
            velocity.Angular.Z *= angularDamping;
        }
    }
}