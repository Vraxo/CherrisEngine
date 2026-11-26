using System.Text;

namespace Overcore.Animation;

/// <summary>What a parameter holds — Unity's four <c>AnimatorControllerParameterType</c> values.</summary>
public enum AnimatorParameterType
{
    Float,
    Int,
    Bool,

    /// <summary>A bool a transition consumes when it fires: set it, let the transition be taken, and it is false again.</summary>
    Trigger
}

/// <summary>How a transition's condition compares a parameter to a value.</summary>
public enum AnimatorConditionMode
{
    If,
    IfNot,
    Greater,
    Less,
    Equals,
    NotEqual
}

/// <summary>How a layer's pose combines with the layers below it.</summary>
public enum AnimatorLayerBlendMode
{
    /// <summary>The layer's pose replaces the lower layers by its weight.</summary>
    Override,

    /// <summary>The layer's pose is added on top of the lower ones, as a delta from its reference pose.</summary>
    Additive
}

/// <summary>Which phase of the frame an <see cref="Animator"/> evaluates in.</summary>
public enum AnimatorUpdateMode
{
    Normal,
    AnimatePhysics,
    UnscaledTime
}

/// <summary>One parameter of a controller, by name and by hash.</summary>
public sealed class AnimatorParameter
{
    public AnimatorParameter(string name, AnimatorParameterType type)
    {
        Name = name ?? string.Empty;
        Type = type;
        Hash = AnimatorController.StringToHash(Name);
    }

    public string Name { get; }

    public AnimatorParameterType Type { get; }

    /// <summary>
    /// The name's FNV-1a hash. Stable across runs and platforms, and deliberately **not** interchangeable with a hash
    /// Unity's editor bakes — see <see cref="AnimatorController.StringToHash"/>.
    /// </summary>
    public int Hash { get; }

    public float FloatValue { get; set; }

    public int IntValue { get; set; }

    public bool BoolValue { get; set; }

    /// <summary>Clears every value back to its type's zero, which is what a fresh controller starts from.</summary>
    public void Reset()
    {
        FloatValue = 0f;
        IntValue = 0;
        BoolValue = false;
    }
}

/// <summary>One term of a transition: a parameter compared against a threshold.</summary>
public readonly record struct AnimatorCondition(int ParameterHash, AnimatorConditionMode Mode, float Threshold)
{
    /// <summary>
    /// Whether this condition passes for a parameter. A condition whose parameter is absent fails, rather than
    /// throwing: a controller that outlives a renamed parameter should stall, not take the frame down.
    /// </summary>
    public bool IsMetBy(AnimatorParameter? parameter)
    {
        if (parameter is null)
        {
            return false;
        }

        return parameter.Type switch
        {
            AnimatorParameterType.Bool or AnimatorParameterType.Trigger =>
                Mode switch
                {
                    AnimatorConditionMode.If => parameter.BoolValue,
                    AnimatorConditionMode.IfNot => !parameter.BoolValue,

                    // A threshold comparison against a bool is meaningless; treat it as unmet instead of guessing.
                    _ => false
                },

            AnimatorParameterType.Int => Mode switch
            {
                AnimatorConditionMode.Greater => parameter.IntValue > Threshold,
                AnimatorConditionMode.Less => parameter.IntValue < Threshold,
                AnimatorConditionMode.Equals => parameter.IntValue == Threshold,
                AnimatorConditionMode.NotEqual => parameter.IntValue != Threshold,

                // A bare If/IfNot against a number means "non-zero", which is how Unity reads it.
                AnimatorConditionMode.If => parameter.IntValue != 0,
                AnimatorConditionMode.IfNot => parameter.IntValue == 0,
                _ => false
            },

            _ => Mode switch
            {
                AnimatorConditionMode.Greater => parameter.FloatValue > Threshold,
                AnimatorConditionMode.Less => parameter.FloatValue < Threshold,
                AnimatorConditionMode.Equals => parameter.FloatValue == Threshold,
                AnimatorConditionMode.NotEqual => parameter.FloatValue != Threshold,
                AnimatorConditionMode.If => parameter.FloatValue != 0f,
                AnimatorConditionMode.IfNot => parameter.FloatValue == 0f,
                _ => false
            }
        };
    }
}

/// <summary>
/// A transition between two states: how long it takes, when it may start, and the conditions that must all pass.
/// </summary>
public sealed class AnimatorTransition
{
    public AnimatorTransition(AnimatorState destination)
    {
        Destination = destination ?? throw new ArgumentNullException(nameof(destination));
    }

    public AnimatorState Destination { get; }

