# The UE5 editor palette, exactly

Both editors are themed with Unreal Engine 5's own color values rather than with colors picked to look
similar:

| Front end | Reads the palette from |
| :--- | :--- |
| `src/CherrisEditor` (ImGui) | `Cherris.UI.Ue5Colors` (`src/Cherris/UI/Ue5Colors.cs`), applied by `EditorTheme.ApplyUnrealEngineStyle()` |
| `src/CherrisEditorPhotino` (Blazor) | `--ue-*` custom properties in `wwwroot/css/app.css` |

`tests/CherrisEditorWeb.Tests/Ue5PaletteTests.cs` parses the stylesheet and fails if any `--ue-*` value
drifts from `Ue5Colors`, so the two cannot quietly diverge.

## Where the numbers come from

- `Engine/Source/Runtime/SlateCore/Private/Styling/StyleColors.cpp` —
  `USlateThemeManager::InitalizeDefaults()`, the built-in **dark** theme. Every entry is
  `SetDefaultColor(EStyleColor::X, COLOR("#RRGGBBAA"))`.
- `StarshipCoreStyle.cpp` (same directory) — the widget styles that consume those colors, plus the two
  colors the style owns: `SelectionColor` (`COLOR("18A0FBFF")`) and the hovered-tab composite.
- `SDockTab.cpp` — the active tab indicator is a vertical-aligned (`VAlign_Bottom`) gradient of
  `Docking.Tab.ActiveTabIndicatorColor`.

`COLOR()` is `FLinearColor::FromSRGBColor(FColor::FromHex(HexValue))` (`StyleColors.h`), i.e. the literal in
the engine source is the **sRGB** color you see; the engine only stores it linearly. CSS and ImGui both work
in sRGB, so the hex values below are used verbatim.

## The palette

| `EStyleColor` | Value | Used by us for |
| :--- | :--- | :--- |
| `Black` | `#000000` | viewport backdrop, modal dim |
| `Title` | `#151515` | window title bars |
| `Background` | `#151515` | app surface, menu bar, tab well, docking background |
| `WindowBorder` | `#0F0F0F` | 1px borders on windows, panels, sections |
| `Foldout` | `#0F0F0F` | collapsible areas |
| `Input` | `#0F0F0F` | text input fill, code blocks |
| `InputOutline` | `#383838` | input border, light table border |
| `Recessed` | `#1A1A1A` | list/tree/log surfaces, separators |
| `Panel` | `#242424` | docked panel background, active tab |
| `Header` | `#2F2F2F` | panel headers, section headers, status strip, pressed buttons |
| `Dropdown` | `#383838` | menu popups, combo boxes, disabled buttons |
| `DropdownOutline` | `#4C4C4C` | menu/dropdown border |
| `Hover` | `#575757` | hover fills, scrollbar thumb, hovered input outline, inactive tab separator |
| `Hover2` | `#808080` | hovered/dragged scrollbar thumb |
| `White` | `#FFFFFF` | check marks, focused text, icons |
| `White25` | `#FFFFFF40` | menu separators |
| `Highlight` | `#0070E0` | selected-text background |
| `Primary` | `#0070E0` | selection fill, focus outline, active tab indicator, primary buttons |
| `PrimaryHover` | `#0E86FF` | primary button hover |
| `PrimaryPress` | `#0050A0` | primary button pressed |
| `Secondary` | `#383838` | standard button fill |
| `Foreground` | `#C0C0C0` | default text |
| `ForegroundHover` | `#FFFFFF` | hovered/selected text, button labels |
| `ForegroundInverted` | `#0F0F0F` | text on light fills |
| `ForegroundHeader` | `#C8C8C8` | header text |
| `Select` | `#0070E0` | selected rows and tree items |
| `SelectInactive` | `#40576F` | unfocused selection |
| `SelectParent` | `#2C323A` | ancestor highlight, selected tile fill |
| `SelectHover` | `#242424` | hovered row (points at `Panel`) |
| `Notifications` | `#464B50` | notification toasts, crash banner |
| `AccentBlue` | `#26BBFF` | plot lines, links |
| `AccentPurple` | `#A139BF` | accent |
| `AccentPink` | `#FF729C` | accent |
| `AccentRed` | `#FF4040` | accent |
| `AccentOrange` | `#FE9B07` | plot highlights |
| `AccentYellow` | `#FFDC1A` | plot bars |
| `AccentGreen` | `#8BC24A` | accent |
| `AccentBrown` | `#804D39` | accent |
| `AccentBlack` | `#242424` | accent |
| `AccentGray` | `#808080` | disabled/secondary text |
| `AccentWhite` | `#FFFFFF` | accent |
| `AccentFolder` | `#B68F55` | folder icons |
| `Warning` | `#FFB800` | log warnings, pending states |
| `Error` | `#EF3535` | log errors, failures |
| `Success` | `#1FE44B` | connected states |

