using Overcore.Terrain;

namespace OvercoreEditor.UI.Viewport;

/// <summary>
/// The terrain brush as the editor holds it between the inspector and the viewport: whether sculpting is
/// armed, what the stroke does, and how big, hard and (for painting) what colour it is.
/// </summary>
/// <remarks>
/// Kept on the <see cref="Editor"/> rather than in either panel, because the two panels that use it — the
/// inspector that edits it and the viewport that strokes with it — are built by different code and meet
/// only there. It is editor state, not scene state: it is never serialised, and arming the brush does not
/// dirty the scene.
/// </remarks>
public sealed class TerrainBrushSettings
{
    /// <summary>True while the viewport sculpts the selected terrain instead of selecting and gizmo-moving it.</summary>
    public bool Enabled { get; set; }

    /// <summary>What a stroke does. <see cref="TerrainBrushMode.Paint"/> paints <see cref="LayerIndex"/>.</summary>
    public TerrainBrushMode Mode { get; set; } = TerrainBrushMode.Raise;

    /// <summary>The stroke's radius in world metres, converted to the terrain's normalised radius when stroked.</summary>
    public float RadiusMetres { get; set; } = 10f;

    /// <summary>
    /// How hard the brush works: metres per second of lift for Raise/Lower, and the pull rate of the
    /// Flatten, Smooth and Paint strokes.
    /// </summary>
    public float Strength { get; set; } = 3f;

    /// <summary>The height a Flatten stroke pulls toward, in metres.</summary>
    public float FlattenHeight { get; set; }

    /// <summary>The layer a Paint stroke paints with, clamped to the terrain's layers when stroked.</summary>
    public int LayerIndex { get; set; }

    /// <summary>
    /// The strength one frame's worth of a held stroke applies: continuous modes are rates, so the frame's
    /// slice of a second is the unit. Flatten, Smooth and Paint pull toward a target, so their slice is a
    /// fraction, clamped to arrive but never overshoot.
    /// </summary>
    /// <param name="deltaTime">Seconds since the previous stroke application.</param>
    public float StrokeStrength(float deltaTime)
    {
        float slice = Math.Clamp(deltaTime, 0f, 0.1f);
        return Mode is TerrainBrushMode.Raise or TerrainBrushMode.Lower
            ? Strength * slice
            : Math.Clamp(Strength * slice, 0f, 1f);
    }
}
