using BepuPhysics;
using BepuUtilities;
using System.Numerics;

namespace Overcore.Core.Physics;

/// <summary>
/// The settings a world keeps that the integrator reads every step. It is a shared object rather than a copy so
/// that changing gravity takes effect on the next step instead of at the next restart.
/// </summary>
internal sealed class SimulationState
{
    public SimulationState(Vector3 gravity) => Gravity = gravity;

    /// <summary>Gravity, in metres per second squared.</summary>
    public Vector3 Gravity { get; set; }
}

public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    private readonly SimulationState _settings;
    private readonly float _linearDamping;
    private readonly float _angularDamping;

    // FIX: These properties must return values, not throw exceptions
    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentum;
    public bool AllowSubstepsForUnconstrainedBodies => false;
    public bool IntegrateVelocityForKinematics => false;  // Line 153 - was throwing NotImplementedException

    /// <summary>Creates the integrator that reads a world's gravity.</summary>
    /// <param name="settings">The settings the world owns, read fresh on every step.</param>
    /// <param name="linearDamping">How quickly linear motion bleeds away, per second.</param>
    /// <param name="angularDamping">How quickly spin bleeds away, per second.</param>
    public PoseIntegratorCallbacks(SimulationState settings, float linearDamping = 0.03f, float angularDamping = 0.03f)
    {
        _settings = settings;
        _linearDamping = linearDamping;
        _angularDamping = angularDamping;
    }

    public void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt) { }

    public void IntegrateVelocity(
        Vector<int> bodyIndices,
        Vector3Wide position,
        QuaternionWide orientation,
        BodyInertiaWide localInertia,
        Vector<int> integrationMask,
        int workerIndex,
        Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        // Gravity for each lane, scaled by that lane's own timestep.
        Vector3Wide gravity;
        gravity.X = new Vector<float>(_settings.Gravity.X) * dt;
        gravity.Y = new Vector<float>(_settings.Gravity.Y) * dt;
        gravity.Z = new Vector<float>(_settings.Gravity.Z) * dt;

        velocity.Linear.X += gravity.X;
        velocity.Linear.Y += gravity.Y;
        velocity.Linear.Z += gravity.Z;

        // Damping, also scaled by dt.
        Vector<float> linearDampingFactor = Vector<float>.One - (new Vector<float>(_linearDamping) * dt);
        Vector<float> angularDampingFactor = Vector<float>.One - (new Vector<float>(_angularDamping) * dt);

        velocity.Linear.X *= linearDampingFactor;
        velocity.Linear.Y *= linearDampingFactor;
        velocity.Linear.Z *= linearDampingFactor;

        velocity.Angular.X *= angularDampingFactor;
        velocity.Angular.Y *= angularDampingFactor;
        velocity.Angular.Z *= angularDampingFactor;
    }
}
