# Screen-space UI

Overcore's UI stack provides **Canvas**, anchored **RectTransform** layout, horizontal/vertical layout groups, rectangular or rounded **RectMask2D** descendant clipping, solid-color or texture-backed **Image** graphics (including UV crops for texture atlases), **Text**, pointer hit-testing, **Button** click events, pointer-driven **Toggle** controls, draggable numeric **Slider** controls, and single-line **InputField** text entry. It is intentionally implemented from scratch; it does not bundle or depend on Unity code.

## Build a panel and button

Add a `Canvas` and `RectTransform` to a root GameObject. Each UI child needs a `RectTransform`. Add an `Image` to show a colored rectangle, and a `Button` to make that rectangle interactive:

```csharp
using System.Numerics;
using Overcore.Components;
using Overcore.Core;

GameObject canvasObject = new("Canvas");
canvasObject.AddComponent(new RectTransform());
canvasObject.AddComponent(new Canvas());

GameObject buttonObject = new("PlayButton");
buttonObject.Transform.Parent = canvasObject.Transform;
buttonObject.AddComponent(new RectTransform
{
    AnchorMin = new Vector2(0.5f, 0.5f),
    AnchorMax = new Vector2(0.5f, 0.5f),
    Pivot = new Vector2(0.5f, 0.5f),
    AnchoredPosition = Vector2.Zero,
    SizeDelta = new Vector2(240f, 64f)
});
Image image = buttonObject.AddComponent(new Image { Color = new Vector3(0.18f, 0.42f, 0.82f) });
// Optional texture-backed image; UV bounds can select a sprite region from an atlas.
image.TextureName = "Assets/UI/button.png";
buttonObject.AddComponent(new Text
{
    Content = "Play",
    FontSize = 28f,
    HorizontalAlignment = HorizontalTextAlignment.Center,
    VerticalAlignment = VerticalTextAlignment.Middle
});
Button button = buttonObject.AddComponent(new Button());
button.Clicked += () => Overcore.Core.Logging.Logger.Info("Play clicked");
```

The OpenTK UI renders over the resolved scene in the standalone runtime and in the editor's off-screen scene viewport. Text supports multiple lines and horizontal/vertical alignment; set `Text.FontName` to a project-relative TrueType font path such as `Assets/Fonts/Display.ttf`, or leave it blank to use the bundled Roboto Mono fallback. A bundled DejaVu Sans Arabic fallback supplies glyphs missing from the selected font. Arabic and Persian letters are contextually shaped, lam-alef ligatures are applied, and basic right-to-left ordering keeps common Latin runs and numbers readable. Set `Text.Direction` to `Auto` (first strong character), `LeftToRight`, or `RightToLeft`; `HorizontalTextAlignment.Start` follows the resolved direction. InputField arrow keys and caret placement follow visual cluster order while the stored value remains in logical Unicode order. This is a practical Arabic/Persian path, not a full Unicode Bidirectional Algorithm or OpenType shaping engine: advanced bidi controls, complex mark positioning, and scripts with other shaping rules are not fully supported. Fonts referenced by scene text are added to the host's shared glyph atlas, including the hidden OpenGL context used by the headless/web editor viewport, so Text and InputField contents are rendered in both OpenTK views. A missing custom font falls back to the bundled font. Veldrid now renders Images, nine-slicing, scissored rectangular, geometry-clipped rounded, and feathered masks, sliders, focus decoration, Text, and InputField values/carets through a Veldrid-owned glyph atlas; it shares pointer/keyboard control events and also draws UI when no camera is active. SDL text input is forwarded to the shared runtime input state. This second backend has source coverage but still needs build and runtime verification. `Button.Clicked` is raised when the left mouse button is pressed and released on the same interactable button; editor-preview input is enabled only while playing. `Toggle.IsOn` changes on a matching pointer press/release, and `ValueChanged` fires only when the value changes. A Slider captures the pointer on press, updates while dragged (including drags beyond its bounds), clamps to its configured range, and supports horizontal/vertical direction and whole-number steps. `Slider.ValueChanged` fires only when the value changes. A hit on a child graphic can bubble to a Button, Toggle, Slider, or InputField on an ancestor. Tab and Shift+Tab move keyboard focus through enabled interactable controls in canvas draw order, and a focus outline is drawn. Enter or Space activates Buttons and Toggles; arrow keys adjust a focused Slider, with Home/End selecting its range endpoints. Images and text with `RaycastTarget` enabled block controls behind them. Buttons expose configurable normal, hover, pressed, and disabled tint multipliers; a Toggle's `Image` is tinted from its on/off/disabled colors. Sliders draw their own track, fill, and handle within their RectTransform. An `InputField` focuses on pointer press, displays its `Value` (or `Placeholder`) using the co-located `Text` style, and accepts keyboard text with grapheme-aware caret movement/deletion, Backspace, Delete, Home, End, and Enter/Escape to finish editing. `ValueChanged` fires when the string changes and `EndEdit` fires when focus ends. A background `Image` is optional.