    /// <summary>Seconds the blend takes. Zero is an instant switch.</summary>
    public float Duration { get; set; }

    /// <summary>Normalized time the transition is allowed to start at, honoured when <see cref="HasExitTime"/>.</summary>
    public float ExitTime { get; set; } = 1f;

    public bool HasExitTime { get; set; }

    /// <summary>Normalized offset into the destination the transition lands at, for a clip that is not at its start.</summary>
    public float TransitionOffset { get; set; }

    /// <summary>The conditions, ANDed. An empty list means the transition is unconditional — a "go" the moment it may.</summary>
    public List<AnimatorCondition> Conditions { get; } = [];

    /// <summary>Adds a condition and returns this transition, so a controller can be written as one expression.</summary>
    public AnimatorTransition When(int parameterHash, AnimatorConditionMode mode, float threshold = 0f)
    {
        Conditions.Add(new AnimatorCondition(parameterHash, mode, threshold));

        return this;
    }

    /// <summary>True when every condition passes; an unconditional transition is always true.</summary>
    public bool AreConditionsMet(Func<int, AnimatorParameter?> lookup)
    {
        foreach (AnimatorCondition condition in Conditions)
        {
            if (!condition.IsMetBy(lookup(condition.ParameterHash)))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The motion a state plays: one clip, or a set of clips blended by a single parameter.</summary>
public abstract class AnimatorMotion
{
    /// <summary>True when the motion has nothing to sample, so a state built on it writes no pose.</summary>
    public abstract bool IsEmpty { get; }

    /// <summary>
    /// Samples the motion at a normalized time, into a pose at a weight. The caller owns the pose and the blending.
    /// </summary>
    /// <param name="normalizedTime">Position in the motion, <c>0..1</c>.</param>
    /// <param name="pose">Where to blend the result.</param>
    /// <param name="weight">How much of it to take.</param>
    /// <param name="parameter">The controller's parameter lookup, which a blend tree needs and a clip ignores.</param>
    public abstract void Sample(float normalizedTime, AnimationPose pose, float weight, Func<int, AnimatorParameter?> parameter);
}

/// <summary>One clip, played straight — the overwhelmingly common case.</summary>
public sealed class ClipMotion : AnimatorMotion
{
    public ClipMotion(AnimationClip clip)
    {
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));
    }

    public AnimationClip Clip { get; }

    public override bool IsEmpty => Clip.IsEmpty;

    public override void Sample(float normalizedTime, AnimationPose pose, float weight, Func<int, AnimatorParameter?> parameter)
    {
        // Sampling goes through the clip's own wrap arithmetic, so a looping clip plays a cycle and a clamping one
        // holds its last frame; normalized time is multiplied out here because a state knows its own speed.
        Clip.Sample(normalizedTime * Clip.Length, pose, weight);
    }
}

/// <summary>
/// A 1D blend tree: two or more clips at thresholds, interpolating the pair whose thresholds bracket the parameter.
/// This is what a Speed-driven walk/run looks like, and it covers the overwhelming majority of locomotion.
/// </summary>
/// <remarks>
/// Thresholds are sorted on first use, so a controller may list its motions in any order. A parameter outside the
/// range clamps to the nearest motion; a parameter exactly on a threshold plays that motion alone.
/// </remarks>
public sealed class BlendTree1D : AnimatorMotion
{
    public BlendTree1D(int parameterHash)
    {
        ParameterHash = parameterHash;
    }

    /// <summary>The parameter that drives the blend.</summary>
    public int ParameterHash { get; }

    /// <summary>The motions and the threshold each one sits at.</summary>
    public List<BlendTreeEntry> Entries { get; } = [];

    public override bool IsEmpty => Entries.Count == 0;

    /// <summary>Adds a motion at a threshold, returning this tree so it can be built as one expression.</summary>
    public BlendTree1D Add(AnimationClip clip, float threshold)
    {
        Entries.Add(new BlendTreeEntry(clip, threshold));

        return this;
    }

