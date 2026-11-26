using System.Numerics;

namespace Overcore.Animation;

/// <summary>Which property of a bone a curve drives — the three glTF paths.</summary>
public enum AnimationProperty
{
    LocalPosition,
    LocalRotation,
    LocalScale
}

/// <summary>Which component of that property a curve drives. Component curves are how a glTF channel is stored.</summary>
public enum AnimationAxis
{
    X,
    Y,
    Z,
    W
}

/// <summary>How the values between two keys are produced. A clip carries one mode for all of its curves.</summary>
public enum AnimationInterpolation
{
    /// <summary>Straight lines, or slerp for rotations.</summary>
    Linear,

    /// <summary>Hold the earlier key until the next one's exact time.</summary>
    Constant,

    /// <summary>Hermite segments whose tangents come from the asset (glTF <c>CUBICSPLINE</c>).</summary>
    Cubic
}

/// <summary>One key: a time in seconds, the value there, and the tangents a cubic segment needs.</summary>
public readonly record struct AnimationKey
{
    public AnimationKey(float time, float value) : this(time, value, 0f, 0f)
    {
    }

    public AnimationKey(float time, float value, float inTangent, float outTangent)
    {
        Time = time;
        Value = value;
        InTangent = inTangent;
        OutTangent = outTangent;
    }

    /// <summary>Seconds from the clip's start.</summary>
    public float Time { get; init; }

    /// <summary>The value at <see cref="Time"/>.</summary>
    public float Value { get; init; }

    /// <summary>Derivative arriving at the key, for <see cref="AnimationInterpolation.Cubic"/>.</summary>
    public float InTangent { get; init; }

    /// <summary>Derivative leaving the key, for <see cref="AnimationInterpolation.Cubic"/>.</summary>
    public float OutTangent { get; init; }
}

/// <summary>
/// One property of one bone, sampled over time: Unity's <c>AnimationCurve</c> inside an <c>AnimationClip</c>, and
/// one glTF animation channel.
/// </summary>
/// <remarks>
/// <para>
/// A curve is identified by the path of the bone it drives and the property/axis it drives there. The path is the
/// hierarchy path from the clip's root (<c>"Hips/Spine/Chest"</c>), with the empty string meaning the root itself —
/// the same addressing glTF channels and Unity bindings use.
/// </para>
/// <para>
/// Keys are kept sorted by time, because sampling is a bracketing search. Adding a key out of order sorts on the
/// next sample rather than throwing: an importer reads a file's keys in file order, which is not always sorted.
/// </para>
/// </remarks>
public class AnimationCurve
{
    private readonly List<AnimationKey> _keys = [];

    /// <param name="bonePath">Hierarchy path from the clip's root; empty for the root.</param>
    /// <param name="property">Which transform property is driven.</param>
    /// <param name="axis">Which component of it is driven.</param>
    public AnimationCurve(string bonePath, AnimationProperty property, AnimationAxis axis)
    {
        BonePath = bonePath ?? string.Empty;
        Property = property;
        Axis = axis;
    }

    /// <summary>Hierarchy path from the clip's root, <c>"Hips/Spine"</c>, or empty for the root.</summary>
    public string BonePath { get; }

    public AnimationProperty Property { get; }

    public AnimationAxis Axis { get; }

    /// <summary>
    /// How this curve's keys are interpolated. glTF puts the mode on the sampler, so it travels with the channel: one
    /// clip can hold a stepped visibility track and a cubic limb at the same time. A hand-built curve defaults to
    /// linear, which is also the clip-wide default.
    /// </summary>
    public AnimationInterpolation Interpolation { get; set; } = AnimationInterpolation.Linear;

    /// <summary>The keys, in time order.</summary>
    public IReadOnlyList<AnimationKey> Keys => _keys;

    /// <summary>The time of the first key, or zero when the curve is empty.</summary>
    public float StartTime => _keys.Count == 0 ? 0f : _keys[0].Time;

    /// <summary>The time of the last key, or zero when the curve is empty.</summary>
    public float EndTime => _keys.Count == 0 ? 0f : _keys[^1].Time;

    public void AddKey(float time, float value) => AddKey(new AnimationKey(time, value));

    /// <summary>Adds a key, keeping the list sorted if the time is not past the last one.</summary>
    public void AddKey(AnimationKey key)
    {
        if (!float.IsFinite(key.Time))
        {
            return;
        }

        if (_keys.Count == 0 || key.Time >= _keys[^1].Time)
        {
            _keys.Add(key);
            return;
        }

        int index = _keys.FindIndex(existing => existing.Time > key.Time);
        _keys.Insert(index < 0 ? _keys.Count : index, key);
    }

    /// <summary>
    /// The curve's value at <paramref name="time"/>, by <paramref name="interpolation"/>. Times outside the keys
    /// clamp to the first or last value — wrapping is the clip's job, not the curve's.
    /// </summary>
    public float Evaluate(float time, AnimationInterpolation interpolation)
    {
        if (_keys.Count == 0)
        {
            return 0f;
        }

        if (_keys.Count == 1 || time <= _keys[0].Time)
        {
            return _keys[0].Value;
        }

        if (time >= _keys[^1].Time)
        {
            return _keys[^1].Value;
        }

        int right = _keys.FindIndex(key => key.Time > time);
        int left = right - 1;

        AnimationKey from = _keys[left];
        AnimationKey to = _keys[right];

        float span = to.Time - from.Time;

        if (span <= 0f || interpolation == AnimationInterpolation.Constant)
        {
            return from.Value;
        }

        float t = (time - from.Time) / span;

        return interpolation == AnimationInterpolation.Cubic
            ? EvaluateCubic(from, to, span, t)
            : from.Value + ((to.Value - from.Value) * t);
    }

    /// <summary>
    /// The Hermite segment between two keys. glTF stores tangents as derivatives in value-per-second, so they are
    /// scaled by the segment's length before entering the standard basis polynomials.
    /// </summary>
    private static float EvaluateCubic(AnimationKey from, AnimationKey to, float span, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float m0 = from.OutTangent * span;
        float m1 = to.InTangent * span;

        return (((2f * t3) - (3f * t2) + 1f) * from.Value)
             + ((t3 - (2f * t2) + t) * m0)
             + (((-2f * t3) + (3f * t2)) * to.Value)
             + ((t3 - t2) * m1);
    }
}
