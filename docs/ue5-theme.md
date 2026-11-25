# The UE5 editor look

Both editors can wear Unreal Engine 5's own values rather than ones picked to look similar. This page
documents that theme; how themes are loaded, selected and added is on [themes.md](themes.md).

| Front end | Gets the theme from |
| :--- | :--- |
| `src/OvercoreEditor` (ImGui) | `Overcore.UI.Ue5StarshipTheme`, applied by `ImGuiTheme.Apply(EditorTheme)` |
| `src/OvercoreEditorPhotino` (Blazor) | the same theme object, applied as `--theme-*` custom properties |

The palette itself lives in `src/Overcore/UI/Ue5Colors.cs`, mapped onto the theme's roles by
`src/Overcore/UI/Themes/Ue5StarshipTheme.cs`, and is also shipped as
`EditorResources/Themes/ue5-starship.json`. It is the default theme, so the stylesheet's `:root` block holds
exactly its values; `tests/OvercoreEditorSession.Tests/Ue5PaletteTests.cs` fails if they drift apart. Nothing
in `app.css` or in the ImGui theme names a color directly any more — both read roles, which is what lets the
theme change at runtime.

## How exact is this?

Three tiers, kept apart on purpose because they are not equally trustworthy:

1. **Copied verbatim.** Every palette entry (see [The palette](#the-palette)), the font family, the text
   sizes (`RegularTextSize`/`SmallTextSize`, `FStyleFonts`) and the numeric constants the engine shares from
   `Styling/CoreStyle.h` — `ButtonMargins`, `PressedButtonMargins`, `ToggleButtonMargins`, `InputFocusRadius`,
   `InputFocusThickness`, `Icon8x8`/`16x16`/`32x32`/`64x64`. These can be checked against the source line.
2. **Derived from a source line.** Values the engine computes rather than writes down: the 13px body text
   (10pt at 96 DPI), the hovered-tab composite (`Panel` at 80%), the pressed-toolbar blue
   (`SelectionColor_Pressed`, stored linear, `#DA820A` on screen). Each says how it was derived.
3. **Our approximation.** Layout the engine does not express as data — how much space a menu bar leaves
   between titles, the height of our own panels' toolbars, the size of our content-browser tiles, the details
   category header's lower rule. These follow the engine's *structure* (16px icons, 1px borders, 4px radii,
   13px text, `Recessed` list surfaces) but the numbers are ours, and this document says so where they appear.

The palette test proves the two front ends agree with each other; it cannot prove agreement with UE5. That is
what the source references in this document are for.

## Where the numbers come from

- `Engine/Source/Runtime/SlateCore/Private/Styling/StyleColors.cpp` —
  `USlateThemeManager::InitalizeDefaults()`, the built-in **dark** theme. Every entry is
  `SetDefaultColor(EStyleColor::X, COLOR("#RRGGBBAA"))`.
- `StarshipCoreStyle.cpp` (same directory) — the widget styles that consume those colors, plus the two
  colors the style owns: `SelectionColor` (`COLOR("18A0FBFF")`) and the hovered-tab composite.
- `Styling/CoreStyle.h` (SlateCore, public) — the shared numeric constants the styles use: `ButtonMargins`
  `(12, 1.5, 12, 1.5)`, `PressedButtonMargins` `(12, 2.5, 12, 0.5)`, `ToggleButtonMargins` `(16, 2)`,
  `InputFocusRadius` 4, `InputFocusThickness` 1, `Icon8x8`…`Icon64x64`.
- `Styling/SlateTypes.h` — the `FDockTabStyle` / `FToolBarStyle` / `FEditableTextBoxStyle` members the values
  above are assigned to.
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
- `HeaderHovered` is `SelectHover` (`#242424`), the engine's table-row hover, which is visible because child
  regions are `Recessed`. ImGui paints that hover flat onto the row instead of compositing it over the row
  brush the way `FTableRowStyle` does.
- `DockingPreview`, `SeparatorHovered`, `ResizeGripHovered`, `TextSelectedBg`, `DragDropTarget` and the two
  dim backgrounds keep ImGui's default alphas with palette RGB, since they are overlays.
- `TabActive` / `TabUnfocused*` / `NavHighlight` do not exist in the ImGui.NET build the editor references,
  so the active tab leans on `WindowBg` (which is `Panel`, as the engine's active tab brush is).

## Metrics

UE5 rounds inputs, buttons, scrollbar thumbs and tab corners by 4px and draws 1px borders; windows and menus
are square. The ImGui style now matches (`FrameRounding`/`GrabRounding`/`ScrollbarRounding`/`TabRounding` = 4,
`ScrollbarSize` = 8, 1px borders), and the stylesheet uses the same radii.

The sizes the stylesheet reproduces, with the line each one comes from:

| Widget (Slate) | Source | Blazor |
| --- | --- | --- |
| Menu bar padding `FMargin(12, 4)` | `WindowMenuBar.MenuBar.Padding` | `.menubar` (`4px 12px`) |
| Menu bar button padding `FMargin(0, 2)` | `Menu.Button` → `MenuBarButton` | `.menubar-button` (`2px` vertical; the 8px horizontal is ours — the engine leaves the gap between titles to `SMenuBar`, not to a margin) |
| Menu block padding `FMargin(12, 1, 5, 1)` | `Menu.Block.Padding` | `.menu-item` (`1px 5px 1px 12px`) |
| Menu separator padding `FMargin(12, 6, 12, 6)` | `Menu.Separator.Padding` | `.menu-separator` (`6px 12px`) |
| Button label padding `FMargin(12, 1.5, 12, 1.5)` | `ButtonMargins` | `.imgui-button` (`2px 12px`) |
| Pressed label shift `FMargin(12, 2.5, 12, 0.5)` | `PressedButtonMargins` | `.imgui-button:active` (`3px 12px 1px`) |
| Editable text padding `FMargin(8, 3, 8, 4)` | `NormalEditableTextBox` | `.imgui-input` (`3px 8px 4px`); ImGui's `FramePadding` is `8, 3` |
| Tab padding `FMargin(4, 3, 10, 4)` | `Docking.Tab` (`MinorTabStyle`) | `.panel-tab` (`3px 10px 4px`) |
| Tab text 10pt | `SetTabTextStyle(NormalText)` | `.panel-tab` (13px) |
| Toolbar background padding `FMargin(0, 4, 0, 4)`, button padding `FMargin(2, 0)`, icon `Icon16x16` | `FToolBarStyle` | `.editor-toolbar-strip` (`4px 0`), `.transport` (`4px` gaps), 16px SVGs |
| Toolbar button padding `FMargin(2, 2)` | `ToolBar.Button` | `.transport-button button` |
| 16px icons | `Icon16x16` (`CoreStyle.h`) | toolbar and search icons |
| 1px focus rectangle, 4px radius | `InputFocusRadius` / `InputFocusThickness` | `--theme-primary` outlines, `--theme-radius` |
| 8pt small text | `SmallTextSize` / `Small` text style | `.statusbar`, `.cb-count`, `.menu-shortcut` (`--theme-font-small`, 11px) |

What is deliberately ours, since the engine does not state it as data: the 26px tab strip height, the 22px
toolbar button, the details category header's lower rule and 4px padding, content-browser tile cells (16px
icon grid, 84px cell) and the panel-body padding.

## Typography

The editor's text styles are `FStarshipCoreStyle::FStyleFonts`: `Normal` 10pt, `Small` 8pt, `Large` 14pt and
condensed headings at 15, 21 and 33pt, all Roboto; `MonospacedText` is `DEFAULT_FONT("Mono", 10)`, Roboto Mono.
Slate scales points by DPI, so at 96 DPI 10pt is 13.33px and the UI uses **13px** (rounded, so glyphs stay on
the pixel grid). `src/Overcore/UI/Ue5Typography.cs` is the shared source of those numbers and of the font file
names; `tests/OvercoreEditorSession.Tests/Ue5TypographyTests.cs` keeps the two front ends and the stylesheet in step.

Fonts are bundled in the repository, since neither front end may depend on a system font:

- `src/OvercoreEditor/EditorResources/Fonts/Roboto-Regular.TTF` — the latin + latin-ext + cyrillic + greek
  merge of the official Roboto subsets (587 glyphs, 89 KB). The ImGui editor loads it at 13px
  (`ImGuiFontLoader`), falling back to `RobotoMono-Regular.TTF` and then ImGui's bitmap font.
- `src/OvercoreEditorPhotino/wwwroot/fonts/roboto-{latin,latin-ext,cyrillic,greek}-400-normal.woff2`,
  `roboto-latin-700-normal.woff2` and `roboto-mono-latin-400-normal.woff2` — the same families for the
  stylesheet, declared as `@font-face` rules with the unicode-ranges the upstream packages ship, so a subset
  downloads only when the UI needs it. `WebEditorServer` serves them as `font/woff2`.
- Both directories carry the SIL Open Font License text (`Roboto-OFL.txt`, `Roboto-LICENSE.txt`) that the
  fonts require.

`RobotoMono-Regular.TTF` has no `▸`, `▾` or `→` glyphs, so the hierarchy tree draws its disclosure arrows as
inline SVG chevrons and the inspector foldouts use bundled Font Awesome chevrons instead of text.

## Where else the look is reproduced

- **Toolbar buttons are `ToolBar.Button`, not the filled `Button`.** Unpainted normally; hovered and pressed
  with the style's `SelectionColor` (`#18A0FB`) and `SelectionColor_Pressed` (`#DA820A`), 16px icons,
  `FMargin(2, 2)` padding. The transport buttons follow that.
- **Flat buttons are `SimpleButton`.** Unpainted, `Dropdown` on hover *and* while pressed, `Foreground` when
  disabled. The viewport's Options button follows that. (The engine's `SimpleButton` has no separate pressed
  fill, unlike `Button`, whose pressed fill is `Header`.)
- **Search boxes** carry the magnifier `SSearchBox` draws inside the text box (`.search-field`, 16px glyph
  inside the 8px text padding).
- **Focus** is a 1px `Primary` rectangle drawn *inside* the widget (`.imgui-input:focus-visible` and friends),
  as the `FocusRectangle` brush does, rather than the browser's default ring. Inputs additionally follow
  `NormalEditableTextBox`: `Input` fill with an `InputOutline` border, `Hover` while hovered, `Primary` while
  focused with white text, `Header` fill when read-only.
- **Tree rows** are `FTableRowStyle`: `Recessed` surface, `SelectHover` hover, `Select` when selected with
  `ForegroundHover` text — and `SelectInactive` is what the engine uses when the table does not have focus,
  which we do not reproduce because the webview does not tell us which dock holds focus.
- **Content browser** folders take the `AccentFolder` tint. Tile and glyph sizes are our approximation of the
  engine's tile view, not a source value.
- **Cursors** are the arrow, not a hand: Slate does not change the pointer over buttons.

