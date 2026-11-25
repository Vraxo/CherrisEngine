namespace OvercoreEditorSession;

/// <summary>
/// A shape to draw in a preview, in the preview's own units (the SVG's viewBox), with Y running down the
/// picture the way a top-down map does.
/// </summary>
/// <param name="Kind">What to draw it as.</param>
/// <param name="Name">The object's name, for tooltips.</param>
/// <param name="X">Centre X.</param>
/// <param name="Y">Centre Y.</param>
/// <param name="Width">Width along X.</param>
/// <param name="Height">Height along Y.</param>
/// <param name="AngleDegrees">
/// How far to turn the shape about its centre, clockwise, already in screen terms: a camera marker ends up
/// pointing where the camera looks, and a mesh footprint is turned the way its object is turned.
/// </param>
public sealed record ThumbnailShape(
    ThumbnailObjectKind Kind,
    string Name,
    float X,
    float Y,
    float Width,
    float Height,
    float AngleDegrees);

/// <summary>
/// Turns a <see cref="SceneThumbnail"/> into drawable shapes that fit a given box.
/// </summary>
/// <remarks>
/// The view is top-down with world +X to the right and world +Z down the picture, so what the user sees is the
/// scene as it is laid out on the floor, scaled to fit. One scale for both axes keeps shapes square: a wide flat
/// level looks wide, not stretched.
/// </remarks>
public static class SceneThumbnailLayout
{
    /// <summary>How big a light or a camera marker is drawn, in preview units.</summary>
    public const float MarkerSize = 9f;

    /// <summary>The smallest a mesh footprint is drawn, so a scene of tiny objects is still legible.</summary>
    public const float MinimumFootprint = 5f;

    /// <summary>Margin left inside the box, in preview units.</summary>
    public const float Padding = 5f;

    /// <summary>
    /// Lays the scene out in a <paramref name="width"/> × <paramref name="height"/> box, centred, with the same
    /// scale on both axes. An empty scene has nothing to draw.
    /// </summary>
    public static IReadOnlyList<ThumbnailShape> Fit(SceneThumbnail thumbnail, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);

        if (thumbnail.Objects.Count == 0 || width <= 0f || height <= 0f)
        {
            return [];
        }

        (float minimumX, float minimumZ, float maximumX, float maximumZ) = Bounds(thumbnail);

        float worldWidth = maximumX - minimumX;
        float worldDepth = maximumZ - minimumZ;

        // A single object, or a stack of them, has no extent of its own; a unit square keeps the scale finite.
        if (worldWidth < 0.001f)
        {
            worldWidth = 1f;
            minimumX -= 0.5f;
        }

        if (worldDepth < 0.001f)
        {
            worldDepth = 1f;
            minimumZ -= 0.5f;
        }

        float usableWidth = MathF.Max(width - (2f * Padding), 1f);
        float usableDepth = MathF.Max(height - (2f * Padding), 1f);
        float scale = MathF.Min(usableWidth / worldWidth, usableDepth / worldDepth);

        // Centre whatever the fit leaves over, so a wide level sits in the middle of a square tile.
        float offsetX = (width - (worldWidth * scale)) / 2f;
        float offsetY = (height - (worldDepth * scale)) / 2f;

        List<ThumbnailShape> shapes = [];

        foreach (ThumbnailObject item in thumbnail.Objects)
        {
            float centreX = offsetX + ((item.X - minimumX) * scale);
            float centreY = offsetY + ((item.Z - minimumZ) * scale);

            float angle = ScreenAngle(item);

            bool measured = item.Kind == ThumbnailObjectKind.Mesh;

            float shapeWidth = measured ? MathF.Max(item.Width * scale, MinimumFootprint) : MarkerSize;
            float shapeHeight = measured ? MathF.Max(item.Depth * scale, MinimumFootprint) : MarkerSize;

            shapes.Add(new ThumbnailShape(
                item.Kind,
                item.Name,
                Clamp(centreX, shapeWidth / 2f, width - (shapeWidth / 2f)),
                Clamp(centreY, shapeHeight / 2f, height - (shapeHeight / 2f)),
                shapeWidth,
                shapeHeight,
                angle));
        }

        return shapes;
    }

    /// <summary>
    /// The rotation to draw an object with. World X is screen X and world Z is screen Y, so a turn around Y shows
    /// up as a turn the other way round the picture; a camera's marker is turned to its forward axis, which the
    /// engine defines as -Z turned by the heading.
    /// </summary>
    private static float ScreenAngle(ThumbnailObject item)
    {
        const float RadiansToDegrees = 180f / MathF.PI;

        return item.Kind == ThumbnailObjectKind.Camera
            ? MathF.Atan2(-MathF.Cos(item.YawRadians), -MathF.Sin(item.YawRadians)) * RadiansToDegrees
            : -item.YawRadians * RadiansToDegrees;
    }

    /// <summary>Every object's footprint, or its position when it is a marker.</summary>
    private static (float MinimumX, float MinimumZ, float MaximumX, float MaximumZ) Bounds(SceneThumbnail thumbnail)
    {
        float minimumX = float.MaxValue;
        float minimumZ = float.MaxValue;
        float maximumX = float.MinValue;
        float maximumZ = float.MinValue;

        foreach (ThumbnailObject item in thumbnail.Objects)
        {
            float halfWidth = item.Kind == ThumbnailObjectKind.Mesh ? item.Width / 2f : 0f;
            float halfDepth = item.Kind == ThumbnailObjectKind.Mesh ? item.Depth / 2f : 0f;

            minimumX = MathF.Min(minimumX, item.X - halfWidth);
            maximumX = MathF.Max(maximumX, item.X + halfWidth);
            minimumZ = MathF.Min(minimumZ, item.Z - halfDepth);
            maximumZ = MathF.Max(maximumZ, item.Z + halfDepth);
        }

        return (minimumX, minimumZ, maximumX, maximumZ);
    }

    // Keeps a shape inside the box when the clamp range is inverted (a shape wider than the tile).
    private static float Clamp(float value, float minimum, float maximum) =>
        minimum > maximum ? (minimum + maximum) / 2f : Math.Clamp(value, minimum, maximum);
}
