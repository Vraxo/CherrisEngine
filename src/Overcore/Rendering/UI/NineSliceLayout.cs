using Overcore.Components;
using System.Numerics;

namespace Overcore.Rendering.UI;

/// <summary>A screen-space quad and its matching texture coordinates.</summary>
public readonly record struct UITextureQuad(RectF Rect, float U0, float V0, float U1, float V1);

/// <summary>Expands fractional image border metadata into a 3-by-3 set of textured quads.</summary>
public static class NineSliceLayout
{
    /// <param name="rect">Destination rectangle in screen pixels.</param>
    /// <param name="uvMin">Bottom-left UV of the source sprite.</param>
    /// <param name="uvMax">Top-right UV of the source sprite.</param>
    /// <param name="left">Left border as a fraction of the sprite region.</param>
    /// <param name="top">Top border as a fraction of the sprite region.</param>
    /// <param name="right">Right border as a fraction of the sprite region.</param>
    /// <param name="bottom">Bottom border as a fraction of the sprite region.</param>
    public static IReadOnlyList<UITextureQuad> Build(RectF rect, Vector2 uvMin, Vector2 uvMax,
        float left, float top, float right, float bottom)
    {
        left = Math.Clamp(float.IsFinite(left) ? left : 0f, 0f, 1f);
        top = Math.Clamp(float.IsFinite(top) ? top : 0f, 0f, 1f);
        right = Math.Clamp(float.IsFinite(right) ? right : 0f, 0f, 1f);
        bottom = Math.Clamp(float.IsFinite(bottom) ? bottom : 0f, 0f, 1f);
        NormalizePair(ref left, ref right);
        NormalizePair(ref top, ref bottom);

        float[] xs = [rect.X, rect.X + rect.Width * left, rect.Right - rect.Width * right, rect.Right];
        float[] ys = [rect.Y, rect.Y + rect.Height * top, rect.Bottom - rect.Height * bottom, rect.Bottom];
        float[] us = [uvMin.X, uvMin.X + (uvMax.X - uvMin.X) * left,
            uvMax.X - (uvMax.X - uvMin.X) * right, uvMax.X];
        // UVs run bottom-to-top while screen coordinates run top-to-bottom.
        float[] vs = [uvMax.Y, uvMax.Y - (uvMax.Y - uvMin.Y) * top,
            uvMin.Y + (uvMax.Y - uvMin.Y) * bottom, uvMin.Y];

        List<UITextureQuad> quads = new(9);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                RectF cell = new(xs[column], ys[row], MathF.Max(0f, xs[column + 1] - xs[column]),
                    MathF.Max(0f, ys[row + 1] - ys[row]));
                if (!cell.IsEmpty)
                {
                    quads.Add(new UITextureQuad(cell, us[column], vs[row], us[column + 1], vs[row + 1]));
                }
            }
        }

        return quads;
    }

    private static void NormalizePair(ref float first, ref float second)
    {
        float sum = first + second;
        if (sum > 1f)
        {
            first /= sum;
            second /= sum;
        }
    }
}
