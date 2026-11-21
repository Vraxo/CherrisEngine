using BepuPhysics;
using BepuUtilities;
using BepuUtilities.Memory;
using Cherris.Components;

namespace Cherris.Core.Physics;

public partial class PhysicsSystem : IDisposable
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
            new PoseIntegratorCallbacks(new(0, -9.81f, 0)),
            new(8, 1));
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

            BodyReference bodyReference = Simulation.Bodies.GetBodyReference(handle);

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
}