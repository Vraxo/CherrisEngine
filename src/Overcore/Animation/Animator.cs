using Overcore.Animation;
using Overcore.Core;
using Overcore.Core.Logging;
using System.Numerics;

namespace Overcore.Components;

/// <summary>
/// Plays an <see cref="AnimatorController"/>: the parameters scripts set, the state machine that picks a state from
/// them, and the pose it samples. This is Unity's <c>Animator</c>, and the layer above <see cref="AnimationPlayer"/>,
/// which plays a single clip with no decisions to make.
/// </summary>
/// <remarks>
/// <para>
/// A state machine is evaluated **once per frame**, after the pose has been sampled, and a frame can take at most one
/// transition — a chain of states that would fire in the same frame fires one per frame instead. That is Unity's rule,
/// and it is what stops a controller in a bad shape from spinning through its whole graph in a single tick.
/// </para>
/// <para>
/// A transition is not a cut: it has a duration, and during it both states are sampled and blended by how far through
/// the transition the frame is. The destination is faded in over the source rather than both being sampled at partial
/// weights against an empty pose, which is what makes a cross-fade land exactly on the destination at the end.
/// </para>
/// <para>
/// The component writes the pose onto its own object's hierarchy, the same way <see cref="AnimationPlayer"/> does, so a
/// rigged character's bone transforms animate whether or not anything is skinned to them.
/// </para>
/// </remarks>
public class Animator : Component
{
    private readonly AnimationPose _pose = new();
    private readonly AnimationPose _scratch = new();
    private readonly AnimationEventDispatch _events = new();
    private readonly List<LayerMachine> _machines = [];

    /// <summary>Per-parameter spring velocity, so a damped set continues the motion it started instead of restarting.</summary>
    private readonly Dictionary<string, float> _velocities = new(StringComparer.Ordinal);

    /// <summary>The controller to play, or null for an Animator that writes nothing.</summary>
    public AnimatorController? Controller
    {
        get => _controller;
        set
        {
            if (ReferenceEquals(_controller, value))
            {
                return;
            }

            _controller = value;
            _machines.Clear();
            _velocities.Clear();
        }
    }

    private AnimatorController? _controller;

    /// <summary>Which phase of the frame this evaluates in.</summary>
    public AnimatorUpdateMode UpdateMode { get; set; } = AnimatorUpdateMode.Normal;

    /// <summary>Scales every state's speed, for a global slow-motion without touching the controller.</summary>
    public float Speed { get; set; } = 1f;

    /// <summary>The state the machine is in on layer zero, or null before the controller has run.</summary>
    public AnimatorState? CurrentState => Machine(0)?.Current;

    /// <summary>Normalized position in the current state of layer zero, <c>0..1</c> for a looping state.</summary>
    public float NormalizedTime => Machine(0)?.NormalizedTime ?? 0f;

    /// <summary>True while a transition is in flight on any layer.</summary>
    public bool IsInTransition => _machines.Exists(machine => machine.IsInTransition);

    /// <summary>The pose the last evaluation produced, for a renderer or an inspector to read back.</summary>
    public AnimationPose Pose => _pose;

    /// <summary>Fires the clip events of the states being played.</summary>
    public AnimationEventDispatch Events => _events;

    /// <summary>Raised after a transition is taken, with the layer, the state it left and the one it entered.</summary>
    public event Action<int, AnimatorState?, AnimatorState>? StateChanged;

    // ---------------------------------------------------------------- parameters

    public void SetFloat(string name, float value) => Set(name, value);

    public void SetFloat(int hash, float value) => Set(hash, value);

    public void SetInt(string name, int value) => Set(name, value);

    public void SetInt(int hash, int value) => Set(hash, value);

    public void SetBool(string name, bool value) => Set(name, value ? 1f : 0f);

    public void SetBool(int hash, bool value) => Set(hash, value ? 1f : 0f);

    /// <summary>
    /// Sets a trigger. A trigger is a bool a transition consumes when it fires, so setting it twice before it is read
    /// is still one trigger — which is why this stores a flag rather than counting.
    /// </summary>
    public void SetTrigger(string name) => Set(name, 1f);

    public void SetTrigger(int hash) => Set(hash, 1f);

    /// <summary>Clears a trigger by hand, for a script that wants to cancel a jump before it fires.</summary>
    public void ResetTrigger(string name) => Set(name, 0f);

