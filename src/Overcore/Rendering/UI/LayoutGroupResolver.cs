using Overcore.Components;
using Overcore.Core;

namespace Overcore.Rendering.UI;

/// <summary>Calculates logical child rectangles for the built-in horizontal and vertical layout groups.</summary>
internal static class LayoutGroupResolver
{
    public static IReadOnlyDictionary<GameObject, RectF> Resolve(GameObject parent, RectF parentRect)
    {
        LayoutGroup? group = parent.GetComponent<LayoutGroup>();
        if (group is null || !group.Enabled)
        {
            return new Dictionary<GameObject, RectF>();
        }

        bool horizontal = group is HorizontalLayoutGroup;
        List<(GameObject GameObject, RectF Preferred)> children = [];
        foreach (Transform childTransform in parent.Transform.Children)
        {
            GameObject child = childTransform.GameObject;
            RectTransform? rectTransform = child.GetComponent<RectTransform>();
            if (!child.ActiveInHierarchy || rectTransform is not { Enabled: true } ||
                child.GetComponent<Canvas>() is not null)
            {
                continue;
            }

            children.Add((child, rectTransform.Resolve(parentRect)));
        }

        Dictionary<GameObject, RectF> result = new();
        if (children.Count == 0)
        {
            return result;
        }

        float left = MathF.Max(0f, group.PaddingLeft);
        float right = MathF.Max(0f, group.PaddingRight);
        float top = MathF.Max(0f, group.PaddingTop);
        float bottom = MathF.Max(0f, group.PaddingBottom);
        float spacing = MathF.Max(0f, group.Spacing);
        float innerWidth = MathF.Max(0f, parentRect.Width - left - right);
        float innerHeight = MathF.Max(0f, parentRect.Height - top - bottom);
        float mainAvailable = horizontal ? innerWidth : innerHeight;
        float crossAvailable = horizontal ? innerHeight : innerWidth;
        float mainPaddingStart = horizontal ? left : top;
        float crossPaddingStart = horizontal ? top : left;
        bool controlMain = horizontal ? group.ControlChildWidth : group.ControlChildHeight;
        bool controlCross = horizontal ? group.ControlChildHeight : group.ControlChildWidth;
        bool expandMain = horizontal ? group.ChildForceExpandWidth : group.ChildForceExpandHeight;
        bool expandCross = horizontal ? group.ChildForceExpandHeight : group.ChildForceExpandWidth;

        float[] mainSizes = new float[children.Count];
        float totalMain = spacing * (children.Count - 1);
        for (int index = 0; index < children.Count; index++)
        {
            RectF preferred = children[index].Preferred;
            mainSizes[index] = MathF.Max(0f, horizontal ? preferred.Width : preferred.Height);
            totalMain += mainSizes[index];
        }

        float freeMain = MathF.Max(0f, mainAvailable - totalMain);
        if (expandMain)
        {
            float share = freeMain / children.Count;
            for (int index = 0; index < mainSizes.Length; index++)
            {
                mainSizes[index] += share;
            }
            freeMain = 0f;
        }

        float cursor = mainPaddingStart + AlignOffset(group.ChildAlignment, freeMain);
        for (int index = 0; index < children.Count; index++)
        {
            (GameObject child, RectF preferred) = children[index];
            float preferredWidth = MathF.Max(0f, preferred.Width);
            float preferredHeight = MathF.Max(0f, preferred.Height);
            float width = horizontal
                ? (controlMain ? mainSizes[index] : preferredWidth)
                : ResolveCrossSize(preferredWidth, crossAvailable, controlCross, expandCross);
            float height = horizontal
                ? ResolveCrossSize(preferredHeight, crossAvailable, controlCross, expandCross)
                : (controlMain ? mainSizes[index] : preferredHeight);

            float crossSize = horizontal ? height : width;
            float crossFree = MathF.Max(0f, crossAvailable - crossSize);
            float crossPosition = crossPaddingStart + AlignOffset(group.ChildAlignment, crossFree);
            float x = horizontal ? parentRect.X + cursor : parentRect.X + crossPosition;
            float y = horizontal ? parentRect.Y + crossPosition : parentRect.Y + cursor;
            result[child] = new RectF(x, y, width, height);
            cursor += mainSizes[index] + spacing;
        }

        return result;
    }

    private static float ResolveCrossSize(float preferred, float available, bool controlled, bool forceExpand) =>
        controlled && forceExpand ? available : preferred;

    private static float AlignOffset(LayoutAlignment alignment, float freeSpace) => alignment switch
    {
        LayoutAlignment.Center => freeSpace * 0.5f,
        LayoutAlignment.End => freeSpace,
        _ => 0f
    };
}
