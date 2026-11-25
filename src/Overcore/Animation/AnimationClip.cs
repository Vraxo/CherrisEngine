namespace Overcore.Animation;

/// <summary>What a clip does outside its own length — Unity's <c>WrapMode</c>, minus the values that only mean
/// something on a legacy <c>Animation</c> component.</summary>
public enum AnimationWrapMode
{
    /// <summary>Play once, then hold the last frame.</summary>
    Once,

    /// <summary>Start again from zero.</summary>
    Loop,

    /// <summary>Reverse at each end, without repeating the end frame.</summary>
    PingPong,

    /// <summary>Hold the first frame before the start and the last frame after the end.</summary>
    ClampForever
}

/// <summary>
/// A named clip: keyframed curves over bone paths, its length, how it wraps, and the events it fires — Unity's
/// <c>AnimationClip</c>, and the file an importer writes next to a model.
/// </summary>
/// <remarks>
/// <para>
/// Increment one of the animation layer: the data, its file shape, and the time arithmetic a caller needs to drive
/// it. Sampling the curves into a pose, blending two clips, and dispatching events are the next increment; nothing
/// here touches a transform, a scene or a GPU, so an importer can build clips with no context at all.
/// </para>
/// <para>
/// A clip's length is the last key of its longest curve unless a caller sets one explicitly — the same rule the
/// importer uses, so a clip read out of a glTF file reports the length the file's channels actually reach.
/// </para>
/// </remarks>
public class AnimationClip
{
    private readonly List<AnimationCurve> _curves = [];
    private readonly List<AnimationEvent> _events = [];

    /// <param name="name">The clip's name; usually the source animation's name.</param>
    public AnimationClip(string name)
    {
        Name = name ?? string.Empty;
    }

    public string Name { get; set; }

    /// <summary>Which asset this clip came from, when it was imported — the model's path, for re-import.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// The clip's length in seconds. Measured from the longest curve unless it was set explicitly, which is what a
    /// loop or an event list needs when the file's last key is not where the author meant the cycle to end.
    /// </summary>
    public float Length
    {
        get => _explicitLength ?? MeasureLength();
        set => _explicitLength = value;
    }

    private float? _explicitLength;

    /// <summary>Keys per second, for the editor's key rows. Does not affect sampling, which is continuous.</summary>
    public float FrameRate { get; set; } = 30f;

    public AnimationWrapMode WrapMode { get; set; } = AnimationWrapMode.Loop;

    public IReadOnlyList<AnimationCurve> Curves => _curves;

    public IReadOnlyList<AnimationEvent> Events => _events;

    /// <summary>True when the clip has no curves at all: nothing it could ever move.</summary>
    public bool IsEmpty => _curves.Count == 0;

    /// <summary>Adds a curve, replacing an existing curve that drives the same property.</summary>
    public void AddCurve(AnimationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        int existing = _curves.FindIndex(candidate =>
            candidate.Property == curve.Property &&
            candidate.Axis == curve.Axis &&
            string.Equals(candidate.BonePath, curve.BonePath, StringComparison.Ordinal));

        if (existing >= 0)
        {
            _curves[existing] = curve;
        }
        else
        {
            _curves.Add(curve);
        }
    }

    /// <summary>Adds an event, keeping the list in time order.</summary>
    public void AddEvent(AnimationEvent animationEvent)
    {
        ArgumentNullException.ThrowIfNull(animationEvent);

        if (!float.IsFinite(animationEvent.Time))
        {
            return;
        }

        int index = _events.FindIndex(existing => existing.Time > animationEvent.Time);
        _events.Insert(index < 0 ? _events.Count : index, animationEvent);
    }

    /// <summary>
    /// Folds a time into the clip's <c>0..Length</c> range the way <see cref="WrapMode"/> asks, so a caller can ask
    /// for time 7.5 of a 1-second loop and get the frame it should actually draw.
    /// </summary>
    /// <param name="time">Seconds, possibly negative or past the end.</param>
    /// <returns>A time within <c>0..Length</c>.</returns>
    public float WrapTime(float time)
    {
        float length = Length;

        if (length <= 0f || !float.IsFinite(time))
        {
            return 0f;
        }

        switch (WrapMode)
        {
            case AnimationWrapMode.Once:
            case AnimationWrapMode.ClampForever:
                return Math.Clamp(time, 0f, length);

            case AnimationWrapMode.Loop:
                return PositiveModulo(time, length);

            case AnimationWrapMode.PingPong:
            {
                float doubled = length * 2f;
                float folded = PositiveModulo(time, doubled);

                return folded <= length ? folded : doubled - folded;
            }

            default:
                return Math.Clamp(time, 0f, length);
        }
    }

    /// <summary>A modulo that is always in <c>0..length</c>, unlike <c>%</c>, which keeps a negative sign.</summary>
    private static float PositiveModulo(float value, float length)
    {
        float remainder = value % length;

        return remainder < 0f ? remainder + length : remainder;
    }

    /// <summary>The last key time across every curve, falling back to an instant when there are no keys.</summary>
    private float MeasureLength()
    {
        float longest = 0f;

        foreach (AnimationCurve curve in _curves)
        {
            if (curve.Keys.Count > 0 && curve.EndTime > longest)
            {
                longest = curve.EndTime;
            }
        }

        return longest;
    }
}

/// <summary>
/// A callback at a time in a clip — Unity's <c>AnimationEvent</c>. The function name is looked up on the scripts of
/// the object the clip drives when its time is crossed.
/// </summary>
public class AnimationEvent
{
    public AnimationEvent(string function, float time)
    {
        Function = function ?? string.Empty;
        Time = time;
    }

    /// <summary>The method to call: a name on a <see cref="Overcore.Components.Script"/> attached to the object.</summary>
    public string Function { get; set; }

    /// <summary>Seconds from the clip's start.</summary>
    public float Time { get; set; }

    /// <summary>Passed to a method that takes a string.</summary>
    public string StringParameter { get; set; } = string.Empty;

    /// <summary>Passed to a method that takes an int.</summary>
    public int IntParameter { get; set; }

    /// <summary>Passed to a method that takes a float.</summary>
    public float FloatParameter { get; set; }
}
