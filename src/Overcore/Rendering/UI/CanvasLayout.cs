using Overcore.Components;
using Overcore.Core;

namespace Overcore.Rendering.UI;

/// <summary>A resolved UI element, retaining the component references needed by drawing and hit-testing.</summary>
public sealed record UIElement(GameObject GameObject, Canvas Canvas, RectTransform RectTransform, RectF Rect,
    RectF? ClipRect, Image? Image, Text? Text, Button? Button, int DrawOrder, Toggle? Toggle = null, Slider? Slider = null,
    InputField? InputField = null, IReadOnlyList<RoundedRectClip>? RoundedClips = null);

/// <summary>Builds screen-space rectangles from active Canvas hierarchies.</summary>
public static class CanvasLayout
{
    public static IReadOnlyList<UIElement> Build(IEnumerable<GameObject> gameObjects, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(gameObjects);
        width = MathF.Max(1f, width);
        height = MathF.Max(1f, height);

        List<GameObject> objects = [.. gameObjects];
        List<(Canvas Canvas, GameObject Root)> canvases = [];
        foreach (GameObject gameObject in objects)
        {
            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            foreach (Canvas canvas in gameObject.GetComponents<Canvas>())
            {
                if (canvas.Enabled)
                {
                    canvases.Add((canvas, gameObject));
                }
            }
        }

        canvases = canvases.OrderBy(item => item.Canvas.SortingOrder).ToList();
        List<UIElement> result = [];
        int order = 0;

        foreach ((Canvas canvas, GameObject root) in canvases)
        {
            float scale = canvas.GetScale(width, height);
            RectF rootRect = new(0f, 0f, width / scale, height / scale);
            RectF viewportRect = new(0f, 0f, width, height);
            Visit(root, canvas, rootRect, viewportRect, Array.Empty<RoundedRectClip>(), true, scale, result, ref order);
        }

        return result;
    }

    private static void Visit(GameObject gameObject, Canvas canvas, RectF parentRect, RectF? inheritedClip,
        IReadOnlyList<RoundedRectClip> inheritedRoundedClips, bool isCanvasRoot, float scale,
        List<UIElement> output, ref int order, RectF? arrangedRect = null)
    {
        if (!gameObject.ActiveInHierarchy)
        {
            return;
        }

        RectTransform? rectTransform = gameObject.GetComponent<RectTransform>();
        if (rectTransform is null || !rectTransform.Enabled)
        {
            return;
        }

        if (inheritedClip.HasValue && inheritedClip.Value.IsEmpty)
        {
            return;
        }

        RectF logicalRect = isCanvasRoot ? parentRect : arrangedRect ?? rectTransform.Resolve(parentRect);
        RectF resolved = Scale(logicalRect, scale);
        RectF? childClip = inheritedClip;
        IReadOnlyList<RoundedRectClip> childRoundedClips = inheritedRoundedClips;
        if (gameObject.GetComponent<RectMask2D>() is { Enabled: true } mask)
        {
            childClip = inheritedClip is { } parentClip
                ? parentClip.Intersect(resolved)
                : resolved;
            float radius = float.IsFinite(mask.CornerRadius) ? MathF.Max(0f, mask.CornerRadius) * scale : 0f;
            float feather = float.IsFinite(mask.Feather) ? MathF.Max(0f, mask.Feather) * scale : 0f;
            if (radius > 0f || feather > 0f || !String.IsNullOrWhiteSpace(mask.MaskTextureName))
            {
                childRoundedClips = [.. inheritedRoundedClips, new RoundedRectClip(resolved, radius, feather,
                    mask.MaskTextureName, mask.MaskThreshold)];
            }
        }

        Image? image = gameObject.GetComponent<Image>();
        Text? text = gameObject.GetComponent<Text>();
        Button? button = gameObject.GetComponent<Button>();
        Toggle? toggle = gameObject.GetComponent<Toggle>();
        Slider? slider = gameObject.GetComponent<Slider>();
        InputField? inputField = gameObject.GetComponent<InputField>();
        if ((image is { Enabled: true }) || (text is { Enabled: true }) || (button is { Enabled: true })
            || (toggle is { Enabled: true }) || (slider is { Enabled: true }) || (inputField is { Enabled: true }))
        {
            output.Add(new UIElement(gameObject, canvas, rectTransform, resolved, inheritedClip,
                image is { Enabled: true } ? image : null,
                text is { Enabled: true } ? text : null,
                button is { Enabled: true } ? button : null,
                order++,
                toggle is { Enabled: true } ? toggle : null,
                slider is { Enabled: true } ? slider : null,
                inputField is { Enabled: true } ? inputField : null,
                inheritedRoundedClips));
        }

        IReadOnlyDictionary<GameObject, RectF> arrangedChildren = LayoutGroupResolver.Resolve(gameObject, logicalRect);
        foreach (Transform child in gameObject.Transform.Children)
        {
            // A nested Canvas owns its own root and sorting order; it is collected separately above.
            if (child.GameObject.GetComponent<Canvas>() is null)
            {
                RectF? childArrangedRect = arrangedChildren.TryGetValue(child.GameObject, out RectF childRect)
                    ? childRect
                    : null;
                Visit(child.GameObject, canvas, logicalRect, childClip, childRoundedClips,
                    false, scale, output, ref order, childArrangedRect);
            }
        }
    }

    private static RectF Scale(RectF rect, float scale) =>
        new(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);
}
