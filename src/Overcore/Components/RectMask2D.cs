namespace Overcore.Components;

/// <summary>Clips descendant UI graphics to this object's resolved rectangle.</summary>
/// <remarks>
/// This first-pass rectangular mask uses the renderer's scissor test. It clips child graphics and pointer hit-tests;
/// it does not mask this object's own graphic, use texture alpha, or create rounded/soft edges.
/// </remarks>
public sealed class RectMask2D : Component
{
}
