namespace Overcore.UI;

/// <summary>
/// The type a theme renders its UI in.
/// </summary>
/// <param name="Family">UI font family name, as the stylesheet and the font file declare it.</param>
/// <param name="MonospaceFamily">Family used for logs and code.</param>
/// <param name="BodyPixels">Body text size in pixels.</param>
/// <param name="SmallPixels">Small text size in pixels.</param>
/// <param name="LargePixels">Large text size in pixels.</param>
/// <param name="FontResource">TTF the ImGui editor loads for body text, relative to <c>EditorResources</c>.</param>
/// <param name="FallbackFontResource">TTF to fall back to when <paramref name="FontResource"/> is missing.</param>
/// <param name="CssStack">
/// The <c>font-family</c> value the stylesheet uses, including fallbacks — the bundled family first, then the
/// families the source editor falls back to on each platform.
/// </param>
/// <param name="BoldWeight">The weight of bold text; themes ship 700 (Roboto) or 600 (Inter).</param>
public sealed record ThemeTypography(
    string Family,
    string MonospaceFamily,
    float BodyPixels,
    float SmallPixels,
    float LargePixels,
    string FontResource,
    string FallbackFontResource,
    string CssStack,
    int BoldWeight = 700)
{
    /// <summary>The monospace stack the stylesheet uses for logs and code.</summary>
    public string MonospaceCssStack =>
        $"\"{MonospaceFamily}\", \"Cascadia Mono\", ui-monospace, monospace";

    /// <summary>The sizes a theme has to state, without the font files (JSON themes may omit those).</summary>
    public static ThemeTypography CreateFallback(string family, float body, float small, float large)
    {
        return new ThemeTypography(
            family,
            family,
            body,
            small,
            large,
            string.Empty,
            string.Empty,
            $"\"{family}\", \"Segoe UI\", system-ui, sans-serif");
    }
}

/// <summary>
/// The handful of dimensions both front ends share.
/// </summary>
/// <param name="CornerRadius">Corner radius of inputs, buttons, tabs and tiles.</param>
/// <param name="BorderWidth">Width of a control's outline.</param>
/// <param name="ControlHeight">Height of a single-line control: a text field, a button, a row.</param>
/// <param name="ScrollbarWidth">Width of a vertical scrollbar.</param>
public sealed record ThemeMetrics(
    float CornerRadius,
    float BorderWidth,
    float ControlHeight,
    float ScrollbarWidth);
