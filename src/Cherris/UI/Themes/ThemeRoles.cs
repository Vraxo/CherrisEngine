namespace Cherris.UI;

/// <summary>
/// The names of the roles every <see cref="EditorTheme"/> has to fill in.
/// </summary>
/// <remarks>
/// <para>
/// A role is what a widget *is*, not what a particular editor calls it: <see cref="Panel"/> is the surface a
/// docked panel sits on, <see cref="Recessed"/> the darker surface a list or an input well sits on, and so
/// on. The UE5 theme fills them from <see cref="Ue5Colors"/>, the Unity theme from Unity's
/// <c>--unity-colors-*</c> variables, and the stylesheet reads them as <c>--theme-&lt;role&gt;</c>.
/// </para>
/// <para>
/// The role set is the UE5 palette's own vocabulary, which is what the editors were built against; a new
/// theme has to supply all of them, even where it has no exact equivalent (the theme file says where it
/// approximated). <see cref="All"/> is the list tests walk.
/// </para>
/// </remarks>
public static class ThemeRoles
{
    /// <summary>Absolute black: viewport backgrounds, shadows.</summary>
    public const string Black = "black";

    /// <summary>Window title bar.</summary>
    public const string Title = "title";

    /// <summary>The editor background behind panels: tab wells, toolbars that sit on the chrome.</summary>
    public const string Background = "background";

    /// <summary>Outline of the window itself.</summary>
    public const string WindowBorder = "window-border";

    /// <summary>Disclosure arrows and foldout glyphs.</summary>
    public const string Foldout = "foldout";

    /// <summary>Fill of an editable text field.</summary>
    public const string Input = "input";

    /// <summary>Resting outline of an editable text field.</summary>
    public const string InputOutline = "input-outline";

    /// <summary>The darker surface list rows and input wells sit on.</summary>
    public const string Recessed = "recessed";

    /// <summary>The surface a docked panel sits on.</summary>
    public const string Panel = "panel";

    /// <summary>Panel section headers: table headers, category headers.</summary>
    public const string Header = "header";

    /// <summary>Dropdowns and popup menus.</summary>
    public const string Dropdown = "dropdown";

    /// <summary>Outline of a dropdown or popup menu.</summary>
    public const string DropdownOutline = "dropdown-outline";

    /// <summary>Generic hover fill for buttons and bars.</summary>
    public const string Hover = "hover";

    /// <summary>Stronger hover: scrollbar thumbs and other dragged affordances.</summary>
    public const string Hover2 = "hover-2";

    /// <summary>Pure white, for inverse text and glyphs.</summary>
    public const string White = "white";

    /// <summary>A translucent white rule: menu separators and hairlines.</summary>
    public const string White25 = "white-25";

    /// <summary>Native text selection inside an editable field.</summary>
    public const string Highlight = "highlight";

    /// <summary>The accent color: focus, primary actions, active indicators.</summary>
    public const string Primary = "primary";

    /// <summary>The accent under the pointer.</summary>
    public const string PrimaryHover = "primary-hover";

    /// <summary>The accent while pressed.</summary>
    public const string PrimaryPress = "primary-press";

    /// <summary>Fill of a normal button.</summary>
    public const string Secondary = "secondary";

    /// <summary>Body text.</summary>
    public const string Foreground = "foreground";

    /// <summary>Body text under the pointer, and on selected rows.</summary>
    public const string ForegroundHover = "foreground-hover";

    /// <summary>Text drawn on a light fill.</summary>
    public const string ForegroundInverted = "foreground-inverted";

    /// <summary>Header text.</summary>
    public const string ForegroundHeader = "foreground-header";

    /// <summary>Fill of a selected row.</summary>
    public const string Select = "select";

    /// <summary>Fill of a selected row in a list that does not have focus.</summary>
    public const string SelectInactive = "select-inactive";

    /// <summary>Fill of a row that is the parent of the selection.</summary>
    public const string SelectParent = "select-parent";

    /// <summary>Fill of a row under the pointer.</summary>
    public const string SelectHover = "select-hover";

    /// <summary>The notification strip.</summary>
    public const string Notifications = "notifications";

    /// <summary>Asset tint: blue.</summary>
    public const string AccentBlue = "accent-blue";

    /// <summary>Asset tint: purple.</summary>
    public const string AccentPurple = "accent-purple";

    /// <summary>Asset tint: pink.</summary>
    public const string AccentPink = "accent-pink";

    /// <summary>Asset tint: red.</summary>
    public const string AccentRed = "accent-red";

    /// <summary>Asset tint: orange.</summary>
    public const string AccentOrange = "accent-orange";

    /// <summary>Asset tint: yellow.</summary>
    public const string AccentYellow = "accent-yellow";

    /// <summary>Asset tint: green.</summary>
    public const string AccentGreen = "accent-green";

    /// <summary>Asset tint: brown.</summary>
    public const string AccentBrown = "accent-brown";

    /// <summary>Asset tint: near-black.</summary>
    public const string AccentBlack = "accent-black";

    /// <summary>Muted text and disabled glyphs.</summary>
    public const string AccentGray = "accent-gray";

    /// <summary>Asset tint: white.</summary>
    public const string AccentWhite = "accent-white";

    /// <summary>Asset tint: folders.</summary>
    public const string AccentFolder = "accent-folder";

    /// <summary>Warning text and icons.</summary>
    public const string Warning = "warning";

    /// <summary>Error text and icons.</summary>
    public const string Error = "error";

    /// <summary>Success text and icons.</summary>
    public const string Success = "success";

    /// <summary>Selector and drag affordance blue: drop indicators, toolbar hover.</summary>
    public const string Selection = "selection";

    /// <summary>The selector blue while pressed.</summary>
    public const string SelectionPressed = "selection-pressed";

    /// <summary>A hovered dock tab.</summary>
    public const string TabHover = "tab-hover";

    /// <summary>Every role, in palette order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Black, Title, Background, WindowBorder, Foldout, Input, InputOutline, Recessed, Panel, Header,
        Dropdown, DropdownOutline, Hover, Hover2, White, White25, Highlight, Primary, PrimaryHover,
        PrimaryPress, Secondary, Foreground, ForegroundHover, ForegroundInverted, ForegroundHeader, Select,
        SelectInactive, SelectParent, SelectHover, Notifications, AccentBlue, AccentPurple, AccentPink,
        AccentRed, AccentOrange, AccentYellow, AccentGreen, AccentBrown, AccentBlack, AccentGray, AccentWhite,
        AccentFolder, Warning, Error, Success, Selection, SelectionPressed, TabHover
    ];
}
