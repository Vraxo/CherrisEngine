# Themes

The editor's looks are data. A theme is one color per role, the type it is set in, and the few dimensions both
front ends share; the ImGui editor maps it onto `ImGuiCol` and the Blazor editor applies it as CSS custom
properties. Nothing in either front end names a color, a font family or a radius directly, which is what makes
a theme swappable at runtime.

## What ships

| Id | Name | Where the values come from |
| :--- | :--- | :--- |
| `ue5-starship` | Unreal Engine 5 | The engine's own defaults — `StyleColors.cpp` (`USlateThemeManager::InitalizeDefaults`), `StarshipCoreStyle.cpp` and `CoreStyle.h`. See [ue5-theme.md](ue5-theme.md). This is the default. |
| `unity-dark` | Unity (dark) | The dark column of Unity's published `--unity-colors-*` design system ([foundations.unity.com](https://www.foundations.unity.com/fundamentals/color-palette)) and its Inter type scale. |

Both are defined in code (`src/Cherris/UI/Themes/Ue5StarshipTheme.cs`, `UnityDarkTheme.cs`) and also shipped as
JSON (`src/CherrisEditor/EditorResources/Themes/*.json`), so a theme can be compared, copied and retuned
without touching the editor. A test keeps the two in step
(`tests/CherrisEditorWeb.Tests/ThemeCatalogTests.cs`, `ShippedFiles_MatchTheBuiltInThemes`).

### How exact the Unity theme is

Unity publishes `--unity-colors-*` per component, with a light and a dark column; the palette page is badged
LTS 2022.3, which is the latest published dark skin we could cite (Unity 6's internal design system is not
public). Our roles do not map one-to-one onto Unity's component variables, so each value in
`UnityDarkTheme.cs` names the variable it comes from, and anything Unity does not publish is marked "Ours":

| Ours | Why |
| :--- | :--- |
| `black` `#000000` | Unity has no "clear color" role. |
| `foreground-inverted` `#090909` | Text on a light fill; taken from Unity's light column. |
| `success` `#7ac24a`, `selection` `#4c7eff`, `selection-pressed` `#3a79bb` | Unity publishes no success or drag/selector affordance color; these follow its hues. |
| `accent-*` tints | The Project window's asset tints are not published as values. |
| corner radius 3px, scrollbar width 12px | Not published; 3px is what Unity's USS controls use. |

Everything else is a copied `--unity-colors-*` value, and `Metrics.ControlHeight` is
`--unity-metrics-single_line-height`.

## Selecting a theme

Both editors read the same catalog and the same preference file, so a choice made in one window is what the
other opens with:

- ImGui editor: **Theme** in the main menu bar.
- Blazor editor: **Theme** in the menu bar.

The item with a bullet is the applied theme; entries marked *file* were loaded from a JSON file rather than
from code; the tooltip is the theme's provenance. The selection is stored in
`%APPDATA%/CherrisEditor/theme.json` (`~/.config/CherrisEditor/theme.json` on Linux and macOS) as
`{ "theme": "unity-dark" }`. A saved id that no longer exists falls back to the default theme.

## Adding a theme

Drop a JSON file in either folder:

| Folder | Purpose |
| :--- | :--- |
| `<editor output>/EditorResources/Themes` | The themes that ship. |
| `%APPDATA%/CherrisEditor/Themes` (`~/.config/CherrisEditor/Themes`) | Yours. Scanned after the shipped folder, so it wins. |

Then pick **Theme > Reload Themes**; no restart and no rebuild. [theme-template.json](theme-template.json) is a
complete, valid theme (it is the UE5 one, with a placeholder id) to copy and edit.

```json
{
  "id": "my-theme",
  "displayName": "My theme",
  "provenance": "Where the values come from.",
  "typography": {
    "family": "Inter", "monospaceFamily": "Roboto Mono",
    "bodyPixels": 12, "smallPixels": 10, "largePixels": 14,
    "fontResource": "Fonts/Inter-Regular.TTF",
    "fallbackFontResource": "Fonts/RobotoMono-Regular.TTF",
    "cssStack": "\"Inter\", Verdana, system-ui, sans-serif", "boldWeight": 600
  },
  "metrics": { "cornerRadius": 3, "borderWidth": 1, "controlHeight": 18, "scrollbarWidth": 12 },
  "colors": { "black": "#000000", "title": "#3e3e3e", "…": "one entry per role" }
}
```

Reading is strict on purpose: a document missing a role, naming a role that does not exist, or spelling a color
badly is rejected with the reason shown under the theme menu, rather than silently leaving a widget unthemed.
One broken file never stops the editor from starting — it just does not appear in the list.

Notes worth knowing:

- **Every color role is required.** All 48 are listed in the template, and in `ThemeRoles.All`.
- A file whose `id` matches a built-in theme *replaces* it; anything else is added to the menu.
- Colors are `#rrggbb` or `#rrggbbaa`.
- Colors and metrics apply immediately. A theme's **font** needs the atlas that is built at startup, so a
  newly added theme renders in the current font until the editor restarts; the built-in themes each have
  their own entry already, so switching between them also switches font.

## How a theme is applied

| Front end | Applied by | Effect |
| :--- | :--- | :--- |
| `src/CherrisEditor` (ImGui) | `ImGuiTheme.Apply(EditorTheme)` | Every `ImGuiCol`, frame padding and rounding, and `io.FontDefault` for the theme's face. |
| `src/CherrisEditorPhotino` (Blazor) | `EditorTheme.ToCssVariables()` → `cherrisTheme.apply()` | CSS custom properties on `document.documentElement`. |

The stylesheet's `:root` block holds the default theme's values as a fallback, so the window is styled before
Blazor starts; `wwwroot/js/theme.js` then overrides them, and `index.html` replays the last applied set from
`localStorage` on load so a non-default theme does not flash the default one.

The variables are the 48 color roles as `--theme-<role>` (e.g. `--theme-panel`, `--theme-select`,
`--theme-accent-blue`) plus:

| Variable | Meaning |
| :--- | :--- |
| `--theme-font-ui`, `--theme-font-mono` | `font-family` stacks for UI and code text. |
| `--theme-font-size`, `--theme-font-small`, `--theme-font-large` | Body, small and large text sizes. |
| `--theme-font-weight-bold` | Weight used for emphasis (Roboto ships 700, Inter 600). |
| `--theme-radius`, `--theme-border-width`, `--theme-control-height`, `--theme-scrollbar-width` | Shared dimensions. |

## Adding a role

Roles are the contract between the two front ends, so adding one touches, in order: `ThemeRoles` (constant and
`All`), both themes in `src/Cherris/UI/Themes`, the mapping in `ImGuiTheme` if ImGui has an equivalent, the
stylesheet default in `app.css`, and both shipped JSON files. `ThemeCatalogTests` fails until every theme fills
the new role, which is the point — a role with no value in a theme is a bug, not a fallback.
