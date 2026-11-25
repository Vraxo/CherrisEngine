using System.Numerics;

namespace Overcore.UI;

/// <summary>
/// The Unreal Engine 5 editor palette (the "Starship" style), exactly as the engine defines it.
/// </summary>
/// <remarks>
/// <para>
/// Values are copied from the engine's own defaults, not eyeballed from screenshots:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>Engine/Source/Runtime/SlateCore/Private/Styling/StyleColors.cpp</c>,
/// <c>USlateThemeManager::InitalizeDefaults()</c> — the built-in dark theme. Every entry there is written as
/// <c>SetDefaultColor(EStyleColor::X, COLOR("#RRGGBBAA"))</c>, and <c>COLOR</c> is
/// <c>FLinearColor::FromSRGBColor(FColor::FromHex(HexValue))</c> (<c>StyleColors.h</c>): the hex is the sRGB
/// color you see, which is also the space ImGui and CSS work in, so the values below are used verbatim.
/// </item>
/// <item>
/// <c>Engine/Source/Runtime/SlateCore/Private/Styling/StarshipCoreStyle.cpp</c> — the widget styles that
/// consume those theme colors (inputs, rows, tabs, menus, scrollbars) plus the two colors the style itself
/// owns: <see cref="Selection"/> and <see cref="TabHover"/>.
/// </item>
/// </list>
/// <para>
/// <see cref="TabHover"/> is the only composite: the engine paints a hovered dock tab with
/// <c>Panel.CopyWithNewOpacity(0.8)</c> over the tab well (<c>Background</c>), and Slate blends in sRGB, so
/// the visible result is <c>0.8 * 0x24 + 0.2 * 0x15 = 0x21</c>.
/// </para>
/// </remarks>
public static class Ue5Colors
{
    // ---- StyleColors.cpp: USlateThemeManager::InitalizeDefaults (dark theme) -------------------------

    /// <summary>The non-mutable "black" slot; the light theme flips it to white.</summary>
    public static readonly ThemeColor Black = ThemeColor.FromRgb(0x000000);

    /// <summary>Window title bars.</summary>
    public static readonly ThemeColor Title = ThemeColor.FromRgb(0x151515);

    /// <summary>The editor's base surface: tab wells, toolbars, empty docking space.</summary>
    public static readonly ThemeColor Background = ThemeColor.FromRgb(0x151515);

    /// <summary>1px window borders.</summary>
    public static readonly ThemeColor WindowBorder = ThemeColor.FromRgb(0x0F0F0F);

    /// <summary>Collapsible area background.</summary>
    public static readonly ThemeColor Foldout = ThemeColor.FromRgb(0x0F0F0F);

    /// <summary>Text input and button fill.</summary>
    public static readonly ThemeColor Input = ThemeColor.FromRgb(0x0F0F0F);

    /// <summary>Input border; also the default divider weight.</summary>
    public static readonly ThemeColor InputOutline = ThemeColor.FromRgb(0x383838);

    /// <summary>Recessed surfaces: list view rows, separators, progress bar troughs.</summary>
    public static readonly ThemeColor Recessed = ThemeColor.FromRgb(0x1A1A1A);

    /// <summary>Docked panel background and active tab fill.</summary>
    public static readonly ThemeColor Panel = ThemeColor.FromRgb(0x242424);

    /// <summary>Panel headers and category headers.</summary>
    public static readonly ThemeColor Header = ThemeColor.FromRgb(0x2F2F2F);

    /// <summary>Combo boxes, tooltips and menu popups.</summary>
    public static readonly ThemeColor Dropdown = ThemeColor.FromRgb(0x383838);

    /// <summary>Border of dropdowns, tooltips and menu popups.</summary>
    public static readonly ThemeColor DropdownOutline = ThemeColor.FromRgb(0x4C4C4C);

    /// <summary>Hover highlight: menu bar buttons, scrollbar thumbs, outline of hovered inputs.</summary>
    public static readonly ThemeColor Hover = ThemeColor.FromRgb(0x575757);

    /// <summary>Stronger hover, used for hovered/dragged scrollbar thumbs.</summary>
    public static readonly ThemeColor Hover2 = ThemeColor.FromRgb(0x808080);

    /// <summary>Pure white, for icons and check marks.</summary>
    public static readonly ThemeColor White = ThemeColor.FromRgb(0xFFFFFF);

    /// <summary>White at 25%: menu separators and headings.</summary>
    public static readonly ThemeColor White25 = ThemeColor.FromRgb(0xFFFFFF, 0x40);

    /// <summary>The highlight/underline blue.</summary>
    public static readonly ThemeColor Highlight = ThemeColor.FromRgb(0x0070E0);

    /// <summary>The primary action blue: selection fills, focus outlines, the active tab indicator.</summary>
    public static readonly ThemeColor Primary = ThemeColor.FromRgb(0x0070E0);

    /// <summary>Primary, hovered.</summary>
    public static readonly ThemeColor PrimaryHover = ThemeColor.FromRgb(0x0E86FF);

    /// <summary>Primary, pressed.</summary>
    public static readonly ThemeColor PrimaryPress = ThemeColor.FromRgb(0x0050A0);

    /// <summary>Secondary fill for toggles and split handles.</summary>
    public static readonly ThemeColor Secondary = ThemeColor.FromRgb(0x383838);

