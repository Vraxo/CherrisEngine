namespace Overcore.UI;

/// <summary>
/// Unity's dark editor theme, from Unity's published Editor Foundations design system.
/// </summary>
/// <remarks>
/// <para>
/// Unity documents the Editor's skin as <c>--unity-colors-*</c> custom properties with a light and a dark
/// column, on <see href="https://www.foundations.unity.com/fundamentals/color-palette">foundations.unity.com</see>
/// and the matching typography page. Every value below is that dark column; the Unity variable it comes from
/// is named next to it, so a color can be checked against the published table.
/// </para>
/// <para>
/// Unity's variable set is organized per component, ours per role, so a few roles borrow the closest
/// component color — each of those says so. Values Unity does not publish at all (asset tint hues, the
/// selector/drag color, the success green, the scrollbar width) are marked "ours": they follow Unity's hues
/// but are not Unity's numbers.
/// </para>
/// </remarks>
public static class UnityDarkTheme
{
    /// <summary>Identifier used in settings, JSON files and the theme menu.</summary>
    public const string Id = "unity-dark";

    /// <summary>Builds the theme.</summary>
    public static EditorTheme Create()
    {
        return new EditorTheme
        {
            Id = Id,
            DisplayName = "Unity (dark)",
            Provenance = "Dark column of Unity's --unity-colors-* design-system palette and its Inter type scale.",
            Typography = new ThemeTypography(
                Family: "Inter",
                MonospaceFamily: "Roboto Mono",
                // --unity-metrics-default-font_normal_size / _small_ / _big_.
                BodyPixels: 12f,
                SmallPixels: 10f,
                LargePixels: 14f,
                FontResource: "Fonts/Inter-Regular.TTF",
                FallbackFontResource: Ue5Typography.MonospaceFontResource,
                // Inter is the primary family on every platform; Verdana and Lucida Grande are Unity's
                // documented fallbacks.
                CssStack: "\"Inter\", Verdana, \"Lucida Grande\", system-ui, sans-serif",
                // Inter ships 600 rather than 700; Unity emphasizes with semibold.
                BoldWeight: 600),
            Metrics = new ThemeMetrics(
                // Unity's controls are rounded; 3px is the radius its USS controls use (ours: not published).
                CornerRadius: 3f,
                BorderWidth: 1f,
                // --unity-metrics-single_line-height.
                ControlHeight: 18f,
                // Ours: Unity does not publish a scrollbar width.
                ScrollbarWidth: 12f),
            Colors = new Dictionary<string, ThemeColor>(StringComparer.Ordinal)
            {
                // Ours: the Scene view's clear color is black and Unity has no role for it.
                [ThemeRoles.Black] = ThemeColor.FromHex("#000000"),

                // --unity-colors-inspector_titlebar-background
                [ThemeRoles.Title] = ThemeColor.FromHex("#3e3e3e"),
                // --unity-colors-app_toolbar-background: the darkest surface, behind the docks.
                [ThemeRoles.Background] = ThemeColor.FromHex("#191919"),
                // --unity-colors-window-border
                [ThemeRoles.WindowBorder] = ThemeColor.FromHex("#242424"),

                // --unity-colors-default-text: Unity's foldout arrows are text colored.
                [ThemeRoles.Foldout] = ThemeColor.FromHex("#d2d2d2"),
                // --unity-colors-input_field-background
                [ThemeRoles.Input] = ThemeColor.FromHex("#2a2a2a"),
                // --unity-colors-input_field-border
                [ThemeRoles.InputOutline] = ThemeColor.FromHex("#212121"),
                // --unity-colors-default-background: what controls and list wells sit on.
                [ThemeRoles.Recessed] = ThemeColor.FromHex("#282828"),
                // --unity-colors-window-background
                [ThemeRoles.Panel] = ThemeColor.FromHex("#383838"),
                // --unity-colors-headerbar-background
                [ThemeRoles.Header] = ThemeColor.FromHex("#3c3c3c"),

                // --unity-colors-dropdown-background
                [ThemeRoles.Dropdown] = ThemeColor.FromHex("#515151"),
                // --unity-colors-dropdown-border
                [ThemeRoles.DropdownOutline] = ThemeColor.FromHex("#303030"),

                // --unity-colors-button-background-hover
                [ThemeRoles.Hover] = ThemeColor.FromHex("#676767"),
                // --unity-colors-scrollbar_thumb-background-hover
                [ThemeRoles.Hover2] = ThemeColor.FromHex("#686868"),
                // --unity-colors-highlight-text-inactive
                [ThemeRoles.White] = ThemeColor.FromHex("#ffffff"),
                // --unity-colors-default-border: Unity's separators are opaque dark rules, not translucent
                // white ones, so the role keeps its meaning but not its translucency.
                [ThemeRoles.White25] = ThemeColor.FromHex("#232323"),

                // --unity-colors-highlight-background: selected text.
                [ThemeRoles.Highlight] = ThemeColor.FromHex("#2c5d87"),
                // --unity-colors-input_field-border-focus: the accent Unity puts on a focused control.
                [ThemeRoles.Primary] = ThemeColor.FromHex("#3a79bb"),
                // --unity-colors-label-text-focus
                [ThemeRoles.PrimaryHover] = ThemeColor.FromHex("#81b4ff"),
                // --unity-colors-button-background-pressed
                [ThemeRoles.PrimaryPress] = ThemeColor.FromHex("#46607c"),
                // --unity-colors-button-background
                [ThemeRoles.Secondary] = ThemeColor.FromHex("#585858"),

                // --unity-colors-default-text
                [ThemeRoles.Foreground] = ThemeColor.FromHex("#d2d2d2"),
                // --unity-colors-button-text
                [ThemeRoles.ForegroundHover] = ThemeColor.FromHex("#eeeeee"),
                // Ours: Unity's light column's text color, for text on a light fill.
                [ThemeRoles.ForegroundInverted] = ThemeColor.FromHex("#090909"),
                // --unity-colors-label-text
                [ThemeRoles.ForegroundHeader] = ThemeColor.FromHex("#c4c4c4"),

                // --unity-colors-highlight-background
                [ThemeRoles.Select] = ThemeColor.FromHex("#2c5d87"),
                // --unity-colors-highlight-background-inactive
                [ThemeRoles.SelectInactive] = ThemeColor.FromHex("#4d4d4d"),
                // --unity-colors-highlight-background-hover-lighter
                [ThemeRoles.SelectParent] = ThemeColor.FromHex("#5f5f5f"),
                // --unity-colors-highlight-background-hover composited over --unity-colors-window-background:
                // rgba(255,255,255,0.0627) over #383838 is #3F3F3F, which is also the alternated row color.
                [ThemeRoles.SelectHover] = ThemeColor.FromHex("#3f3f3f"),

                // --unity-colors-toolbar-background
                [ThemeRoles.Notifications] = ThemeColor.FromHex("#3c3c3c"),

                // Asset tints: Unity's design system has no asset-color palette, so these follow Unity's hues
                // but are ours. Where Unity does publish the hue, it comes from one of its text colors.
                // --unity-colors-link-text
                [ThemeRoles.AccentBlue] = ThemeColor.FromHex("#4c7eff"),
                // Ours.
                [ThemeRoles.AccentPurple] = ThemeColor.FromHex("#a55fd6"),
                // Ours.
                [ThemeRoles.AccentPink] = ThemeColor.FromHex("#e56aa6"),
                // --unity-colors-error-text
                [ThemeRoles.AccentRed] = ThemeColor.FromHex("#d32222"),
                // Ours.
                [ThemeRoles.AccentOrange] = ThemeColor.FromHex("#e08a1e"),
                // --unity-colors-warning-text
                [ThemeRoles.AccentYellow] = ThemeColor.FromHex("#f4bc02"),
                // Ours.
                [ThemeRoles.AccentGreen] = ThemeColor.FromHex("#7ac24a"),
                // Ours.
                [ThemeRoles.AccentBrown] = ThemeColor.FromHex("#9a6b4f"),
                // --unity-colors-app_toolbar-background
                [ThemeRoles.AccentBlack] = ThemeColor.FromHex("#191919"),
                // Ours.
                [ThemeRoles.AccentGray] = ThemeColor.FromHex("#9a9a9a"),
                // --unity-colors-highlight-text-inactive
                [ThemeRoles.AccentWhite] = ThemeColor.FromHex("#ffffff"),
                // Ours: the Project window's folder tint.
                [ThemeRoles.AccentFolder] = ThemeColor.FromHex("#d9b04a"),

                // --unity-colors-warning-text
                [ThemeRoles.Warning] = ThemeColor.FromHex("#f4bc02"),
                // --unity-colors-error-text
                [ThemeRoles.Error] = ThemeColor.FromHex("#d32222"),
                // Ours: Unity has no success color; this is the hue its green console lines use.
                [ThemeRoles.Success] = ThemeColor.FromHex("#7ac24a"),

                // Ours: Unity publishes no selector/drag affordance color; the accent blue is the closest.
                // --unity-colors-link-text
                [ThemeRoles.Selection] = ThemeColor.FromHex("#4c7eff"),
                // Ours: the pressed variant, a shade darker.
                [ThemeRoles.SelectionPressed] = ThemeColor.FromHex("#3a79bb"),
                // --unity-colors-tab-background-hover
                [ThemeRoles.TabHover] = ThemeColor.FromHex("#303030")
            }
        };
    }
}