Two more values come from `StarshipCoreStyle.cpp` rather than the theme table:

| Color | Value | Origin |
| :--- | :--- | :--- |
| `Selection` | `#18A0FB` | `SelectionColor_LinearRef(COLOR("18A0FBFF"))` — drop indicators, selector outlines, splitter drag |
| `TabHover` | `#212121` | a hovered dock tab is `Panel.CopyWithNewOpacity(0.8)` over the tab well `Background`; Slate blends in sRGB, so `0.8 × 0x24 + 0.2 × 0x15 = 0x21` |

## Where each color is applied

| Engine style | ImGui role | Blazor rule |
| :--- | :--- | :--- |
| `FEditableTextBoxStyle` (Input / InputOutline / hover `Hover` / focus `Primary` / read-only `Header`) | `FrameBg`, `FrameBgHovered`, `FrameBgActive` | `.imgui-input`, `.imgui-input:hover`, `:focus`, `[readonly]` |
| `FButtonStyle` (`Secondary` → `Hover` → `Header`, `Input` outline, `ForegroundHover` label, disabled `Dropdown`/`Recessed`) | `Button`, `ButtonHovered`, `ButtonActive` | `.imgui-button`, `.editor-button`, `.transport-button` |
| `PrimaryButton` (`Primary` → `PrimaryHover` → `PrimaryPress`) | — (ImGui has one button style) | `.editor-button.primary` |
| `FTableRowStyle` (rows `Recessed`, hover `SelectHover`, selected `Select`, text `Foreground`/`ForegroundHover`) | `Header`, `HeaderHovered`, `HeaderActive` | `.tree`, `.tree-row`, `.cb-grid`, `.cb-item` |
| `FDockTabStyle` (well `Background`, active tab `Panel`, hover `TabHover`, separator `Hover`, indicator `Primary`) | `Tab`, `TabHovered` | `.panel-tabstrip`, `.panel-tab`, `.panel-tab.active::after` |
| `Menu.Background` / `Menu.Outline` / `Menu.Button` (hover `Primary`) / `Menu.Separator` (`White25`) | `PopupBg` | `.menu-popup`, `.menu-item:hover`, `.menu-separator` |
| `WindowMenuBar.Button` (hover `Hover`, fore `Foreground`/`ForegroundHover`; open submenu `Primary`) | `MenuBarBg` | `.menubar`, `.menubar-button`, `.menubar-button.open` |
| `FScrollBarStyle` (unpainted track, `Hover`/`Hover2` thumbs, 8px) | `ScrollbarBg` (transparent), `ScrollbarGrab*` | `::-webkit-scrollbar-*` |
| `SSeparator` (`Recessed`) | `Separator` | `.imgui-separator` |
| `SEditableText` selection (`Highlight`) | `TextSelectedBg` | — |
| Docking drop targets (`SelectionColor`) | `DragDropTarget` | `.dock-splitter:hover` |

### Where ImGui forces a compromise

ImGui has one color per state where UE5 has a fill *and* an outline, and a few of its roles are translucent
by design. Those are the only places where a value is not literally one palette entry:

- `FrameBgHovered` / `FrameBgActive` use `InputOutline` / `Primary` as *fills*, because the engine signals
  hover and focus by repainting the outline of an input whose fill stays `Input`.
- `HeaderHovered` is the engine's generic `Hover` (`#575757`) rather than `SelectHover`, because ImGui paints
  the hover straight onto the window background; in Blazor, where the list sits on `Recessed`, the exact
  `SelectHover` (`#242424`) is used instead.
- `DockingPreview`, `SeparatorHovered`, `ResizeGripHovered`, `TextSelectedBg`, `DragDropTarget` and the two
  dim backgrounds keep ImGui's default alphas with palette RGB, since they are overlays.
- `TabActive` / `TabUnfocused*` / `NavHighlight` do not exist in the ImGui.NET build the editor references,
  so the active tab leans on `WindowBg` (which is `Panel`, as the engine's active tab brush is).

## Metrics

UE5 rounds inputs, buttons, scrollbar thumbs and tab corners by 4px and draws 1px borders; windows and menus
are square. The ImGui style now matches (`FrameRounding`/`GrabRounding`/`ScrollbarRounding`/`TabRounding` = 4,
`ScrollbarSize` = 8, 1px borders), and the stylesheet uses the same radii.