    public float GetFloat(string name) => Controller?.FindParameter(name)?.FloatValue ?? 0f;

    public int GetInt(string name) => Controller?.FindParameter(name)?.IntValue ?? 0;

    public bool GetBool(string name) => Controller?.FindParameter(name)?.BoolValue ?? false;

    /// <summary>
    /// Moves a float toward a target over time instead of jumping, which is how a locomotion blend stops snapping.
    /// </summary>
    /// <param name="name">The parameter.</param>
    /// <param name="target">Where it is heading.</param>
    /// <param name="dampTime">Seconds to cover the distance; zero or less snaps.</param>
    /// <param name="deltaTime">Seconds since the previous frame.</param>
    public void SetFloat(string name, float target, float dampTime, float deltaTime)
    {
        AnimatorParameter? parameter = Controller?.FindParameter(name);

        if (parameter is null)
        {
            return;
        }

        if (dampTime <= 0f || deltaTime <= 0f)
        {
            parameter.FloatValue = target;
            _velocities.Remove(name);

            return;
        }

        // A critically damped spring, which is what `dampTime` names: the value accelerates from rest, overshoots
        // nothing, and settles — so a locomotion blend eases rather than snapping or decaying unevenly.
        float omega = 2f / dampTime;
        float x = omega * deltaTime;
        float decay = 1f / (1f + x + (0.48f * x * x) + (0.235f * x * x * x));

        float change = parameter.FloatValue - target;
        float temp = (_velocities.GetValueOrDefault(name) + (omega * change)) * deltaTime;
        float velocity = (_velocities.GetValueOrDefault(name) - (omega * temp)) * decay;

        _velocities[name] = velocity;

        parameter.FloatValue = target + ((change + temp) * decay);
    }

    /// <summary>True when a layer's state is the one named — Unity's <c>IsName("Run")</c>.</summary>
    public bool IsInState(string stateName, int layer = 0) =>
        string.Equals(Machine(layer)?.Current?.Name, stateName, StringComparison.Ordinal);

    /// <summary>
    /// Jumps straight to a state at a normalized time, with no transition — what a scrubber or a teleport uses.
    /// </summary>
    public void Play(string stateName, int layer = 0, float normalizedTime = 0f)
    {
        AnimatorState? state = Controller?.FindState(stateName, layer);

        if (state is null)
        {
            Logger.Warning($"[Animator] No state called '{stateName}' on layer {layer}.");

            return;
        }

        LayerMachine? machine = Machine(layer);

        machine?.Enter(state, normalizedTime, notify: true);
    }

    /// <summary>
    /// Advances every layer one frame and writes the combined pose. Called by the scene's animation step.
    /// </summary>
    /// <param name="deltaTime">Seconds since the previous frame, in the unit <see cref="UpdateMode"/> asks for.</param>
    public void Advance(float deltaTime)
    {
        AnimatorController? controller = Controller;

        if (controller is null || GameObject is null || !IsActive || !Enabled)
        {
            return;
        }

        if (controller.Layers.Count == 0)
        {
            return;
        }

        float step = deltaTime > 0f && float.IsFinite(deltaTime) ? deltaTime : 0f;

        EnsureMachines();

        // The base pose is whatever the hierarchy holds, so a layer that addresses a bone and a lower one that does
        // not compose rather than the second wiping out the first.
        _pose.CaptureFrom(Transform);

        for (int index = 0; index < _machines.Count && index < controller.Layers.Count; index++)
        {
            AnimatorLayer layer = controller.Layers[index];

            _machines[index].Advance(layer, step, Speed, ParameterLookup, _pose, _scratch);
        }

        _pose.ApplyTo(Transform, warnOnMissing: false);

        // Events fire after the pose is written, so a handler reads the frame it was fired for.
        FireEvents(controller);
    }

    /// <summary>Builds one machine per layer, and rebuilds them when the controller's layers change shape.</summary>
    private void EnsureMachines()
    {
        int wanted = Controller?.Layers.Count ?? 0;

        while (_machines.Count > wanted)
        {
            _machines.RemoveAt(_machines.Count - 1);
        }

        while (_machines.Count < wanted)
        {
            int layerIndex = _machines.Count;

            _machines.Add(new LayerMachine(state => StateChanged?.Invoke(layerIndex, null, state)));
        }
    }

