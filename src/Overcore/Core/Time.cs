using Overcore.Core.Physics;
using System.Diagnostics;

namespace Overcore.Core;

/// <summary>
/// The clock every script reads, Unity's <c>Time</c>: how long the last frame took, how far the clock has
/// come, and the scale applied to both.
/// </summary>
/// <remarks>
/// <para>
/// The frame loop advances it once per frame (<see cref="Advance"/> is called by
/// <see cref="SceneManager.Update(float, bool)"/>), and the physics world advances <see cref="FixedTime"/>
/// once per fixed step. A scene driven by hand — a test calling <c>Scene.Update</c> directly — leaves it alone,
/// which is why scripts should read the delta they are given rather than the clock.
/// </para>
/// <para>
/// <see cref="TimeScale"/> slows the whole world down without changing how often frames are drawn: a scale of
/// <c>0.5</c> halves <see cref="DeltaTime"/>, and so halves the speed of everything that moves by it. A scale of
/// zero freezes the world — frames still run, deltas are zero, and physics stops stepping.
/// </para>
/// </remarks>
public static class Time
{
    // Real time since the process started, for code that wants to move at wall-clock speed whatever the scale.
    private static readonly Stopwatch WallClock = Stopwatch.StartNew();

    // The fixed step used while no physics world exists to say otherwise.
    private static float _fixedDeltaTime = 1.0f / 60.0f;

    /// <summary>Seconds the last frame took, already scaled by <see cref="TimeScale"/>.</summary>
    public static float DeltaTime { get; private set; }

    /// <summary>Seconds the last frame really took, whatever the scale.</summary>
    public static float UnscaledDeltaTime { get; private set; }

    /// <summary>
    /// The step physics advances by, in seconds. Its owner is the scene's <see cref="PhysicsSystem"/>
    /// (<see cref="PhysicsSystem.FixedTimeStep"/>); with no world open it is the 1/60 s default.
    /// </summary>
    public static float FixedDeltaTime
    {
        get => PhysicsSystem.Active?.FixedTimeStep ?? _fixedDeltaTime;
        set
        {
            value = MathF.Max(value, 1.0e-4f);
            _fixedDeltaTime = value;

            if (PhysicsSystem.Active is { } physics)
            {
                physics.FixedTimeStep = value;
            }
        }
    }

    /// <summary>How fast time runs: 1 is normal speed, 0.5 is half, 0 is a freeze frame.</summary>
    public static float TimeScale { get; set; } = 1.0f;

    /// <summary>Scaled seconds since the clock started — Unity's <c>Time.time</c>.</summary>
    public static float ElapsedTime { get; private set; }

    /// <summary>Unscaled seconds since the clock started.</summary>
    public static float UnscaledElapsedTime { get; private set; }

    /// <summary>Scaled seconds of simulated physics — Unity's <c>Time.fixedTime</c>.</summary>
    public static float FixedTime { get; private set; }

    /// <summary>Frames the loop has drawn — Unity's <c>Time.frameCount</c>.</summary>
    public static int FrameCount { get; private set; }

    /// <summary>Seconds since the process started, unaffected by pauses and scaling.</summary>
    public static float RealtimeSinceStartup => (float)WallClock.Elapsed.TotalSeconds;

    /// <summary>
    /// The longest frame the loop will believe in, in seconds: a hitch (a breakpoint, a stall) is clamped to it so
    /// the world does not try to catch up in one enormous step. Zero turns the clamp off.
    /// </summary>
    public static float MaximumDeltaTime { get; set; } = 0.25f;

    /// <summary>
    /// Advances the clock one frame and returns the scaled delta the world should move by. The frame loop calls
    /// this once per frame; it is public because a host that drives the world itself has to call it too.
    /// </summary>
    /// <param name="unscaledDeltaTime">Seconds the frame really took.</param>
    /// <returns>The same delta scaled by <see cref="TimeScale"/>, clamped by <see cref="MaximumDeltaTime"/>.</returns>
    public static float Advance(float unscaledDeltaTime)
    {
        if (unscaledDeltaTime < 0.0f || float.IsNaN(unscaledDeltaTime))
        {
            unscaledDeltaTime = 0.0f;
        }

        if (MaximumDeltaTime > 0.0f && unscaledDeltaTime > MaximumDeltaTime)
        {
            unscaledDeltaTime = MaximumDeltaTime;
        }

        UnscaledDeltaTime = unscaledDeltaTime;
        UnscaledElapsedTime += unscaledDeltaTime;

        DeltaTime = unscaledDeltaTime * MathF.Max(TimeScale, 0.0f);
        ElapsedTime += DeltaTime;
        FrameCount++;

        return DeltaTime;
    }

    /// <summary>Adds one fixed step to <see cref="FixedTime"/>. The physics world calls this per step.</summary>
    internal static void AdvanceFixed(float step) => FixedTime += MathF.Max(step, 0.0f);

    /// <summary>Puts the clock back to zero. The runtime calls this when it starts, and tests between cases.</summary>
    public static void Reset()
    {
        DeltaTime = 0.0f;
        UnscaledDeltaTime = 0.0f;
        ElapsedTime = 0.0f;
        UnscaledElapsedTime = 0.0f;
        FixedTime = 0.0f;
        FrameCount = 0;
        TimeScale = 1.0f;
    }
}
