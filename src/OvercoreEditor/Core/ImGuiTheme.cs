using Overcore.UI;
using ImGuiNET;
using System.Numerics;

namespace OvercoreEditor;

/// <summary>
/// Maps an <see cref="EditorTheme"/> onto ImGui's style: colors, rounding, borders and density.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is the same for every theme, because it is a statement about what a widget <em>is</em>: windows
/// are <see cref="ThemeRoles.Panel"/>, child regions and list wells are <see cref="ThemeRoles.Recessed"/>,
/// inputs are <see cref="ThemeRoles.Input"/> inside an <see cref="ThemeRoles.InputOutline"/>, buttons run
/// <see cref="ThemeRoles.Secondary"/> → <see cref="ThemeRoles.Hover"/> → <see cref="ThemeRoles.Header"/>, rows
/// run <see cref="ThemeRoles.SelectHover"/> → <see cref="ThemeRoles.Select"/>, and text is
/// <see cref="ThemeRoles.Foreground"/>. Each theme supplies the values; see <see cref="Ue5StarshipTheme"/> and
/// <see cref="UnityDarkTheme"/> for where those come from.
/// </para>
/// <para>
/// Menus and popups follow Unity's context-menu look rather than UE5's: a near-black
/// <see cref="ThemeRoles.Background"/> surface, a mid-grey <see cref="ThemeRoles.Header"/> hover fill,
/// <see cref="ThemeRoles.Foreground"/> text with <see cref="ThemeRoles.AccentGray"/> for disabled items, and a
/// <see cref="ThemeRoles.DropdownOutline"/> hairline separator. Sampled from Unity's Project-window menu
/// (#141414 background, #3c3c3c hover, #d2d2d2 text, #727272 disabled, #373737 separator), those roles land
/// within a few levels of the sample in both themes without changing any published palette value.
/// </para>
/// <para>
/// Rounding, border width, scrollbar width and frame padding come from <see cref="EditorTheme.Metrics"/>, so a
/// theme with 18px controls and 3px corners looks like one rather than like the engine's 22px and 4px with
/// different paint.
/// </para>
/// <para>
/// A few ImGui roles have no single equivalent. Where a role needs transparency (docking preview, modal dim)
/// the alpha comes from ImGui's own defaults and the RGB stays a theme color; each such case is called out at
/// the line that sets it.
/// </para>
/// </remarks>
public static class ImGuiTheme
{
    public static void Apply(EditorTheme theme)
    {
        ImGuiStylePtr style = ImGui.GetStyle();

        // ---- colors, by the role the source style gives it ------------------------------------------

        style.Colors[(int)ImGuiCol.Text] = theme[ThemeRoles.Foreground].ToVector4();
        style.Colors[(int)ImGuiCol.TextDisabled] = theme[ThemeRoles.AccentGray].ToVector4();

        style.Colors[(int)ImGuiCol.WindowBg] = theme[ThemeRoles.Panel].ToVector4();
        style.Colors[(int)ImGuiCol.ChildBg] = theme[ThemeRoles.Recessed].ToVector4();
        // Menus and popups sit on Background, the darkest surface, like Unity's context menus (#141414 sampled).
        // Dropdown stays for dialogs and other raised surfaces in the Blazor editor, which reads it directly.
        style.Colors[(int)ImGuiCol.PopupBg] = theme[ThemeRoles.Background].ToVector4();
        style.Colors[(int)ImGuiCol.Border] = theme[ThemeRoles.WindowBorder].ToVector4();
        style.Colors[(int)ImGuiCol.BorderShadow] = theme[ThemeRoles.Black].WithAlpha(0f).ToVector4();

        // The input fill stays put; hover and focus only repaint the outline (InputOutline -> Hover ->
        // Primary). ImGui fills the whole frame, so the outline color becomes the fill for those states.
        style.Colors[(int)ImGuiCol.FrameBg] = theme[ThemeRoles.Input].ToVector4();
        style.Colors[(int)ImGuiCol.FrameBgHovered] = theme[ThemeRoles.InputOutline].ToVector4();
        style.Colors[(int)ImGuiCol.FrameBgActive] = theme[ThemeRoles.Primary].ToVector4();

        style.Colors[(int)ImGuiCol.TitleBg] = theme[ThemeRoles.Title].ToVector4();
        style.Colors[(int)ImGuiCol.TitleBgActive] = theme[ThemeRoles.Title].ToVector4();
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = theme[ThemeRoles.Title].ToVector4();
        style.Colors[(int)ImGuiCol.MenuBarBg] = theme[ThemeRoles.Background].ToVector4();

        // The scrollbar track is unpainted and the thumbs are rounded, in Hover / Hover2.
        style.Colors[(int)ImGuiCol.ScrollbarBg] = theme[ThemeRoles.Background].WithAlpha(0f).ToVector4();
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = theme[ThemeRoles.Hover].ToVector4();
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = theme[ThemeRoles.Hover2].ToVector4();
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = theme[ThemeRoles.Hover2].ToVector4();

        style.Colors[(int)ImGuiCol.CheckMark] = theme[ThemeRoles.White].ToVector4();
        style.Colors[(int)ImGuiCol.SliderGrab] = theme[ThemeRoles.Primary].ToVector4();
        style.Colors[(int)ImGuiCol.SliderGrabActive] = theme[ThemeRoles.PrimaryHover].ToVector4();

        // FButtonStyle: Secondary fill, Hover fill when hovered, Header fill when pressed.
        style.Colors[(int)ImGuiCol.Button] = theme[ThemeRoles.Secondary].ToVector4();
        style.Colors[(int)ImGuiCol.ButtonHovered] = theme[ThemeRoles.Hover].ToVector4();
        style.Colors[(int)ImGuiCol.ButtonActive] = theme[ThemeRoles.Header].ToVector4();

        // FTableRowStyle: Select fills the selected row. The hovered and pressed fills are Header, the mid-grey
        // Unity uses for a hovered menu item (#3c3c3c sampled; Unity's Header is exactly that), so a menu hover
        // reads as a grey bar rather than the row-hover Panel it used to be.
        style.Colors[(int)ImGuiCol.Header] = theme[ThemeRoles.Select].ToVector4();
        style.Colors[(int)ImGuiCol.HeaderHovered] = theme[ThemeRoles.Header].ToVector4();
        style.Colors[(int)ImGuiCol.HeaderActive] = theme[ThemeRoles.Header].ToVector4();

        // A hairline that stays visible on both the dark menu surface and the lighter panel surface.
        style.Colors[(int)ImGuiCol.Separator] = theme[ThemeRoles.DropdownOutline].ToVector4();
        // ImGui has no separate separator affordances in UE5; both borrow the drag/selection blue.
        style.Colors[(int)ImGuiCol.SeparatorHovered] = theme[ThemeRoles.Selection].WithAlpha(0.78f).ToVector4();
        style.Colors[(int)ImGuiCol.SeparatorActive] = theme[ThemeRoles.Selection].ToVector4();

        style.Colors[(int)ImGuiCol.ResizeGrip] = theme[ThemeRoles.Panel].WithAlpha(0f).ToVector4();
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = theme[ThemeRoles.Selection].WithAlpha(0.5f).ToVector4();
        style.Colors[(int)ImGuiCol.ResizeGripActive] = theme[ThemeRoles.Primary].ToVector4();

        // Dock tabs: the well is Background (the tab itself is unpainted), the active tab is the panel
        // color, and a hovered tab is Panel at 80% over the well.
        style.Colors[(int)ImGuiCol.Tab] = theme[ThemeRoles.Background].ToVector4();
        style.Colors[(int)ImGuiCol.TabHovered] = theme[ThemeRoles.TabHover].ToVector4();
        // Not available in the ImGui.NET build this editor uses.
        // style.Colors[(int)ImGuiCol.TabActive] = theme[ThemeRoles.Panel].ToVector4();
        // style.Colors[(int)ImGuiCol.TabUnfocused] = theme[ThemeRoles.Background].ToVector4();
        // style.Colors[(int)ImGuiCol.TabUnfocusedActive] = theme[ThemeRoles.Panel].ToVector4();

        // ImGui draws the docking preview translucently; the RGB stays Primary.
        style.Colors[(int)ImGuiCol.DockingPreview] = theme[ThemeRoles.Primary].WithAlpha(0.7f).ToVector4();
        style.Colors[(int)ImGuiCol.DockingEmptyBg] = theme[ThemeRoles.Background].ToVector4();

        style.Colors[(int)ImGuiCol.PlotLines] = theme[ThemeRoles.AccentBlue].ToVector4();
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = theme[ThemeRoles.AccentOrange].ToVector4();
        style.Colors[(int)ImGuiCol.PlotHistogram] = theme[ThemeRoles.AccentYellow].ToVector4();
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = theme[ThemeRoles.AccentOrange].ToVector4();

        style.Colors[(int)ImGuiCol.TableHeaderBg] = theme[ThemeRoles.Header].ToVector4();
        style.Colors[(int)ImGuiCol.TableBorderStrong] = theme[ThemeRoles.DropdownOutline].ToVector4();
        style.Colors[(int)ImGuiCol.TableBorderLight] = theme[ThemeRoles.InputOutline].ToVector4();
        // The table view paints its own rows, so ImGui's stripes stay out of the way.
        style.Colors[(int)ImGuiCol.TableRowBg] = theme[ThemeRoles.Black].WithAlpha(0f).ToVector4();
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = theme[ThemeRoles.Black].WithAlpha(0f).ToVector4();

        // Selected text is painted with the Highlight color.
        style.Colors[(int)ImGuiCol.TextSelectedBg] = theme[ThemeRoles.Highlight].WithAlpha(0.35f).ToVector4();

        // The drop target is the selector color, not the row-selection blue.
        style.Colors[(int)ImGuiCol.DragDropTarget] = theme[ThemeRoles.Selection].WithAlpha(0.9f).ToVector4();

        // style.Colors[(int)ImGuiCol.NavHighlight] = theme[ThemeRoles.Primary].ToVector4();
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = theme[ThemeRoles.White].WithAlpha(0.7f).ToVector4();
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = theme[ThemeRoles.Black].WithAlpha(0.2f).ToVector4();
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = theme[ThemeRoles.Black].WithAlpha(0.6f).ToVector4();

        // ---- metrics -------------------------------------------------------------------------------
        // Density comes from the theme: its control height decides the frame padding, its corner radius the
        // rounding, its border width the borders. UE5's 22px control and 4px corners reproduce Slate's layout;
        // Unity's 18px control and 3px corners reproduce its own.

        float fontPixels = theme.Typography.BodyPixels;
        float innerHeight = MathF.Max(theme.Metrics.ControlHeight - fontPixels - 2f * theme.Metrics.BorderWidth, 0f);
        float paddingY = MathF.Round(innerHeight / 2f * 2f) / 2f;

        style.WindowPadding = new Vector2(8, 8);
        style.FramePadding = new Vector2(8, paddingY);
        style.CellPadding = new Vector2(4, 2);
        style.ItemSpacing = new Vector2(8, 4);
        style.ItemInnerSpacing = new Vector2(4, 4);
        style.TouchExtraPadding = new Vector2(0, 0);
        style.IndentSpacing = 21;
        style.ScrollbarSize = theme.Metrics.ScrollbarWidth;
        style.GrabMinSize = 10;

        int border = (int)MathF.Round(theme.Metrics.BorderWidth);

        style.WindowBorderSize = border;
        style.ChildBorderSize = border;
        style.PopupBorderSize = border;
        style.FrameBorderSize = border;
        style.TabBorderSize = border;

        float radius = theme.Metrics.CornerRadius;

        // Windows, children and popups stay square in both source editors; only the controls are rounded.
        style.WindowRounding = 0;
        style.ChildRounding = 0;
        style.PopupRounding = 0;
        style.FrameRounding = radius;
        style.ScrollbarRounding = radius;
        style.GrabRounding = radius;
        style.LogSliderDeadzone = 0;
        style.TabRounding = radius;
    }
}
