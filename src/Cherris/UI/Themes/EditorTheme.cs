using System.Globalization;

namespace Cherris.UI;

/// <summary>
/// A complete editor theme: a color for every <see cref="ThemeRoles"/>, the type it is set in, and the few
/// dimensions both front ends share.
/// </summary>
/// <remarks>
/// <para>
/// Themes are data, not code paths. The ImGui editor maps a theme onto <c>ImGuiCol</c> and the Blazor editor
/// applies <see cref="ToCssVariables"/> to the document, so adding a theme (built in, or a JSON file dropped
/// into <c>Themes/</c>) does not touch either front end.
/// </para>
/// <para>
/// <see cref="Provenance"/> is not decoration: it is what the theme's UI tooltip and the docs show, so it is
/// always clear whether a color was copied from the source editor, derived from one, or chosen by us.
/// </para>
/// </remarks>
public sealed record EditorTheme
{
    /// <summary>Stable identifier, also the file name of a JSON theme.</summary>
    public required string Id { get; init; }

    /// <summary>Name shown in the theme menu.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Where the values come from, in one line.</summary>
    public string Provenance { get; init; } = string.Empty;

    /// <summary>The theme's type.</summary>
    public required ThemeTypography Typography { get; init; }

    /// <summary>The theme's dimensions.</summary>
    public required ThemeMetrics Metrics { get; init; }

    /// <summary>One color per role in <see cref="ThemeRoles.All"/>.</summary>
    public required IReadOnlyDictionary<string, ThemeColor> Colors { get; init; }

    /// <summary>True when this theme came from a JSON file rather than from code.</summary>
    public bool IsExternal { get; init; }

    /// <summary>Looks up a role, throwing when the theme is incomplete.</summary>
    /// <param name="role">One of the <see cref="ThemeRoles"/> constants.</param>
    public ThemeColor this[string role] =>
        Colors.TryGetValue(role, out ThemeColor color)
            ? color
            : throw new KeyNotFoundException($"Theme '{Id}' has no color for role '{role}'.");

    /// <summary>Looks up a role without throwing.</summary>
    /// <param name="role">One of the <see cref="ThemeRoles"/> constants.</param>
    public ThemeColor? GetColor(string role)
    {
        return Colors.TryGetValue(role, out ThemeColor color) ? color : null;
    }

    /// <summary>The CSS custom property name for a role: <c>--theme-&lt;role&gt;</c>.</summary>
    /// <param name="role">One of the <see cref="ThemeRoles"/> constants.</param>
    public static string CssToken(string role)
    {
        return "--theme-" + role;
    }

    /// <summary>
    /// Everything the stylesheet needs, as custom property name to value: one entry per role plus the type
    /// and dimension variables. This is what the Blazor editor applies with
    /// <c>document.documentElement.style.setProperty</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToCssVariables()
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);

        foreach ((string role, ThemeColor color) in Colors)
        {
            variables[CssToken(role)] = color.Hex;
        }

        variables["--theme-font-ui"] = Typography.CssStack;
        variables["--theme-font-mono"] = Typography.MonospaceCssStack;
        variables["--theme-font-size"] = Pixels(Typography.BodyPixels);
        variables["--theme-font-small"] = Pixels(Typography.SmallPixels);
        variables["--theme-font-large"] = Pixels(Typography.LargePixels);
        variables["--theme-radius"] = Pixels(Metrics.CornerRadius);
        variables["--theme-border-width"] = Pixels(Metrics.BorderWidth);
        variables["--theme-control-height"] = Pixels(Metrics.ControlHeight);
        variables["--theme-scrollbar-width"] = Pixels(Metrics.ScrollbarWidth);
        variables["--theme-font-weight-bold"] = Typography.BoldWeight.ToString(CultureInfo.InvariantCulture);

        return variables;
    }

    private static string Pixels(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture) + "px";
    }
}