## Layout rules

- Rectangles use **top-left-origin pixel coordinates**; normalized anchor Y values also start at the top.
- `AnchorMin` / `AnchorMax` select points in the parent. `SizeDelta` adds pixels to the size implied by the anchor span. `AnchoredPosition` offsets the anchor reference, and `Pivot` selects the point in the child rectangle attached there.
- A Canvas root fills the viewport. Child GameObjects follow Transform parent-child order. A nested Canvas starts a new full-screen root; higher `SortingOrder` values draw above lower ones.
- `CanvasScaleMode.ConstantPixelSize` uses authored pixel sizes. `ScaleWithScreenSize` scales from `ReferenceWidth` / `ReferenceHeight`; `MatchWidthOrHeight` blends between width-based and height-based scale.
- The layout resolver clamps anchors and pivots to `[0, 1]` and prevents negative resolved dimensions.
- Add a `HorizontalLayoutGroup` or `VerticalLayoutGroup` to a parent to arrange its direct UI children. Groups support spacing, four-sided padding, start/center/end alignment on both axes, child-size control, and optional equal expansion into unused width or height. Each child's resolved `RectTransform` size supplies its preferred size; nested groups work by resolving each child container before laying out its descendants. A layout group does not arrange children without an enabled `RectTransform`.
- Add `RectMask2D` to a UI parent to clip its descendants to that parent's rectangle. Set `CornerRadius` above zero for a rounded-rectangle clip; the shape is tessellated into convex polygons and clips textured graphics and text, while pointer hit-testing uses the same rounded shape. Nested masks intersect, and a mask object does not clip its own graphic. Set `Feather` above zero to fade the inner edge; hit-testing still uses the hard outer boundary. Set `MaskTextureName` to a project image asset to sample its alpha channel across the mask rectangle. `MaskThreshold` controls pointer hit-testing; rendered pixels multiply by the mask's linearly sampled level-zero alpha, and hit-testing uses matching bilinear sampling. Up to eight texture masks are sampled in one nested mask stack.
- `Image.TextureName` names a project-relative texture asset; leave it blank for a solid rectangle. `UVMin` / `UVMax` select a region of a texture atlas, while `Color` and `Alpha` tint the sampled pixels. `SliceLeft`, `SliceTop`, `SliceRight`, and `SliceBottom` provide fractional source-region border metadata; nonzero borders render as a nine-slice whose corner/edge proportions follow the destination rectangle.
- `Image.SpriteName` can reference a `.sprite.json` sidecar (the suffix is added when omitted); the desktop Content Browser can drag these sidecars onto the Image inspector field. It supplies the texture path, texture dimensions, top-left pixel rect, normalized pivot, pixels-per-unit, and pixel borders. Example: `{ "texture": "Assets/UI/atlas.png", "textureSize": [256, 128], "rect": [32, 16, 64, 32], "pivot": [0.5, 0.5], "pixelsPerUnit": 100, "border": [8, 4, 16, 12] }`. A valid sidecar overrides the Image's direct texture/UV/slice fields; if it is missing or invalid, those direct fields remain the fallback. In the Image inspector, create an initial sidecar, drag across the atlas preview to set the pixel rect, then use **Create From Selection** with a new filename to import additional sprite sidecars beside it.
- `Text.Wrap` defaults to true and wraps at word boundaries to the element width, splitting overlong words by Unicode text element when necessary. InputField values remain single-line and are clipped to their text rectangle. Arabic-script text uses a bundled Noto Sans Arabic fallback when the selected project font lacks a glyph; the engine includes contextual forms for common Arabic, Persian, Urdu, and other extended Arabic letters supported by that font.