    /// <summary>Default text color.</summary>
    public static readonly ThemeColor Foreground = ThemeColor.FromRgb(0xC0C0C0);

    /// <summary>Hovered/active text color.</summary>
    public static readonly ThemeColor ForegroundHover = ThemeColor.FromRgb(0xFFFFFF);

    /// <summary>Text on a light fill; falls back to the input fill.</summary>
    public static readonly ThemeColor ForegroundInverted = ThemeColor.FromRgb(0x0F0F0F);

    /// <summary>Header text.</summary>
    public static readonly ThemeColor ForegroundHeader = ThemeColor.FromRgb(0xC8C8C8);

    /// <summary>Selected list rows and tree items.</summary>
    public static readonly ThemeColor Select = ThemeColor.FromRgb(0x0070E0);

    /// <summary>Selection while the widget does not have focus.</summary>
    public static readonly ThemeColor SelectInactive = ThemeColor.FromRgb(0x40576F);

    /// <summary>Ancestor highlight of a selected row.</summary>
    public static readonly ThemeColor SelectParent = ThemeColor.FromRgb(0x2C323A);

    /// <summary>Hovered row inside a list; the engine points this at <see cref="Panel"/>.</summary>
    public static readonly ThemeColor SelectHover = ThemeColor.FromRgb(0x242424);

    /// <summary>Notification toast background.</summary>
    public static readonly ThemeColor Notifications = ThemeColor.FromRgb(0x464B50);

    /// <summary>Blue accent.</summary>
    public static readonly ThemeColor AccentBlue = ThemeColor.FromRgb(0x26BBFF);

    /// <summary>Purple accent.</summary>
    public static readonly ThemeColor AccentPurple = ThemeColor.FromRgb(0xA139BF);

    /// <summary>Pink accent.</summary>
    public static readonly ThemeColor AccentPink = ThemeColor.FromRgb(0xFF729C);

    /// <summary>Red accent.</summary>
    public static readonly ThemeColor AccentRed = ThemeColor.FromRgb(0xFF4040);

    /// <summary>Orange accent.</summary>
    public static readonly ThemeColor AccentOrange = ThemeColor.FromRgb(0xFE9B07);

    /// <summary>Yellow accent.</summary>
    public static readonly ThemeColor AccentYellow = ThemeColor.FromRgb(0xFFDC1A);

    /// <summary>Green accent.</summary>
    public static readonly ThemeColor AccentGreen = ThemeColor.FromRgb(0x8BC24A);

    /// <summary>Brown accent.</summary>
    public static readonly ThemeColor AccentBrown = ThemeColor.FromRgb(0x804D39);

    /// <summary>Black accent, matched to the panel.</summary>
    public static readonly ThemeColor AccentBlack = ThemeColor.FromRgb(0x242424);

    /// <summary>Grey accent; the engine's "disabled" text.</summary>
    public static readonly ThemeColor AccentGray = ThemeColor.FromRgb(0x808080);

    /// <summary>White accent.</summary>
    public static readonly ThemeColor AccentWhite = ThemeColor.FromRgb(0xFFFFFF);

    /// <summary>Folder icon accent.</summary>
    public static readonly ThemeColor AccentFolder = ThemeColor.FromRgb(0xB68F55);

    /// <summary>Warning: log warnings, pending states.</summary>
    public static readonly ThemeColor Warning = ThemeColor.FromRgb(0xFFB800);

    /// <summary>Error: log errors, failures.</summary>
    public static readonly ThemeColor Error = ThemeColor.FromRgb(0xEF3535);

    /// <summary>Success: connected states, confirmations.</summary>
    public static readonly ThemeColor Success = ThemeColor.FromRgb(0x1FE44B);

    // ---- StarshipCoreStyle.cpp ------------------------------------------------------------------------

    /// <summary>
    /// The editor's <c>SelectionColor</c> (<c>StarshipCoreStyle.cpp</c>, <c>COLOR("18A0FBFF")</c>): the
    /// brighter blue of drop indicators, selector outlines and drag affordances. Distinct from
    /// <see cref="Select"/>, which fills selected rows.
    /// </summary>
    public static readonly ThemeColor Selection = ThemeColor.FromRgb(0x18A0FB);

    /// <summary>
    /// The pressed counterpart of <see cref="Selection"/>: <c>StarshipCoreStyle.cpp</c> initialises
    /// <c>SelectionColor_Pressed_LinearRef</c> with the *linear* triple (0.701, 0.225, 0.003) rather than an
    /// sRGB hex, so this is that value sRGB-encoded (#DA820A). The engine's toolbar buttons paint it while
    /// pressed.
    /// </summary>
    public static readonly ThemeColor SelectionPressed = ThemeColor.FromRgb(0xDA820A);

    /// <summary>
    /// A hovered dock tab: <c>Panel.CopyWithNewOpacity(0.8)</c> over the tab well (<see cref="Background"/>),
    /// blended in sRGB as Slate does — <c>0.8 * 0x24 + 0.2 * 0x15 = 0x21</c>.
    /// </summary>
    public static readonly ThemeColor TabHover = ThemeColor.FromRgb(0x212121);

    /// <summary>
    /// The disabled-text grey the engine uses for inactive labels; same value as <see cref="AccentGray"/>.
    /// </summary>
    public static readonly ThemeColor TextDisabled = AccentGray;
}