    public override void Sample(float normalizedTime, AnimationPose pose, float weight, Func<int, AnimatorParameter?> parameter)
    {
        if (Entries.Count == 0)
        {
            return;
        }

        List<BlendTreeEntry> sorted = [.. Entries];
        sorted.Sort((left, right) => left.Threshold.CompareTo(right.Threshold));

        if (sorted.Count == 1)
        {
            SampleClip(sorted[0].Clip, normalizedTime, pose, weight);

            return;
        }

        float value = parameter(ParameterHash)?.FloatValue ?? 0f;

        // Outside the range there is nothing to blend with, so the nearest motion plays alone rather than fading
        // toward a weight the tree does not hold.
        if (value <= sorted[0].Threshold)
        {
            SampleClip(sorted[0].Clip, normalizedTime, pose, weight);

            return;
        }

        if (value >= sorted[^1].Threshold)
        {
            SampleClip(sorted[^1].Clip, normalizedTime, pose, weight);

            return;
        }

        for (int index = 0; index < sorted.Count - 1; index++)
        {
            BlendTreeEntry lower = sorted[index];
            BlendTreeEntry upper = sorted[index + 1];

            if (value < lower.Threshold || value > upper.Threshold)
            {
                continue;
            }

            float span = upper.Threshold - lower.Threshold;

            if (span <= 0f)
            {
                SampleClip(lower.Clip, normalizedTime, pose, weight);

                return;
            }

            float t = (value - lower.Threshold) / span;

            // The far clip is faded in over the near one rather than both being sampled at a fraction: blending two
            // poses at partial weights against an empty pose would mix them with the identity.
            SampleClip(lower.Clip, normalizedTime, pose, weight * (1f - t));
            SampleClip(upper.Clip, normalizedTime, pose, weight * t);

            return;
        }
    }

    private static void SampleClip(AnimationClip clip, float normalizedTime, AnimationPose pose, float weight) =>
        clip.Sample(normalizedTime * clip.Length, pose, weight);
}

/// <summary>One motion of a blend tree, at the threshold it is reached at.</summary>
public sealed class BlendTreeEntry
{
    public BlendTreeEntry(AnimationClip clip, float threshold)
    {
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));
        Threshold = threshold;
    }

    public AnimationClip Clip { get; }

    public float Threshold { get; }
}

/// <summary>A state: a motion, how fast it plays, and the transitions leaving it.</summary>
public sealed class AnimatorState
{
    public AnimatorState(string name, AnimatorMotion? motion = null)
    {
        Name = name ?? string.Empty;
        Motion = motion;
        Hash = AnimatorController.StringToHash(Name);
    }

    public string Name { get; }

    public int Hash { get; }

    public AnimatorMotion? Motion { get; set; }

    /// <summary>Playback rate; negative plays the motion backwards.</summary>
    public float Speed { get; set; } = 1f;

    /// <summary>Normalized position the state starts at, so a cycle can begin part-way through.</summary>
    public float CycleOffset { get; set; }

    /// <summary>The transitions leaving this state, in the order they are tried.</summary>
    public List<AnimatorTransition> Transitions { get; } = [];

    /// <summary>Adds a transition to another state and returns it, so conditions can be chained onto it.</summary>
    public AnimatorTransition TransitionTo(AnimatorState destination)
    {
        AnimatorTransition transition = new(destination);
        Transitions.Add(transition);

        return transition;
    }
}

/// <summary>A layer: a state machine with a weight, blended over the layers beneath it.</summary>
public sealed class AnimatorLayer
{
    public AnimatorLayer(string name)
    {
        Name = name ?? string.Empty;
    }

    public string Name { get; }

    public float Weight { get; set; } = 1f;

    public AnimatorLayerBlendMode BlendMode { get; set; } = AnimatorLayerBlendMode.Override;

    /// <summary>The states, by name, in the order they were added.</summary>
    public Dictionary<string, AnimatorState> States { get; } = new(StringComparer.Ordinal);

    /// <summary>The state the layer starts in. The first state added becomes it when none is set.</summary>
    public AnimatorState? DefaultState { get; set; }

    /// <summary>
    /// Transitions that may fire from any state — Unity's "Any State". Tried before the current state's own
    /// transitions, so a hit reaction interrupts whatever is playing.
    /// </summary>
    public List<AnimatorTransition> AnyStateTransitions { get; } = [];

    /// <summary>The reference pose an additive layer adds its delta to; null means the identity.</summary>
    public AnimationPose? AdditiveReferencePose { get; set; }

    /// <summary>Adds a state under a name and returns it, so a controller can be written as one expression.</summary>
    public AnimatorState AddState(string name, AnimatorMotion? motion = null)
    {
        AnimatorState state = new(name, motion);
        States[name] = state;
        DefaultState ??= state;

        return state;
    }

    /// <summary>Adds a state that plays one clip.</summary>
    public AnimatorState AddState(string name, AnimationClip clip) => AddState(name, new ClipMotion(clip));

    /// <summary>Adds an Any State transition and returns it, so conditions can be chained onto it.</summary>
    public AnimatorTransition AnyState(AnimatorState destination)
    {
        AnimatorTransition transition = new(destination);
        AnyStateTransitions.Add(transition);

        return transition;
    }
}

