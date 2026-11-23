namespace Cherris.UI;

/// <summary>
/// The Unreal Engine 5 editor's typography, as <c>FStarshipCoreStyle::FStyleFonts</c> declares it.
/// </summary>
/// <remarks>
/// <para>
/// The engine uses Roboto for UI text and Roboto Mono for monospaced text, sized in Slate points:
/// <c>Normal</c> 10pt, <c>Small</c> 8pt, <c>Large</c> 14pt, and the condensed headings at 15/21/33pt
/// (<c>StarshipCoreStyle.cpp</c>). Slate scales points by the display DPI; at the 96 DPI the editor's
/// windows run at, 10pt is 13.33px.
/// </para>
/// <para>
/// Pixel sizes are rounded to whole pixels (<see cref="NormalPixels"/> = 13) because both ImGui's font atlas
/// and browser text shaping look blurry at fractional sizes. <see cref="ToPixels"/> keeps the exact
/// conversion for anything that needs it — the stylesheet documents the same rounding.
/// </para>
/// </remarks>
public static class Ue5Typography
{
    /// <summary>UI font family (bundled as <c>wwwroot/fonts/roboto-*.woff2</c> and <c>Fonts/Roboto-Regular.TTF</c>).</summary>
    public const string Family = "Roboto";

    /// <summary>Monospaced family, used for logs and code, as the engine's "Mono" font is.</summary>
    public const string MonospaceFamily = "Roboto Mono";

    /// <summary>Slate <c>Normal</c> text size, in points.</summary>
    public const float NormalPointSize = 10f;

    /// <summary>Slate <c>Small</c> text size, in points.</summary>
    public const float SmallPointSize = 8f;

    /// <summary>Slate <c>Large</c> text size, in points.</summary>
    public const float LargePointSize = 14f;

    /// <summary>Slate heading size, in points.</summary>
    public const float HeadingExtraSmallPointSize = 15f;

    /// <summary>Slate heading size, in points.</summary>
    public const float HeadingSmallPointSize = 21f;

    /// <summary>Slate heading size, in points.</summary>
    public const float HeadingMediumPointSize = 33f;

    /// <summary>The DPI the editor's pixel sizes are computed for.</summary>
    public const float ReferenceDpi = 96f;

    /// <summary>
    /// The TTF the ImGui editor loads for UI text, relative to <c>EditorResources</c>. The file is the
    /// latin + latin-ext + cyrillic + greek merge of the official Roboto subsets, so it covers what the
    /// stylesheet's four <c>@font-face</c> blocks cover.
    /// </summary>
    public const string UiFontResource = "Fonts/Roboto-Regular.TTF";

    /// <summary>The monospaced TTF, kept for logs and for machines where the UI font is missing.</summary>
    public const string MonospaceFontResource = "Fonts/RobotoMono-Regular.TTF";

    /// <summary>Body text size in pixels: 10pt rounded to the pixel grid (13.33px in the engine).</summary>
    public static float NormalPixels => 13f;

    /// <summary>Small text size in pixels: 8pt rounded (10.67px in the engine).</summary>
    public static float SmallPixels => 11f;

    /// <summary>Large text size in pixels: 14pt rounded (18.67px in the engine).</summary>
    public static float LargePixels => 19f;

    /// <summary>Heading size in pixels: 15pt (20px in the engine).</summary>
    public static float HeadingExtraSmallPixels => 20f;

    /// <summary>Converts a Slate point size to pixels at <see cref="ReferenceDpi"/>.</summary>
    /// <param name="pointSize">Size in points, as the engine declares it.</param>
    public static float ToPixels(float pointSize)
    {
        return pointSize * ReferenceDpi / 72f;
    }
}
