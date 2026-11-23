using Cherris.UI;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor;

/// <summary>
/// Applies the Unreal Engine 5 editor theme to ImGui.
/// </summary>
/// <remarks>
/// <para>
/// Every color comes from <see cref="Ue5Colors"/>, which holds the engine's own palette values, so the
/// editor looks like UE5 rather than like an approximation of it. The role mapping below follows the way
/// <c>StarshipCoreStyle.cpp</c> uses those theme colors:
/// </para>
/// <list type="bullet">
/// <item>Windows are <see cref="Ue5Colors.Panel"/> (#242424), the docked-panel color.</item>
/// <item>Child regions are <see cref="Ue5Colors.Recessed"/> (#1A1A1A), the list-view row color.</item>
/// <item>Inputs are <see cref="Ue5Colors.Input"/> (#0F0F0F) inside an <see cref="Ue5Colors.InputOutline"/>.</item>
/// <item>
/// Buttons are <see cref="Ue5Colors.Secondary"/>, hovered <see cref="Ue5Colors.Hover"/>, pressed
/// <see cref="Ue5Colors.Header"/> — the three fills of the engine's <c>FButtonStyle</c>.
/// </item>
/// <item>Selected rows are <see cref="Ue5Colors.Select"/>, the table-row selection fill.</item>
/// <item>Text is <see cref="Ue5Colors.Foreground"/>, hovered text <see cref="Ue5Colors.ForegroundHover"/>.</item>
/// <item>
/// Tabs use the engine's dock-tab brushes: the tab well is <see cref="Ue5Colors.Background"/>, the active
/// tab is the panel color, and hovering blends <see cref="Ue5Colors.TabHover"/>.
/// </item>
/// </list>
/// <para>
/// A few ImGui roles have no single equivalent. Where a role needs transparency (docking preview, modal dim)
/// the alpha comes from ImGui's own defaults and the RGB stays a palette color; each such case is called out
/// at the line that sets it.
/// </para>
/// </remarks>
public static class EditorTheme
{
    public static void ApplyUnrealEngineStyle()
    {
        ImGuiStylePtr style = ImGui.GetStyle();

        // ---- UE5 palette, by the role StarshipCoreStyle gives it ------------------------------------

        style.Colors[(int)ImGuiCol.Text] = Ue5Colors.Foreground.ToVector4();
        style.Colors[(int)ImGuiCol.TextDisabled] = Ue5Colors.TextDisabled.ToVector4();

        style.Colors[(int)ImGuiCol.WindowBg] = Ue5Colors.Panel.ToVector4();
        style.Colors[(int)ImGuiCol.ChildBg] = Ue5Colors.Recessed.ToVector4();
        style.Colors[(int)ImGuiCol.PopupBg] = Ue5Colors.Dropdown.ToVector4();
        style.Colors[(int)ImGuiCol.Border] = Ue5Colors.WindowBorder.ToVector4();
        style.Colors[(int)ImGuiCol.BorderShadow] = Ue5Colors.Black.WithAlpha(0f).ToVector4();

        // The engine's input fill stays #0F0F0F; hover and focus only repaint the 1px outline
        // (InputOutline -> Hover -> Primary). ImGui fills the whole frame, so the outline color becomes the
        // fill for those states.
        style.Colors[(int)ImGuiCol.FrameBg] = Ue5Colors.Input.ToVector4();
        style.Colors[(int)ImGuiCol.FrameBgHovered] = Ue5Colors.InputOutline.ToVector4();
        style.Colors[(int)ImGuiCol.FrameBgActive] = Ue5Colors.Primary.ToVector4();

        style.Colors[(int)ImGuiCol.TitleBg] = Ue5Colors.Title.ToVector4();
        style.Colors[(int)ImGuiCol.TitleBgActive] = Ue5Colors.Title.ToVector4();
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = Ue5Colors.Title.ToVector4();
        style.Colors[(int)ImGuiCol.MenuBarBg] = Ue5Colors.Background.ToVector4();

        // FScrollBarStyle leaves the track unpainted and draws rounded thumbs in Hover / Hover2.
        style.Colors[(int)ImGuiCol.ScrollbarBg] = Ue5Colors.Background.WithAlpha(0f).ToVector4();
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = Ue5Colors.Hover.ToVector4();
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = Ue5Colors.Hover2.ToVector4();
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = Ue5Colors.Hover2.ToVector4();

        style.Colors[(int)ImGuiCol.CheckMark] = Ue5Colors.White.ToVector4();
        style.Colors[(int)ImGuiCol.SliderGrab] = Ue5Colors.Primary.ToVector4();
        style.Colors[(int)ImGuiCol.SliderGrabActive] = Ue5Colors.PrimaryHover.ToVector4();

        // FButtonStyle: Secondary fill, Hover fill when hovered, Header fill when pressed.
        style.Colors[(int)ImGuiCol.Button] = Ue5Colors.Secondary.ToVector4();
        style.Colors[(int)ImGuiCol.ButtonHovered] = Ue5Colors.Hover.ToVector4();
        style.Colors[(int)ImGuiCol.ButtonActive] = Ue5Colors.Header.ToVector4();

        // FTableRowStyle: Select fills the selected row, SelectHover the hovered one.
        style.Colors[(int)ImGuiCol.Header] = Ue5Colors.Select.ToVector4();
        style.Colors[(int)ImGuiCol.HeaderHovered] = Ue5Colors.SelectHover.ToVector4();
        style.Colors[(int)ImGuiCol.HeaderActive] = Ue5Colors.Select.ToVector4();

        style.Colors[(int)ImGuiCol.Separator] = Ue5Colors.Recessed.ToVector4();
        // ImGui has no separate separator affordances in UE5; both borrow the drag/selection blue.
        style.Colors[(int)ImGuiCol.SeparatorHovered] = Ue5Colors.Selection.WithAlpha(0.78f).ToVector4();
        style.Colors[(int)ImGuiCol.SeparatorActive] = Ue5Colors.Selection.ToVector4();

        style.Colors[(int)ImGuiCol.ResizeGrip] = Ue5Colors.Panel.WithAlpha(0f).ToVector4();
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = Ue5Colors.Selection.WithAlpha(0.5f).ToVector4();
        style.Colors[(int)ImGuiCol.ResizeGripActive] = Ue5Colors.Primary.ToVector4();

        // Dock tabs: the well is Background (the tab itself is unpainted), the active tab is the panel
        // color, and a hovered tab is Panel at 80% over the well.
        style.Colors[(int)ImGuiCol.Tab] = Ue5Colors.Background.ToVector4();
        style.Colors[(int)ImGuiCol.TabHovered] = Ue5Colors.TabHover.ToVector4();
        // Not available in the ImGui.NET build this editor uses.
        // style.Colors[(int)ImGuiCol.TabActive] = Ue5Colors.Panel.ToVector4();
        // style.Colors[(int)ImGuiCol.TabUnfocused] = Ue5Colors.Background.ToVector4();
        // style.Colors[(int)ImGuiCol.TabUnfocusedActive] = Ue5Colors.Panel.ToVector4();

        // ImGui draws the docking preview translucently; the RGB stays Primary.
        style.Colors[(int)ImGuiCol.DockingPreview] = Ue5Colors.Primary.WithAlpha(0.7f).ToVector4();
        style.Colors[(int)ImGuiCol.DockingEmptyBg] = Ue5Colors.Background.ToVector4();

        style.Colors[(int)ImGuiCol.PlotLines] = Ue5Colors.AccentBlue.ToVector4();
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = Ue5Colors.AccentOrange.ToVector4();
        style.Colors[(int)ImGuiCol.PlotHistogram] = Ue5Colors.AccentYellow.ToVector4();
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = Ue5Colors.AccentOrange.ToVector4();

        style.Colors[(int)ImGuiCol.TableHeaderBg] = Ue5Colors.Header.ToVector4();
        style.Colors[(int)ImGuiCol.TableBorderStrong] = Ue5Colors.DropdownOutline.ToVector4();
        style.Colors[(int)ImGuiCol.TableBorderLight] = Ue5Colors.InputOutline.ToVector4();
        // Rows are painted by the table view itself in UE5, so ImGui's stripes stay out of the way.
        style.Colors[(int)ImGuiCol.TableRowBg] = Ue5Colors.Black.WithAlpha(0f).ToVector4();
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = Ue5Colors.Black.WithAlpha(0f).ToVector4();

        // SEditableText paints the selected text with the Highlight color.
        style.Colors[(int)ImGuiCol.TextSelectedBg] = Ue5Colors.Highlight.WithAlpha(0.35f).ToVector4();

        // The engine's drop target color is SelectionColor (#18A0FB), not the row-selection blue.
        style.Colors[(int)ImGuiCol.DragDropTarget] = Ue5Colors.Selection.WithAlpha(0.9f).ToVector4();

        // style.Colors[(int)ImGuiCol.NavHighlight] = Ue5Colors.Primary.ToVector4();
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = Ue5Colors.White.WithAlpha(0.7f).ToVector4();
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = Ue5Colors.Black.WithAlpha(0.2f).ToVector4();
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = Ue5Colors.Black.WithAlpha(0.6f).ToVector4();

        // ---- metrics -------------------------------------------------------------------------------
        // UE5 rounds its inputs, buttons, scrollbar thumbs and tab corners by 4px; windows stay square.

        style.WindowPadding = new Vector2(8, 8);
        style.FramePadding = new Vector2(5, 4);
        style.CellPadding = new Vector2(4, 2);
        style.ItemSpacing = new Vector2(8, 4);
        style.ItemInnerSpacing = new Vector2(4, 4);
        style.TouchExtraPadding = new Vector2(0, 0);
        style.IndentSpacing = 21;
        style.ScrollbarSize = 8;
        style.GrabMinSize = 10;

        style.WindowBorderSize = 1;
        style.ChildBorderSize = 1;
        style.PopupBorderSize = 1;
        style.FrameBorderSize = 1;
        style.TabBorderSize = 1;

        style.WindowRounding = 0;
        style.ChildRounding = 0;
        style.FrameRounding = 4;
        style.PopupRounding = 0;
        style.ScrollbarRounding = 4;
        style.GrabRounding = 4;
        style.LogSliderDeadzone = 0;
        style.TabRounding = 4;
    }
}
