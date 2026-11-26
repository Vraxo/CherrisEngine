using Overcore.Animation;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Components;

/// <summary>
/// Plays one clip on one object: its clock, the pose the clip is sampled into, and the events it fires. This is
/// Unity's legacy <c>Animation</c> component — one clip, played directly, no state machine — and it is what the
/// <c>Animator</c> of the next increment will drive instead of replacing.
/// </summary>
/// <remarks>
/// <para>
/// It is called <c>AnimationPlayer</c> rather than Unity's <c>Animation</c> for a reason worth writing down: a type
/// named <c>Animation</c> is unreachable by its bare name from anywhere inside the engine's own namespaces. C# resolves
/// a simple name against the members of each enclosing namespace before it consults a <c>using</c> directive, and
/// <c>Overcore</c> already contains the namespace <c>Overcore.Animation</c> — so <c>Animation</c> means that namespace
/// in every file under <c>Overcore.*</c>, and a type of the same name can only be named by fully qualifying it. The
/// cost of the rename is one word; the cost of keeping it is a landmine in every file that ever refers to the type.
/// </para>
/// <para>
/// The rest of the API keeps Unity's names, so what a script sees is unchanged.
/// </para>
/// </remarks>
/// <remarks>
/// <para>
/// The component writes the pose onto the object's own hierarchy, so playing a clip needs no skeleton and no renderer:
/// a door that swings, a camera that flies a path and a character that walks are the same mechanism. A skeleton is
/// only involved when a mesh is skinned, and then the bone transforms this writes are exactly the ones the joint
/// palette is computed from.
/// </para>
/// <para>
/// A clip is sampled into a pose that starts as a copy of the transforms the hierarchy already holds, so a clip that
/// only drives a hand leaves the rest of the body where a script put it. That also means the component is not
/// additive: two <see cref="Animation"/> components driving the same bone fight, and the last one to be ticked wins —
/// the same as Unity.
/// </para>
/// </remarks>
public class AnimationPlayer : Component
{
    private AnimationPose _pose = new();
    private AnimationEventDispatch _events = new();
    private bool _useUnscaledTime;

    /// <summary>The clip to play, or null for a component that does nothing.</summary>
    public AnimationClip? Clip { get; set; }

    /// <summary>How much of the clip's motion reaches the transforms: one full, zero none, in between a blend.</summary>
    public float Weight { get; set; } = 1f;

    /// <summary>How much of the clip's rotation reaches the transforms, scaling <see cref="Weight"/>.</summary>
    /// <remarks>
    /// Kept separate because it is the one case a flat weight cannot express: a script fading a walk into a run wants
    /// the limbs' rotation to follow the fade while the object's own heading keeps coming from the script.
    /// </remarks>
    public float RotationWeight { get; set; } = 1f;

    /// <summary>Whether the clip's clock advances. A paused clock still applies the pose, so scrubbing works.</summary>
    public bool IsPlaying { get; set; } = true;

    /// <summary>Seconds into the clip, folded by its wrap mode once it is sampled.</summary>
    public float Time { get; set; }

    /// <summary>How the clock behaves outside the clip's length. Defaults to the clip's own wrap mode.</summary>
    public AnimationWrapMode? WrapModeOverride { get; set; }

    /// <summary>True to advance by unscaled time, for a pause screen or an editor preview.</summary>
    public bool UseUnscaledTime
    {
        get => _useUnscaledTime;
        set => _useUnscaledTime = value;
    }

    /// <summary>The pose the last sample produced. Kept so a renderer or an inspector can read it back.</summary>
    public AnimationPose Pose => _pose;

    /// <summary>Fires the clip's events. Per component, so a clip reused by two objects reports its own misses.</summary>
    public AnimationEventDispatch Events
    {
        get => _events;
        set => _events = value ?? new AnimationEventDispatch();
    }

    /// <summary>The effective wrap mode: the override when one is set, otherwise the clip's.</summary>
    public AnimationWrapMode WrapMode => WrapModeOverride ?? Clip?.WrapMode ?? AnimationWrapMode.Once;

    /// <summary>Normalized position in the clip, <c>0..1</c>, which is what a scrubbing slider wants.</summary>
    public float NormalizedTime
    {
        get
        {
            float length = Clip?.Length ?? 0f;

            return length <= 0f ? 0f : Math.Clamp(Time / length, 0f, 1f);
        }
        set
        {
            float length = Clip?.Length ?? 0f;

            Time = length <= 0f ? 0f : Math.Clamp(value, 0f, 1f) * length;
        }
    }

    /// <summary>True once a non-looping clip has reached its end and is holding its last frame.</summary>
    public bool IsFinished =>
        Clip is { } clip && WrapMode is AnimationWrapMode.Once or AnimationWrapMode.ClampForever &&
        Time >= clip.Length;