    private LayerMachine? Machine(int layer) =>
        layer >= 0 && layer < _machines.Count ? _machines[layer] : null;

    /// <summary>Fires each layer's clip events for the interval the frame covered.</summary>
    private void FireEvents(AnimatorController controller)
    {
        if (GameObject is null)
        {
            return;
        }

        for (int index = 0; index < _machines.Count && index < controller.Layers.Count; index++)
        {
            _machines[index].FireEvents(_events, GameObject);
        }
    }

    /// <summary>
    /// Writes a parameter, whether it was addressed by name or by hash. A name that is not in the controller is
    /// ignored rather than throwing — a script outliving a renamed parameter should go quiet, not take the frame down.
    /// </summary>
    private void Set(string name, float value)
    {
        AnimatorParameter? parameter = Controller?.FindParameter(name);

        if (parameter is not null)
        {
            Write(parameter, value);
        }
    }

    private void Set(int hash, float value)
    {
        AnimatorParameter? parameter = Controller?.FindParameter(hash);

        if (parameter is not null)
        {
            Write(parameter, value);
        }
    }

    /// <summary>
    /// Stores a value in the field its type actually uses. A caller passing a bool as 1f is not a float parameter
    /// being written by accident — the type on the controller decides which field the value lands in.
    /// </summary>
    private static void Write(AnimatorParameter parameter, float value)
    {
        switch (parameter.Type)
        {
            case AnimatorParameterType.Float:
                parameter.FloatValue = value;
                break;

            case AnimatorParameterType.Int:
                parameter.IntValue = (int)value;
                break;

            case AnimatorParameterType.Bool:
            case AnimatorParameterType.Trigger:
                parameter.BoolValue = value > 0f;
                break;
        }
    }

    /// <summary>A parameter lookup by hash, which is what a condition carries.</summary>
    private AnimatorParameter? ParameterLookup(int hash) => Controller?.FindParameter(hash);

    /// <summary>A value folded into <c>0..1</c>, so a clock running past a state's end wraps rather than overshoots.</summary>
    private static float Wrap01(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        float wrapped = value % 1f;

        return wrapped < 0f ? wrapped + 1f : wrapped;
    }

    /// <summary>How long a motion runs, in seconds. A state with no motion has no length to divide by.</summary>
    private static float LengthOf(AnimatorMotion? motion)
    {
        if (motion is ClipMotion clip)
        {
            return MathF.Max(clip.Clip.Length, 0f);
        }

        if (motion is BlendTree1D tree)
        {
            return MathF.Max(tree.Length, 0f);
        }

        return 0f;
    }

    /// <summary>A state's clock in seconds, as a position through its motion. A motion of no length is at its start.</summary>
    private static float Normalize(float seconds, AnimatorState state)
    {
        float length = LengthOf(state.Motion);

        return length > 0f ? seconds / length : 0f;
    }

    /// <summary>Moves a value toward a target by at most one step, without overshooting it.</summary>
    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
        {
            return target;
        }

