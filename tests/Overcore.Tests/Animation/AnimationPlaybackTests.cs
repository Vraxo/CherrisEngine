using AwesomeAssertions;
using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Core.Physics;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// The <see cref="AnimationPlayer"/> component and the event dispatcher: the clock, the pose written onto a real hierarchy,
/// and the events a frame crosses. A scene is not needed — an object and a clip are.
/// </summary>
public class AnimationPlaybackTests : IDisposable
{
    // One scene, one physics system, one audio system per test — xUnit builds a fresh instance for each. They are
    // fields rather than locals in the helper because the scene keeps the physics system for its fixed steps: a
    // system disposed when the helper returned would fail the first frame that touched it.
    private readonly PhysicsSystem _physics = new();
    private readonly AudioSystem _audio = new();

    [Fact]
    public void Advance_MovesTheClock_AndWritesThePoseOntoTheHierarchy()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 2f));

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip });

        animation.Advance(0.5f);

        animation.Time.Should().BeApproximately(0.5f, 1e-5f);
        hips.Transform.Position.Y.Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void Advance_WhilePaused_StillAppliesThePose()
    {
        // A paused clock is how a scrubber works: the pose has to follow Time even though Time is not moving.
        GameObject root = Hierarchy("Hips", out GameObject hips);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 2f));

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip, IsPlaying = false });

        animation.Time = 0.75f;
        animation.Advance(10f);

        // The clock did not move, and the pose is the one for the time it is sitting at.
        animation.Time.Should().Be(0.75f);
        hips.Transform.Position.Y.Should().BeApproximately(1.5f, 1e-5f);
    }

    [Fact]
    public void Advance_WithALoopingClip_WrapsTheClock()
    {
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));
        clip.WrapMode = AnimationWrapMode.Loop;

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip });

        animation.Advance(1.25f);

        // A quarter of the way into the second lap, not 1.25 seconds into a one second clip.
        animation.Time.Should().BeApproximately(0.25f, 1e-5f);
    }

    [Fact]
    public void Advance_WithAnOverride_UsesItInsteadOfTheClipsOwnWrapMode()
    {
        // The same clip can be told to hold instead of loop by the object playing it, which is what the override is.
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));
        clip.WrapMode = AnimationWrapMode.Loop;

        AnimationPlayer animation = root.AddComponent(new Animation
        {
            Clip = clip,
            WrapModeOverride = AnimationWrapMode.ClampForever
        });

        animation.Advance(3f);

        animation.Time.Should().Be(1f);
        animation.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void Advance_WithAWeightOfZero_ChangesNothing()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);

        AnimationClip clip = HeightOf("Hips", (0f, 5f), (1f, 5f));

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip, Weight = 0f });

        animation.Advance(0.5f);

        hips.Transform.Position.Y.Should().Be(0f);
    }

    [Fact]
    public void Advance_BlendsThePoseIn_AtAPartialWeight()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);

        AnimationClip clip = HeightOf("Hips", (0f, 4f), (1f, 4f));

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip, Weight = 0.5f });

        animation.Advance(0.5f);

        // Half of the four units the clip asks for, from where the transform already was.
        hips.Transform.Position.Y.Should().BeApproximately(2f, 1e-5f);
    }

    [Fact]
    public void Advance_WithNoClip_DoesNothingRatherThanThrowing()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer());

        animation.Invoking(a => a.Advance(1f)).Should().NotThrow();
        hips.Transform.Position.Y.Should().Be(0f);
    }

    [Fact]
    public void Advance_WithTheComponentDisabled_DoesNothing()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);

        AnimationClip clip = HeightOf("Hips", (0f, 9f), (1f, 9f));

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip });
        animation.Enabled = false;

        animation.Advance(0.5f);

        hips.Transform.Position.Y.Should().Be(0f);
    }

    [Fact]
    public void Advance_CarriesTheClockToTheNextFrame()
    {
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (2f, 2f));
        clip.WrapMode = AnimationWrapMode.ClampForever;

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip });

        for (int frame = 0; frame < 4; frame++)
        {
            animation.Advance(0.25f);
        }

        animation.Time.Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void Play_StartsAClipFromTheBeginning()
    {
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip, Time = 0.9f });

        animation.Play(clip);

        animation.Time.Should().Be(0f);
        animation.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void PlayAt_ScrubsWithoutFiringEvents()
    {
        // A scrub is not playback: dragging a slider past a footstep must not fire it.
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));
        clip.AddEvent(new AnimationEvent(nameof(Recorder.Mark), 0.5f));

        Recorder recorder = root.AddComponent(new Recorder());

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip });

        animation.PlayAt(0.9f);

        recorder.Count.Should().Be(0);
        animation.Time.Should().Be(0.9f);
    }

    [Fact]
    public void SampleAnimation_AppliesAClipInOneCall_WithNoClock()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 2f));

        clip.SampleAnimation(root, 0.5f);

        hips.Transform.Position.Y.Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void SampleAnimation_LeavesBonesTheClipDoesNotAddressWhereTheyWere()
    {
        GameObject root = Hierarchy("Hips", out GameObject hips);

        GameObject arm = new("Arm");
        arm.Transform.Parent = root.Transform;
        arm.Transform.Position = new Vector3(3f, 3f, 3f);

        AnimationClip clip = HeightOf("Hips", (0f, 1f), (1f, 1f));

        clip.SampleAnimation(root, 0.5f);

        hips.Transform.Position.Y.Should().Be(1f);
        arm.Transform.Position.Should().Be(new Vector3(3f, 3f, 3f));
    }

    // ---------------------------------------------------------------- events

    [Fact]
    public void Fire_AcrossAFrameThatCrossesThreeEvents_FiresAllThreeInOrder()
    {
        // A long frame fires every event it crossed, once each, in ascending time order — not just the nearest one.
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));
        clip.AddEvent(new AnimationEvent(nameof(Recorder.Mark), 0.2f));
        clip.AddEvent(new AnimationEvent(nameof(Recorder.Mark), 0.5f));
        clip.AddEvent(new AnimationEvent(nameof(Recorder.Mark), 0.8f));

        Recorder recorder = root.AddComponent(new Recorder());

        clip.Events.Should().HaveCount(3);

        AnimationEventDispatch dispatch = new();
        dispatch.Fire(clip, root, 0f, 1f, wrapped: false);

        recorder.Count.Should().Be(3);
    }

    [Fact]
    public void Fire_DoesNotFireTheSameEventTwice_AsThePlayheadSitsOnIt()
    {
        // Half-open intervals: an event at 0.5 fires on the frame that arrives there and not again while the
        // playhead stays past it.
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));
        clip.AddEvent(new AnimationEvent(nameof(Recorder.Mark), 0.5f));

        Recorder recorder = root.AddComponent(new Recorder());

        AnimationEventDispatch dispatch = new();

        dispatch.Fire(clip, root, 0.4f, 0.6f, wrapped: false);
        dispatch.Fire(clip, root, 0.6f, 0.7f, wrapped: false);
        dispatch.Fire(clip, root, 0.7f, 0.9f, wrapped: false);

        recorder.Count.Should().Be(1);
    }

    [Fact]
    public void Fire_WhenTheClockWrapped_FiresTheEventsOnBothSidesOfTheWrap()
    {
        // The frame crossed the end of the clip and started again: events before the end and after zero both belong
        // to it, or a looping footstep would be missed once per lap.
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));
        clip.AddEvent(new AnimationEvent(nameof(Recorder.Mark), 0.9f));
        clip.AddEvent(new AnimationEvent(nameof(Recorder.Mark), 0.1f));

        Recorder recorder = root.AddComponent(new Recorder());

        AnimationEventDispatch dispatch = new();
        dispatch.Fire(clip, root, 0.8f, 0.2f, wrapped: true);

        recorder.Count.Should().Be(2);
    }

    [Fact]
    public void Fire_PassesTheEventsPayLoadToTheMethod()
    {
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));

        AnimationEvent animationEvent = new(nameof(Recorder.Take), 0.5f) { StringParameter = "wood" };
        clip.AddEvent(animationEvent);

        Recorder recorder = root.AddComponent(new Recorder());

        new AnimationEventDispatch().Fire(clip, root, 0f, 1f, wrapped: false);

        recorder.Last.Should().Be("wood");
    }

    [Fact]
    public void Fire_WithAMissingMethod_LogsOnceAndDoesNotThrow()
    {
        GameObject root = Hierarchy("Hips", out _);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (1f, 1f));
        clip.AddEvent(new AnimationEvent("NoSuchMethod", 0.5f));

        root.AddComponent(new Recorder());

        AnimationEventDispatch dispatch = new();

        dispatch.Invoking(d => dispatch.Fire(clip, root, 0f, 1f, wrapped: false)).Should().NotThrow();
        dispatch.Fire(clip, root, 1f, 2f, wrapped: false);

        // One line in the log for the life of the play, not one per frame.
        Logger.Messages
            .Count(message => message.Message.Contains("NoSuchMethod", StringComparison.Ordinal))
            .Should()
            .Be(1);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A started scene whose root has one child, so a clip has a path to address. Components are attached afterwards
    /// because a component only becomes active — and so only advances — once it belongs to an object in a started
    /// scene, the same way <c>Scene.Start</c> is what wakes a scene's scripts.
    /// </summary>
    private GameObject Hierarchy(string childName, out GameObject child)
    {
        GameObject root = new("Root");

        child = new(childName);
        child.Transform.Parent = root.Transform;

        Start(root);

        return root;
    }

    /// <summary>Opens a scene around one object and starts it, which is what wakes every component on it.</summary>
    private void Start(GameObject root)
    {
        Scene scene = new("Test.scene", [root]);
        scene.Start(_physics, _audio);
    }

    public void Dispose()
    {
        _physics.Dispose();
        _audio.Dispose();
    }

    /// <summary>A clip driving one bone's height, with the given keys and no wrapping.</summary>
    private static AnimationClip HeightOf(string bonePath, params (float Time, float Value)[] keys)
    {
        AnimationCurve curve = new(bonePath, AnimationProperty.LocalPosition, AnimationAxis.Y);

        foreach ((float time, float value) in keys)
        {
            curve.AddKey(time, value);
        }

        AnimationClip clip = new("Height") { WrapMode = AnimationWrapMode.ClampForever };
        clip.AddCurve(curve);

        return clip;
    }
}

/// <summary>A script that records what an animation event did to it, the way a player's footstep handler would.</summary>
public class Recorder : Script
{
    public int Count { get; private set; }

    public string Last { get; private set; } = string.Empty;

    public void Mark() => Count++;

    public void Take(string value)
    {
        Last = value;
        Count++;
    }
}
