using Overcore.Components;

namespace Overcore.Rendering.Shadows;

/// <summary>One slice of the camera's view that a single shadow map covers: the depths from <paramref name="Near"/>
/// to <paramref name="Far"/>, in world units along the camera's forward axis.</summary>
/// <remarks>
/// The ranges of a cascade set are contiguous and in order — one cascade's <see cref="Far"/> is the next one's
/// <see cref="Near"/> — which is what lets the receiver pick a cascade by comparing one depth against
/// <see cref="CascadeSplits.FarDepths"/> and nothing else.
/// </remarks>
public readonly record struct CascadeRange(float Near, float Far)
{
    /// <summary>How deep the slice is. A slice with no depth can be neither fitted nor drawn.</summary>
    public float Width => Far - Near;
}

/// <summary>
/// Where a directional light's shadow cascades begin and end, as Unity's quality settings define them.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the one Unity ships in its own <c>QualitySettings.asset</c>: cascade <c>i</c> of <c>n</c> ends at
/// <c>(2^i - 1) / (2^n - 1)</c> of the shadow distance, so four cascades end at 1/15, 3/15 and 7/15 — 0.0667, 0.2 and
/// 0.4667 — and two end at 1/3. Each cascade is exactly twice as deep as the one inside it. A cascade twice as deep
/// covers twice the volume at the same texel count, so its texel density is half, which is why the splits halve: the
/// eye stops being able to tell at about the same rate.
/// </para>
/// <para>
/// Everything here is arithmetic over floats with no engine state and no GPU, which is what makes it exact testable —
/// see <c>docs/shadows.md</c> §3 and §9. The renderer is the only thing that reads it, and it reads it once a frame.
/// </para>
/// </remarks>
public static class CascadeSplits
{
    /// <summary>The cascade count a scene gets without asking for one. Unity's default quality level uses four.</summary>
    public const int DefaultCascades = 4;

    /// <summary>The shadow distance a scene gets without asking for one, in world units. Unity's default is 150.</summary>
    public const float DefaultDistance = 150f;

    /// <summary>The deepest shadow distance the engine will accept. Longer than any scene this renders.</summary>
    public const float MaximumDistance = 5000f;

    /// <summary>The shortest shadow distance that is still a shadow, in world units.</summary>
    public const float MinimumDistance = 1f;

    /// <summary>Four, because a fixed <c>mat4[4]</c> of light matrices is what the receiver's shader declares.</summary>
    public const int MaximumCascades = 4;

    /// <summary>The smallest gap two consecutive splits may have, so a boundary can never be a division by zero.</summary>
    public const float MinimumGap = 0.001f;

    /// <summary>
    /// The cascade count a requested one becomes. Only 1, 2 and 4 are real — a split formula that divides a view into
    /// three gives one wide cascade and one narrow one and no benefit — so anything else rounds to the nearest of the
    /// three, a tie going to the larger, and anything out of range clamps.
    /// </summary>
    public static int SupportedCascades(int requested) => requested switch
    {
        <= 1 => 1,
        2 => 2,
        3 => 4,
        _ => 4
    };

    /// <summary>
    /// The default cumulative boundaries for <paramref name="cascades"/> cascades: <c>n - 1</c> values, each a fraction
    /// of the shadow distance, strictly increasing and below 1.
    /// </summary>
    /// <returns>
    /// An empty array for one cascade, <c>{ 1/3 }</c> for two, and <c>{ 1/15, 3/15, 7/15 }</c> for four.
    /// </returns>
    public static float[] Derive(int cascades)
    {
        int count = SupportedCascades(cascades);

        if (count == 1)
        {
            return [];
        }

        float[] boundaries = new float[count - 1];

        // (2^i - 1) / (2^n - 1) for i = 1..n-1. Written out rather than as a power so the arithmetic is exactly the
        // published fractions: 1/15 is a float, and Math.Pow(2, 4) - 1 == 15 is not something to stake a split on.
        float denominator = (1 << count) - 1;

        for (int i = 1; i < count; i++)
        {
            boundaries[i - 1] = ((1 << i) - 1) / denominator;
        }

        return boundaries;
    }

