namespace Cherris.UI;

/// <summary>
/// The Unreal Engine 5 editor theme: <see cref="Ue5Colors"/> mapped onto the theme's roles.
/// </summary>
/// <remarks>
/// This is the theme the editors shipped with, kept as a theme so it can be selected, exported and compared
/// rather than being the only arrangement the code knows. Every value is the engine's own — see
/// <see cref="Ue5Colors"/> for the source line each color comes from and <see cref="Ue5Typography"/> for the
/// type.
/// </remarks>
public static class Ue5StarshipTheme
{
    /// <summary>Identifier used in settings, JSON files and the theme menu.</summary>
    public const string Id = "ue5-starship";

    /// <summary>Builds the theme.</summary>
    public static EditorTheme Create()
    {
        return new EditorTheme
        {
            Id = Id,
            DisplayName = "Unreal Engine 5",
            Provenance = "Values from SlateCore StyleColors.cpp (dark theme), StarshipCoreStyle.cpp and CoreStyle.h.",
            Typography = new ThemeTypography(
                Ue5Typography.Family,
                Ue5Typography.MonospaceFamily,
                Ue5Typography.NormalPixels,
                Ue5Typography.SmallPixels,
                Ue5Typography.LargePixels,
                Ue5Typography.UiFontResource,
                Ue5Typography.MonospaceFontResource,
                "\"Roboto\", \"Segoe UI\", system-ui, sans-serif"),
            Metrics = new ThemeMetrics(
                CornerRadius: 4f,
                BorderWidth: 1f,
                ControlHeight: 22f,
                ScrollbarWidth: 8f),
            Colors = new Dictionary<string, ThemeColor>(StringComparer.Ordinal)
            {
                [ThemeRoles.Black] = Ue5Colors.Black,
                [ThemeRoles.Title] = Ue5Colors.Title,
                [ThemeRoles.Background] = Ue5Colors.Background,
                [ThemeRoles.WindowBorder] = Ue5Colors.WindowBorder,
                [ThemeRoles.Foldout] = Ue5Colors.Foldout,
                [ThemeRoles.Input] = Ue5Colors.Input,
                [ThemeRoles.InputOutline] = Ue5Colors.InputOutline,
                [ThemeRoles.Recessed] = Ue5Colors.Recessed,
                [ThemeRoles.Panel] = Ue5Colors.Panel,
                [ThemeRoles.Header] = Ue5Colors.Header,
                [ThemeRoles.Dropdown] = Ue5Colors.Dropdown,
                [ThemeRoles.DropdownOutline] = Ue5Colors.DropdownOutline,
                [ThemeRoles.Hover] = Ue5Colors.Hover,
                [ThemeRoles.Hover2] = Ue5Colors.Hover2,
                [ThemeRoles.White] = Ue5Colors.White,
                [ThemeRoles.White25] = Ue5Colors.White25,
                [ThemeRoles.Highlight] = Ue5Colors.Highlight,
                [ThemeRoles.Primary] = Ue5Colors.Primary,
                [ThemeRoles.PrimaryHover] = Ue5Colors.PrimaryHover,
                [ThemeRoles.PrimaryPress] = Ue5Colors.PrimaryPress,
                [ThemeRoles.Secondary] = Ue5Colors.Secondary,
                [ThemeRoles.Foreground] = Ue5Colors.Foreground,
                [ThemeRoles.ForegroundHover] = Ue5Colors.ForegroundHover,
                [ThemeRoles.ForegroundInverted] = Ue5Colors.ForegroundInverted,
                [ThemeRoles.ForegroundHeader] = Ue5Colors.ForegroundHeader,
                [ThemeRoles.Select] = Ue5Colors.Select,
                [ThemeRoles.SelectInactive] = Ue5Colors.SelectInactive,
                [ThemeRoles.SelectParent] = Ue5Colors.SelectParent,
                [ThemeRoles.SelectHover] = Ue5Colors.SelectHover,
                [ThemeRoles.Notifications] = Ue5Colors.Notifications,
                [ThemeRoles.AccentBlue] = Ue5Colors.AccentBlue,
                [ThemeRoles.AccentPurple] = Ue5Colors.AccentPurple,
                [ThemeRoles.AccentPink] = Ue5Colors.AccentPink,
                [ThemeRoles.AccentRed] = Ue5Colors.AccentRed,
                [ThemeRoles.AccentOrange] = Ue5Colors.AccentOrange,
                [ThemeRoles.AccentYellow] = Ue5Colors.AccentYellow,
                [ThemeRoles.AccentGreen] = Ue5Colors.AccentGreen,
                [ThemeRoles.AccentBrown] = Ue5Colors.AccentBrown,
                [ThemeRoles.AccentBlack] = Ue5Colors.AccentBlack,
                [ThemeRoles.AccentGray] = Ue5Colors.AccentGray,
                [ThemeRoles.AccentWhite] = Ue5Colors.AccentWhite,
                [ThemeRoles.AccentFolder] = Ue5Colors.AccentFolder,
                [ThemeRoles.Warning] = Ue5Colors.Warning,
                [ThemeRoles.Error] = Ue5Colors.Error,
                [ThemeRoles.Success] = Ue5Colors.Success,
                [ThemeRoles.Selection] = Ue5Colors.Selection,
                [ThemeRoles.SelectionPressed] = Ue5Colors.SelectionPressed,
                [ThemeRoles.TabHover] = Ue5Colors.TabHover
            }
        };
    }
}
