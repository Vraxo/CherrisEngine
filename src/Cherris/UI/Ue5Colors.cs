using System.Numerics;

namespace Cherris.UI;

/// <summary>
/// An opaque-or-not 8-bit sRGB color, the way the Unreal Engine 5 editor palette stores its values.
/// </summary>
/// <param name="R">Red, 0-255.</param>
/// <param name="G">Green, 0-255.</param>
/// <param name="B">Blue, 0-255.</param>
/// <param name="A">Alpha, 0-255.</param>
public readonly record struct Ue5Color(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Builds a color from a packed <c>0xRRGGBB</c> value.</summary>
    /// <param name="rgb">Packed red, green and blue.</param>
    /// <param name="a">Alpha, 0-255.</param>
    public static Ue5Color FromRgb(uint rgb, byte a = 255)
    {
        return new Ue5Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);
    }

    /// <summary>
    /// The color as <c>#rrggbb</c>, or <c>#rrggbbaa</c> when it is translucent — lower case, because that is
    /// how the stylesheet spells the same values.
    /// </summary>
    public string Hex => A == 255
        ? $"#{R:x2}{G:x2}{B:x2}"
        : $"#{R:x2}{G:x2}{B:x2}{A:x2}";

    /// <summary>The color as an ImGui color vector (0..1 per channel).</summary>
    public Vector4 ToVector4()
    {
        return new Vector4(R / 255f, G / 255f, B / 255f, A / 255f);
    }

    /// <summary>Returns the same RGB with a different alpha, for the translucent ImGui roles.</summary>
    /// <param name="alpha">New alpha, 0..1.</param>
    public Ue5Color WithAlpha(float alpha)
    {
        return this with { A = (byte)Math.Round(Math.Clamp(alpha, 0f, 1f) * 255f) };
    }
}

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
    public static readonly Ue5Color Black = Ue5Color.FromRgb(0x000000);

    /// <summary>Window title bars.</summary>
    public static readonly Ue5Color Title = Ue5Color.FromRgb(0x151515);

    /// <summary>The editor's base surface: tab wells, toolbars, empty docking space.</summary>
    public static readonly Ue5Color Background = Ue5Color.FromRgb(0x151515);

    /// <summary>1px window borders.</summary>
    public static readonly Ue5Color WindowBorder = Ue5Color.FromRgb(0x0F0F0F);

    /// <summary>Collapsible area background.</summary>
    public static readonly Ue5Color Foldout = Ue5Color.FromRgb(0x0F0F0F);

    /// <summary>Text input and button fill.</summary>
    public static readonly Ue5Color Input = Ue5Color.FromRgb(0x0F0F0F);

    /// <summary>Input border; also the default divider weight.</summary>
    public static readonly Ue5Color InputOutline = Ue5Color.FromRgb(0x383838);

    /// <summary>Recessed surfaces: list view rows, separators, progress bar troughs.</summary>
    public static readonly Ue5Color Recessed = Ue5Color.FromRgb(0x1A1A1A);

    /// <summary>Docked panel background and active tab fill.</summary>
    public static readonly Ue5Color Panel = Ue5Color.FromRgb(0x242424);

    /// <summary>Panel headers and category headers.</summary>
    public static readonly Ue5Color Header = Ue5Color.FromRgb(0x2F2F2F);

    /// <summary>Combo boxes, tooltips and menu popups.</summary>
    public static readonly Ue5Color Dropdown = Ue5Color.FromRgb(0x383838);

    /// <summary>Border of dropdowns, tooltips and menu popups.</summary>
    public static readonly Ue5Color DropdownOutline = Ue5Color.FromRgb(0x4C4C4C);

    /// <summary>Hover highlight: menu bar buttons, scrollbar thumbs, outline of hovered inputs.</summary>
    public static readonly Ue5Color Hover = Ue5Color.FromRgb(0x575757);

    /// <summary>Stronger hover, used for hovered/dragged scrollbar thumbs.</summary>
    public static readonly Ue5Color Hover2 = Ue5Color.FromRgb(0x808080);

    /// <summary>Pure white, for icons and check marks.</summary>
    public static readonly Ue5Color White = Ue5Color.FromRgb(0xFFFFFF);

    /// <summary>White at 25%: menu separators and headings.</summary>
    public static readonly Ue5Color White25 = Ue5Color.FromRgb(0xFFFFFF, 0x40);

    /// <summary>The highlight/underline blue.</summary>
    public static readonly Ue5Color Highlight = Ue5Color.FromRgb(0x0070E0);

    /// <summary>The primary action blue: selection fills, focus outlines, the active tab indicator.</summary>
    public static readonly Ue5Color Primary = Ue5Color.FromRgb(0x0070E0);

    /// <summary>Primary, hovered.</summary>
    public static readonly Ue5Color PrimaryHover = Ue5Color.FromRgb(0x0E86FF);

    /// <summary>Primary, pressed.</summary>
    public static readonly Ue5Color PrimaryPress = Ue5Color.FromRgb(0x0050A0);

    /// <summary>Secondary fill for toggles and split handles.</summary>
    public static readonly Ue5Color Secondary = Ue5Color.FromRgb(0x383838);

    /// <summary>Default text color.</summary>
    public static readonly Ue5Color Foreground = Ue5Color.FromRgb(0xC0C0C0);

    /// <summary>Hovered/active text color.</summary>
    public static readonly Ue5Color ForegroundHover = Ue5Color.FromRgb(0xFFFFFF);

    /// <summary>Text on a light fill; falls back to the input fill.</summary>
    public static readonly Ue5Color ForegroundInverted = Ue5Color.FromRgb(0x0F0F0F);

    /// <summary>Header text.</summary>
    public static readonly Ue5Color ForegroundHeader = Ue5Color.FromRgb(0xC8C8C8);

    /// <summary>Selected list rows and tree items.</summary>
    public static readonly Ue5Color Select = Ue5Color.FromRgb(0x0070E0);

    /// <summary>Selection while the widget does not have focus.</summary>
    public static readonly Ue5Color SelectInactive = Ue5Color.FromRgb(0x40576F);

    /// <summary>Ancestor highlight of a selected row.</summary>
    public static readonly Ue5Color SelectParent = Ue5Color.FromRgb(0x2C323A);

    /// <summary>Hovered row inside a list; the engine points this at <see cref="Panel"/>.</summary>
    public static readonly Ue5Color SelectHover = Ue5Color.FromRgb(0x242424);

    /// <summary>Notification toast background.</summary>
    public static readonly Ue5Color Notifications = Ue5Color.FromRgb(0x464B50);

    /// <summary>Blue accent.</summary>
    public static readonly Ue5Color AccentBlue = Ue5Color.FromRgb(0x26BBFF);

    /// <summary>Purple accent.</summary>
    public static readonly Ue5Color AccentPurple = Ue5Color.FromRgb(0xA139BF);

    /// <summary>Pink accent.</summary>
    public static readonly Ue5Color AccentPink = Ue5Color.FromRgb(0xFF729C);

    /// <summary>Red accent.</summary>
    public static readonly Ue5Color AccentRed = Ue5Color.FromRgb(0xFF4040);

    /// <summary>Orange accent.</summary>
    public static readonly Ue5Color AccentOrange = Ue5Color.FromRgb(0xFE9B07);

    /// <summary>Yellow accent.</summary>
    public static readonly Ue5Color AccentYellow = Ue5Color.FromRgb(0xFFDC1A);

    /// <summary>Green accent.</summary>
    public static readonly Ue5Color AccentGreen = Ue5Color.FromRgb(0x8BC24A);

    /// <summary>Brown accent.</summary>
    public static readonly Ue5Color AccentBrown = Ue5Color.FromRgb(0x804D39);

    /// <summary>Black accent, matched to the panel.</summary>
    public static readonly Ue5Color AccentBlack = Ue5Color.FromRgb(0x242424);

    /// <summary>Grey accent; the engine's "disabled" text.</summary>
    public static readonly Ue5Color AccentGray = Ue5Color.FromRgb(0x808080);

    /// <summary>White accent.</summary>
    public static readonly Ue5Color AccentWhite = Ue5Color.FromRgb(0xFFFFFF);

    /// <summary>Folder icon accent.</summary>
    public static readonly Ue5Color AccentFolder = Ue5Color.FromRgb(0xB68F55);

    /// <summary>Warning: log warnings, pending states.</summary>
    public static readonly Ue5Color Warning = Ue5Color.FromRgb(0xFFB800);

    /// <summary>Error: log errors, failures.</summary>
    public static readonly Ue5Color Error = Ue5Color.FromRgb(0xEF3535);

    /// <summary>Success: connected states, confirmations.</summary>
    public static readonly Ue5Color Success = Ue5Color.FromRgb(0x1FE44B);

    // ---- StarshipCoreStyle.cpp ------------------------------------------------------------------------

    /// <summary>
    /// The editor's <c>SelectionColor</c> (<c>StarshipCoreStyle.cpp</c>, <c>COLOR("18A0FBFF")</c>): the
    /// brighter blue of drop indicators, selector outlines and drag affordances. Distinct from
    /// <see cref="Select"/>, which fills selected rows.
    /// </summary>
    public static readonly Ue5Color Selection = Ue5Color.FromRgb(0x18A0FB);

    /// <summary>
    /// The pressed counterpart of <see cref="Selection"/>: <c>StarshipCoreStyle.cpp</c> initialises
    /// <c>SelectionColor_Pressed_LinearRef</c> with the *linear* triple (0.701, 0.225, 0.003) rather than an
    /// sRGB hex, so this is that value sRGB-encoded (#DA820A). The engine's toolbar buttons paint it while
    /// pressed.
    /// </summary>
    public static readonly Ue5Color SelectionPressed = Ue5Color.FromRgb(0xDA820A);

    /// <summary>
    /// A hovered dock tab: <c>Panel.CopyWithNewOpacity(0.8)</c> over the tab well (<see cref="Background"/>),
    /// blended in sRGB as Slate does — <c>0.8 * 0x24 + 0.2 * 0x15 = 0x21</c>.
    /// </summary>
    public static readonly Ue5Color TabHover = Ue5Color.FromRgb(0x212121);

    /// <summary>
    /// The disabled-text grey the engine uses for inactive labels; same value as <see cref="AccentGray"/>.
    /// </summary>
    public static readonly Ue5Color TextDisabled = AccentGray;
}