        return current + (MathF.Sign(target - current) * maxDelta);
    }

    /// <summary>Gives the component a fresh machine when it is cloned, so two copies do not share a transition.</summary>
    internal override void OnInstantiated()
    {
        base.OnInstantiated();

        _machines.Clear();
        _velocities.Clear();
        _pose.Clear();
        _scratch.Clear();
    }

    /// <summary>
    /// One layer's state machine: the state it is in, the transition it is taking, and the two clocks that keep a
    /// cross-fade from snapping when it lands.
    /// </summary>
    private sealed class LayerMachine
    {
        private readonly Action<AnimatorState> _entered;

        private AnimatorState? _current;
        private AnimatorState? _transitionSource;
        private AnimatorTransition? _transition;
        private float _transitionTime;
        private float _stateTime;
        private float _destinationTime;
        private float _normalizedTime;
        private float _previousNormalizedTime;

        /// <summary>
        /// Normalized time since the state was entered, without wrapping. Exit time is a threshold within the state's
        /// stay, so checking it against the wrapped clock would make <c>exitTime = 1</c> unreachable on a looping
        /// clip: the clock hits the end, wraps to near zero, and the check never sees the crossing.
        /// </summary>
        private float _elapsedNormalized;

        public LayerMachine(Action<AnimatorState> entered) => _entered = entered;

        public AnimatorState? Current => _current;

        public float NormalizedTime => _normalizedTime;

        public bool IsInTransition => _transition is not null;

        /// <summary>Enters a state outright, resetting its clock and dropping any transition in flight.</summary>
        public void Enter(AnimatorState state, float normalizedTime, bool notify)
        {
            _current = state;

            // The clock is seconds, so the normalized time a caller names is multiplied out by the motion's length.
            _stateTime = Math.Clamp(normalizedTime, 0f, 1f) * LengthOf(state.Motion);
            _normalizedTime = Wrap01(Normalize(_stateTime, state) + state.CycleOffset);
            _previousNormalizedTime = _normalizedTime;
            _elapsedNormalized = 0f;
            _transition = null;
            _transitionSource = null;
            _transitionTime = 0f;
            _destinationTime = 0f;

            if (notify)
            {
                _entered(state);
            }
        }

        /// <summary>Advances the machine one frame and blends this layer's contribution into the pose.</summary>
        public void Advance(
            AnimatorLayer layer,
            float deltaTime,
            float speedScale,
            Func<int, AnimatorParameter?> parameter,
            AnimationPose pose,
            AnimationPose scratch)
        {
            if (_current is null)
            {
                if (layer.DefaultState is null)
                {
                    return;
                }

                Enter(layer.DefaultState, 0f, notify: false);
            }

            _previousNormalizedTime = _normalizedTime;

            AdvanceClock(deltaTime, speedScale);
            AdvanceTransition(layer, deltaTime, speedScale, parameter);
            Sample(layer, pose, scratch, parameter);
        }

        /// <summary>Fires the events of whichever clips this layer played this frame.</summary>
        public void FireEvents(AnimationEventDispatch events, GameObject root)
        {
            if (_current?.Motion is not ClipMotion clipMotion)
            {
                return;
            }

            events.Fire(
                clipMotion.Clip,
                root,
                _previousNormalizedTime * clipMotion.Clip.Length,
                _normalizedTime * clipMotion.Clip.Length,
                _normalizedTime < _previousNormalizedTime);
        }

        private void AdvanceTransition(
            AnimatorLayer layer,
            float deltaTime,
            float speedScale,
            Func<int, AnimatorParameter?> parameter)
        {
            if (_transition is null)
            {
                // No transition in flight: this is the one place a frame may start one, which is what stops a
                // controller from chaining through its whole graph in a single tick.
                TryTakeTransition(layer, parameter);

                if (_transition is null)
                {
                    return;
                }

                // A transition that begins now still owes this frame its time, or a cut would sit in flight until
                // the next tick for no reason the author asked for.
            }

            // The two states each run on their own clock, so neither jumps when the other starts.
            _destinationTime += deltaTime * _transition!.Destination.Speed * speedScale;
            _transitionTime += deltaTime;

            if (_transition.Duration > 0f && _transitionTime < _transition.Duration)
            {
                return;
            }

            // Landing is not a cut: the destination becomes current carrying the clock it accrued, so the blend holds
            // its final frame instead of snapping to the start.
            AnimatorState destination = _transition.Destination;

            _current = destination;
            _stateTime = _destinationTime;
            _normalizedTime = Wrap01(Normalize(_stateTime, destination) + destination.CycleOffset);
            _previousNormalizedTime = _normalizedTime;
            _elapsedNormalized = 0f;
            _transition = null;
            _transitionSource = null;
            _transitionTime = 0f;
            _destinationTime = 0f;

            _entered(destination);
        }

        private void TryTakeTransition(AnimatorLayer layer, Func<int, AnimatorParameter?> parameter)
        {
            if (_current is null)
            {
                return;
            }

            // Any State first, so an interrupt wins over the current state's own exits.
            if (TryFind(layer.AnyStateTransitions, source: null, parameter, out AnimatorTransition? interrupt))
            {
                Begin(interrupt, parameter);

                return;
            }

            if (TryFind(_current.Transitions, _current, parameter, out AnimatorTransition? transition))
            {
                Begin(transition, parameter);
            }
        }

        private bool TryFind(
            List<AnimatorTransition> transitions,
            AnimatorState? source,
            Func<int, AnimatorParameter?> parameter,
            out AnimatorTransition transition)
        {
            foreach (AnimatorTransition candidate in transitions)
            {
                // A transition with an exit time may not start until the state has reached it; one without may start
                // the moment its conditions pass.
                if (candidate.HasExitTime && source is not null && _elapsedNormalized < candidate.ExitTime)
                {
                    continue;
                }

                if (!candidate.AreConditionsMet(parameter))
                {
                    continue;
                }

                transition = candidate;

                return true;
            }

            transition = null!;

            return false;
        }

        private void Begin(AnimatorTransition transition, Func<int, AnimatorParameter?> parameter)
        {
            _transitionSource = _current;
            _transition = transition;
            _transitionTime = 0f;
            _destinationTime = Math.Clamp(transition.TransitionOffset, 0f, 1f) * LengthOf(_current?.Motion);

            // A trigger is consumed by the transition that read it, so a second jump press does not fire twice.
            foreach (AnimatorCondition condition in transition.Conditions)
            {
                AnimatorParameter? consumed = parameter(condition.ParameterHash);

                if (consumed?.Type == AnimatorParameterType.Trigger)
                {
                    consumed.BoolValue = false;
                }
            }
        }

        private void AdvanceClock(float deltaTime, float speedScale)
        {
            if (_current?.Motion is null || _current.Motion.IsEmpty)
            {
                return;
            }

            // The source keeps playing underneath a transition, so a cross-fade does not freeze the pose it leaves.
            float advance = deltaTime * _current.Speed * speedScale;

            _stateTime += advance;
            _normalizedTime = Wrap01(Normalize(_stateTime, _current) + _current.CycleOffset);

            // A negative speed plays a clip backwards, which still spends time in the state, so progress is unsigned.
            float length = LengthOf(_current.Motion);

            _elapsedNormalized += length > 0f ? MathF.Abs(advance) / length : 0f;
        }

        private void Sample(
            AnimatorLayer layer,
            AnimationPose pose,
            AnimationPose scratch,
            Func<int, AnimatorParameter?> parameter)
        {
            if (_current?.Motion is null || _current.Motion.IsEmpty)
            {
                return;
            }

            float weight = Math.Clamp(layer.Weight, 0f, 1f);

            if (weight <= 0f)
            {
                return;
            }

            if (_transition is null || _transitionSource is null)
            {
                _current.Motion.Sample(_normalizedTime, pose, weight, parameter);

                return;
            }

            // Mid-transition: the destination is sampled over a copy of the frame's pose and faded in by how far the
            // transition has run, so the result lands exactly on the destination at the end.
            float progress = _transition.Duration <= 0f
                ? 1f
                : Math.Clamp(_transitionTime / _transition.Duration, 0f, 1f);

            scratch.CopyFrom(pose);

            _transition.Destination.Motion?.Sample(
                Wrap01(Normalize(_destinationTime, _transition.Destination) + _transition.Destination.CycleOffset),
                scratch,
                1f,
                parameter);

            _current.Motion.Sample(_normalizedTime, pose, weight * (1f - progress), parameter);

            if (layer.BlendMode == AnimatorLayerBlendMode.Additive)
            {
                // An additive layer contributes only the difference from its reference pose, which is what lets one
                // layer nod a head on top of whatever the base layer is doing with the body.
                ApplyDelta(scratch, layer.AdditiveReferencePose, pose, weight * progress);

                return;
            }

            pose.Blend(scratch, weight * progress);
        }

        /// <summary>Adds a pose's difference from a reference pose onto the accumulated pose.</summary>
        private static void ApplyDelta(AnimationPose posed, AnimationPose? reference, AnimationPose target, float weight)
        {
            foreach (string path in posed.BonePaths)
            {
                AnimationPose.BonePose bone = posed.Get(path);

                AnimationPose.BonePose basePose = reference?.Get(path) ?? AnimationPose.BonePose.Identity;

                Vector3 position = basePose.Position + ((bone.Position - basePose.Position) * weight);
                Vector3 scale = basePose.Scale + ((bone.Scale - basePose.Scale) * weight);

                // A rotation delta is a quaternion proportion: slerping from the identity by the weight applies that
                // fraction of the turn, which a component-wise scale of the quaternion would not.
                Quaternion delta = Quaternion.Normalize(Quaternion.Multiply(
                    Quaternion.Inverse(basePose.Rotation),
                    bone.Rotation));

                Quaternion applied = Quaternion.Slerp(Quaternion.Identity, delta, weight);

                target.Set(path, position, Quaternion.Multiply(basePose.Rotation, applied), scale);
            }
        }
    }
}