    /// <summary>
    /// Advances the clock and writes the pose. Called by the scene's animation step, between <c>Update</c> and
    /// <c>LateUpdate</c>, so a script that moves a bone in <c>Update</c> is overridden and one that reads the final
    /// pose in <c>LateUpdate</c> sees it.
    /// </summary>
    /// <param name="deltaTime">Seconds since the previous frame, already scaled by the caller.</param>
    public void Advance(float deltaTime)
    {
        AnimationClip? clip = Clip;

        if (clip is null || GameObject is null || !IsActive || !Enabled)
        {
            return;
        }

        float previousTime = Time;

        if (IsPlaying && deltaTime > 0f && float.IsFinite(deltaTime))
        {
            Time += deltaTime;
        }

        // Wrapping is the component's, not the clip's: a clip can be told to loop differently by the object playing
        // it, which is what the override exists for.
        AnimationWrapMode mode = WrapMode;
        float length = clip.Length;
        bool wrapped = false;

        if (length > 0f)
        {
            switch (mode)
            {
                case AnimationWrapMode.Loop:
                    wrapped = Time >= length;
                    Time = PositiveModulo(Time, length);
                    break;

                case AnimationWrapMode.PingPong:
                {
                    float doubled = length * 2f;
                    wrapped = Time >= doubled;
                    float folded = PositiveModulo(Time, doubled);
                    Time = folded <= length ? folded : doubled - folded;
                    break;
                }

                case AnimationWrapMode.Once:
                case AnimationWrapMode.ClampForever:
                    Time = Math.Clamp(Time, 0f, length);
                    break;
            }
        }

        Apply();

        // Events are fired after the pose is written, so a handler that reads the transform sees the frame it was
        // fired for rather than the one before it.
        if (IsPlaying)
        {
            _events.Fire(clip, GameObject, previousTime, Time, wrapped);
        }
    }

    /// <summary>
    /// Samples the clip at the current time and writes it onto the hierarchy. Public because an editor's scrubber
    /// needs to apply a pose without advancing a clock.
    /// </summary>
    public void Apply()
    {
        AnimationClip? clip = Clip;

        if (clip is null || GameObject is null)
        {
            return;
        }

        // Captured first so a clip that drives some of a bone's axes, or only some of the bones, leaves the rest of
        // the hierarchy exactly as it found it.
        _pose.CaptureFrom(GameObject.Transform);

        clip.SampleAt(Time, _pose, 1f);

        if (Weight <= 0f)
        {
            return;
        }

        if (Weight >= 1f && RotationWeight >= 1f)
        {
            _pose.ApplyTo(GameObject.Transform);
            return;
        }

        // A partial weight is applied by blending the sampled pose over a second capture of what the hierarchy held,
        // which is what keeps the untouched bones untouched.
        AnimationPose blended = new();
        blended.CaptureFrom(GameObject.Transform);
        blended.Blend(_pose, Math.Clamp(Weight, 0f, 1f));

        if (RotationWeight < 1f)
        {
            ApplyRotationScaled(blended, Math.Clamp(RotationWeight, 0f, 1f));
        }
        else
        {
            blended.ApplyTo(GameObject.Transform);
        }
    }

    /// <summary>
    /// Writes a pose with its rotations pulled toward the identity by a factor. Slerping toward the identity is what
    /// "half the rotation" means; a component-wise scale of a quaternion is not a rotation at all.
    /// </summary>
    private void ApplyRotationScaled(AnimationPose pose, float factor)
    {
        if (GameObject is null)
        {
            return;
        }

        foreach (string path in pose.BonePaths)
        {
            Transform? target = AnimationPose.Resolve(GameObject.Transform, path);

            if (target is null)
            {
                continue;
            }

            AnimationPose.BonePose bone = pose.Get(path);

            target.Position = bone.Position;
            target.Rotation = Quaternion.Slerp(Quaternion.Identity, bone.Rotation, factor);
            target.Scale = bone.Scale;
        }
    }

    /// <summary>
    /// Jumps to a time and applies the frame there, firing no events — the scrubber case. A scrub is not playback, so
    /// a footstep must not fire every time the slider passes it.
    /// </summary>
    public void PlayAt(float time)
    {
        Time = time;
        IsPlaying = false;

        Apply();
    }

    /// <summary>Starts a clip from the beginning.</summary>
    public void Play(AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        Clip = clip;
        Time = 0f;
        IsPlaying = true;
    }

    /// <summary>Restarts the current clip where it is.</summary>
    public void Rewind() => Time = 0f;

    /// <summary>A modulo that is always in <c>0..length</c>, unlike <c>%</c>, which keeps a negative sign.</summary>
    private static float PositiveModulo(float value, float length)
    {
        float remainder = value % length;

        return remainder < 0f ? remainder + length : remainder;
    }

    /// <summary>Gives the component a fresh pose and clock when an object is instantiated from a prefab.</summary>
    internal override void OnInstantiated()
    {
        base.OnInstantiated();

        _pose = new AnimationPose();
        _events = new AnimationEventDispatch();
        Time = 0f;
    }
}