## Progress checklist

The runtime implementation has not yet been validated by a .NET build in this workspace; `dotnet` is unavailable, so checkmarks below describe implemented source and test coverage, not passing test results.

- [x] Canvas and RectTransform sizing, anchors, pivot, scaling, and sorting.
- [x] Images, atlas UV regions, fractional nine-slice borders, rectangular/rounded mask clipping, and matching hit-test clipping in OpenTK and Veldrid.
- [x] Text alignment/line spacing, Buttons, Toggles, Sliders, layout groups, InputField editing, and keyboard focus navigation.
- [x] Scene/prefab serialization and generic property bridge for UI components; regression tests cover UI properties including custom font selection.
- [x] Project-relative TrueType font selection, a bundled Roboto Mono fallback, and shared native/headless glyph-atlas rendering.
- [x] Project-authored `.sprite.json` metadata with validated atlas rects, pivots, pixels-per-unit, borders, cached loading, scene serialization, Image rendering integration, and Content Browser drag/drop.
- [x] Editor creation of full-texture Sprite sidecars; metadata inspection for rect, pivot, pixels-per-unit, and borders; source texture changes revalidate the asset and refresh dimensions/rect bounds.
- [x] Visual atlas slicing/import workflow: drag a pixel rect on the texture preview and create separately named sidecars for multiple sprites.
- [x] Word wrapping, explicit newline preservation, and long-word breaking for text blocks; InputField stays single-line and clips to its element.
- [x] Grapheme-aware InputField caret movement/deletion, max-length truncation, word wrapping, and rune-based measurement in both renderers.
- [x] Arabic/Persian and extended-Arabic contextual forms (including Urdu letters), lam-alef ligatures, basic mixed-direction line ordering, bundled Noto Sans Arabic fallback glyphs, and visual RTL input caret navigation.
- [ ] Full Unicode Bidirectional Algorithm, full OpenType shaping/mark positioning, complete Unicode glyph coverage, and measured caret/layout behavior across all scripts; the current ImGui glyph API is BMP-limited and supplementary scalars use a replacement glyph.
- [x] Rounded-rectangle clipping for descendants and matching rounded hit-tests, including nested masks.
- [x] Configurable feathered inner edges for rounded and rectangular masks in both renderers.
- [x] Texture-alpha masks with configurable pointer hit-test threshold and OpenTK/Veldrid rendering.
- [ ] Veldrid UI rendering/input parity; rendering and input paths now cover the UI component set, but backend parity tests and runtime verification remain.
- [ ] Run all UI and editor-session tests plus a full build on a machine with the .NET SDK; verify native and headless renders manually.

## Current boundaries and next increments

This is the foundation, not yet a full Unity uGUI clone. Rectangular/rounded clipping with feathered edges and texture-alpha masks plus word wrapping are supported. Arabic/Persian and extended-Arabic shaping use bundled Noto Sans Arabic glyphs and a broader presentation-form table, but this is not full UAX #9/OpenType shaping or mark positioning; complete Unicode glyph coverage is unfinished (the current ImGui.NET glyph lookup is limited to 16-bit codepoints, with supplementary characters rendered as a question-mark fallback), and Veldrid parity has not yet been validated across runtime paths. The Image inspector now previews texture atlases, supports drag-selection of pixel rects, can create multiple separately named sidecars from one source texture, and resets the sprite rect to the full bounds when changing to another valid project texture. The font, headless viewport, and Veldrid paths still need .NET build/test and manual render verification. `Clicked`, `ValueChanged`, and `EndEdit` delegates are runtime wiring and are not saved in YAML; attach listeners from a script's `Start` method. UI components and their serializable layout/style properties are supported by scene and prefab serialization.

Next UI increments should improve Unicode-aware glyph and caret handling, add advanced mask modes, and automate cross-backend verification. Those layers should build on the stable pixel-space `RectTransform.Resolve` contract rather than folding editor-specific behavior into the runtime components.