    /// <summary>
    /// A usable split list for <paramref name="cascades"/> cascades, repaired rather than trusted.
    /// </summary>
    /// <param name="splits">
    /// What a scene file said, or null. A list of the wrong length — including the empty list for a cascaded setup —
    /// is discarded and the derived defaults are used, because a hand edit that disagrees with the cascade count is
    /// a file that has not been thought through, not one whose numbers should be honoured.
    /// </param>
    /// <param name="cascades">The cascade count, already through <see cref="SupportedCascades"/>.</param>
    /// <param name="repaired">
    /// True when the list handed in was discarded or had a value clamped, so a caller can log it once.
    /// </param>
    public static float[] Sanitize(IReadOnlyList<float>? splits, int cascades, out bool repaired)
    {
        int count = SupportedCascades(cascades);
        repaired = false;

        float[] derived = Derive(count);

        if (count == 1)
        {
            repaired = splits is { Count: > 0 };
            return derived;
        }

        if (splits is null || splits.Count != derived.Length)
        {
            repaired = splits is not null;
            return derived;
        }

        float[] result = new float[derived.Length];
        float previous = 0f;

        for (int i = 0; i < derived.Length; i++)
        {
            // Room for the boundaries still to come, so the last one cannot be pushed past 1 and leave no room for
            // itself: an out-of-order list collapses onto the last legal position rather than wrapping.
            float highest = 1f - MinimumGap * (derived.Length - i);
            float value = splits[i];

            if (!float.IsFinite(value))
            {
                value = previous + MinimumGap;
            }

            float clamped = Math.Clamp(value, previous + MinimumGap, highest);

            if (clamped != value)
            {
                repaired = true;
            }

            result[i] = clamped;
            previous = clamped;
        }

        return result;
    }

    /// <summary>
    /// Where each cascade ends, in world units of camera view depth — what the receiver compares a fragment's depth
    /// against, and what the renderer fits each light-space box to.
    /// </summary>
    /// <param name="distance">The shadow distance. Clamped to <see cref="MinimumDistance"/>..<see cref="MaximumDistance"/>.</param>
    /// <param name="cascades">The cascade count; see <see cref="SupportedCascades"/>.</param>
    /// <param name="splits">Hand-tuned boundaries, or null for the derived ones.</param>
    /// <returns>One value per cascade, the last of which is always the distance itself.</returns>
    public static float[] FarDepths(
        float distance,
        int cascades = DefaultCascades,
        IReadOnlyList<float>? splits = null)
    {
        int count = SupportedCascades(cascades);
        float shadowDistance = ClampDistance(distance);

        if (count == 1)
        {
            return [shadowDistance];
        }

        float[] boundaries = Sanitize(splits, count, out _);
        float[] depths = new float[count];

        for (int i = 0; i < count - 1; i++)
        {
            depths[i] = shadowDistance * boundaries[i];
        }

        depths[count - 1] = shadowDistance;

        return depths;
    }

    /// <summary>
    /// The cascades themselves: contiguous depth slices from the camera's near plane out to the shadow distance.
    /// </summary>
    /// <param name="distance">The shadow distance.</param>
    /// <param name="cameraNearPlane">The camera's own near plane, where the first cascade begins.</param>
    /// <param name="cascades">The cascade count; see <see cref="SupportedCascades"/>.</param>
    /// <param name="splits">Hand-tuned boundaries, or null for the derived ones.</param>
    /// <remarks>
    /// A camera near plane deeper than the first cascade's far plane would leave that cascade with no depth at all, so
    /// the near plane is pulled in to a tenth of the innermost split instead. Every slice therefore has a positive
    /// width, which is the property the fit depends on and the one thing here that is not a straight copy of Unity:
    /// Unity clamps the near plane too, but it can put the first cascade's start anywhere because its splits are in
    /// space, not depth.
    /// </remarks>
    public static CascadeRange[] Ranges(
        float distance,
        float cameraNearPlane,
        int cascades = DefaultCascades,
        IReadOnlyList<float>? splits = null)
    {
        float[] farDepths = FarDepths(distance, cascades, splits);

        float near = float.IsFinite(cameraNearPlane) && cameraNearPlane > 0f
            ? cameraNearPlane
            : Camera.MinimumNearClipPlane;

        // A tenth of the innermost boundary leaves that cascade nine tenths of its depth, and every later cascade
        // untouched.
        float deepestNear = farDepths[0] * 0.1f;

        if (near > deepestNear)
        {
            near = deepestNear;
        }

        CascadeRange[] ranges = new CascadeRange[farDepths.Length];

        for (int i = 0; i < farDepths.Length; i++)
        {
            ranges[i] = new CascadeRange(MathF.Min(near, farDepths[i] * 0.5f), farDepths[i]);
            near = farDepths[i];
        }

        return ranges;
    }

    private static float ClampDistance(float distance)
    {
        return float.IsFinite(distance)
            ? Math.Clamp(distance, MinimumDistance, MaximumDistance)
            : DefaultDistance;
    }
}
