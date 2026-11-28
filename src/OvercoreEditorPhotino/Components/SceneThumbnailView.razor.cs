using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Overcore.Projects;
using OvercoreEditorPhotino;
using OvercoreEditorPhotino.Components;
using OvercoreEditorPhotino.Shared;
using OvercoreEditorPhotino.Services;
using OvercoreEditorSession;

namespace OvercoreEditorPhotino.Components;

/// <summary>
/// Code-behind for <see cref="SceneThumbnailView"/>: the scene preview image and its placeholder.
/// The markup lives in <c>SceneThumbnailView.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class SceneThumbnailView
{
    /// <summary>The scene to draw.</summary>
    [Parameter, EditorRequired]
    public SceneThumbnail Thumbnail { get; set; } = SceneThumbnail.Empty;

    /// <summary>Width of the picture, in its own units; the CSS decides how big it lands on screen.</summary>
    [Parameter]
    public float Width { get; set; } = 76f;

    /// <summary>Height of the picture, in its own units.</summary>
    [Parameter]
    public float Height { get; set; } = 46f;

    private IReadOnlyList<ThumbnailShape> _shapes = [];

    protected override void OnParametersSet()
    {
        _shapes = SceneThumbnailLayout.Fit(Thumbnail, Width, Height);
    }

    // Numbers travel into SVG as text, so they are formatted once here, invariantly and without noise.
    private static string Units(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Turn(ThumbnailShape shape) =>
        MathF.Abs(shape.AngleDegrees) < 0.5f
            ? string.Empty
            : $"rotate({Units(shape.AngleDegrees)} {Units(shape.X)} {Units(shape.Y)})";

    /// <summary>A triangle pointing along +X in its own frame, turned to the camera's forward axis.</summary>
    private static string CameraPoints(ThumbnailShape shape)
    {
        float radius = shape.Width / 2f;
        float radians = shape.AngleDegrees * (MathF.PI / 180f);
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);

        Vector2[] corners =
        [
            new(radius, 0f),
            new(-radius * 0.6f, radius * 0.82f),
            new(-radius * 0.6f, -radius * 0.82f)
        ];

        return string.Join(' ', corners.Select(corner =>
        {
            float x = corner.X * cosine - (corner.Y * sine);
            float y = (corner.X * sine) + (corner.Y * cosine);

            return $"{Units(shape.X + x)},{Units(shape.Y + y)}";
        }));
    }
}