/// <summary>
/// A controller asset: the parameters, the layers, and the hashing that ties a parameter's name to its number.
/// This is Unity's <c>AnimatorController</c>, and the shape the <c>.controller</c> file of §6 will carry.
/// </summary>
/// <remarks>
/// <para>
/// Parameters can be addressed by name or by the int <see cref="AnimatorController.StringToHash"/> produces. The hash
/// is FNV-1a over the name's UTF-8 bytes: stable across runs and platforms, and deliberately **not** interchangeable
/// with a hash Unity's editor bakes, whose algorithm is not published. Anything that persists a hash must persist the
/// name next to it.
/// </para>
/// <para>
/// The controller is data only — no clock, no pose, no scene — so a state machine can be built and reasoned about
/// without any of the machinery that plays it. <see cref="Animator"/> is what turns it into motion.
/// </para>
/// </remarks>
public class AnimatorController
{
    private readonly List<AnimatorParameter> _parameters = [];
    private readonly Dictionary<int, AnimatorParameter> _byHash = [];
    private readonly Dictionary<string, AnimatorParameter> _byName = new(StringComparer.Ordinal);

    public AnimatorController(string name = "Controller")
    {
        Name = name ?? string.Empty;
    }

    public string Name { get; set; }

    public IReadOnlyList<AnimatorLayer> Layers => _layers;

    private readonly List<AnimatorLayer> _layers = [];

    public IReadOnlyList<AnimatorParameter> Parameters => _parameters;

    /// <summary>
    /// FNV-1a over the name's UTF-8 bytes — the engine's stand-in for Unity's unpublished
    /// <c>Animator.StringToHash</c>.
    /// </summary>
    /// <remarks>
    /// FNV-1a is chosen because it is a handful of lines, has no table, and is defined by a published constant, so two
    /// builds of this engine in two languages agree. It is **not** Unity's algorithm: a number baked by Unity's editor
    /// will not match, which is why every place that stores a hash stores the name as well.
    /// </remarks>
    /// <param name="name">The parameter or state name.</param>
    /// <returns>A stable 32-bit hash.</returns>
    public static int StringToHash(string name)
    {
        // FNV-1a, 32-bit: offset basis 2166136261, prime 16777619.
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        uint hash = offsetBasis;

        foreach (byte value in Encoding.UTF8.GetBytes(name ?? string.Empty))
        {
            hash ^= value;
            hash *= prime;
        }

        return unchecked((int)hash);
    }

    /// <summary>Adds a layer and returns it, so a controller can be written as one expression.</summary>
    public AnimatorLayer AddLayer(string name)
    {
        AnimatorLayer layer = new(name);
        _layers.Add(layer);

        return layer;
    }

    /// <summary>Adds a parameter and returns it, so a controller can be written as one expression.</summary>
    public AnimatorParameter AddParameter(string name, AnimatorParameterType type)
    {
        AnimatorParameter parameter = new(name, type);

        // A name is one parameter: re-adding the same name with a different type replaces the old one rather than
        // leaving two entries with the same hash and different behaviour.
        if (_byHash.TryGetValue(parameter.Hash, out AnimatorParameter? existing))
        {
            _parameters.Remove(existing);
        }

        if (_byName.TryGetValue(parameter.Name, out AnimatorParameter? named))
        {
            _parameters.Remove(named);
        }

        _parameters.Add(parameter);
        _byHash[parameter.Hash] = parameter;
        _byName[parameter.Name] = parameter;

        return parameter;
    }

    /// <summary>Finds a parameter by name, or null.</summary>
    public AnimatorParameter? FindParameter(string name) =>
        _byName.GetValueOrDefault(name ?? string.Empty);

    /// <summary>Finds a parameter by hash, or null. The hash is the only thing a baked condition carries.</summary>
    public AnimatorParameter? FindParameter(int hash) => _byHash.GetValueOrDefault(hash);

    /// <summary>Finds a state by name in a layer.</summary>
    public AnimatorState? FindState(string stateName, int layer = 0) =>
        layer >= 0 && layer < _layers.Count ? _layers[layer].States.GetValueOrDefault(stateName) : null;

    /// <summary>
    /// Every state name a layer declares, which is what a state-machine inspector lists and what a state change
    /// animation targets.
    /// </summary>
    public IEnumerable<string> StateNames(int layer = 0) =>
        layer >= 0 && layer < _layers.Count ? _layers[layer].States.Keys : [];

    /// <summary>Clears every parameter back to its type's zero, as a controller does when a scene reloads.</summary>
    public void ResetParameters()
    {
        foreach (AnimatorParameter parameter in _parameters)
        {
            parameter.Reset();
        }
    }
}
