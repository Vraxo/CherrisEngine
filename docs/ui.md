# Screen-space UI

Overcore's UI stack provides **Canvas**, anchored **RectTransform** layout, horizontal/vertical layout groups, **RectMask2D** rectangular descendant clipping, solid-color or texture-backed **Image** graphics (including UV crops for texture atlases), **Text**, pointer hit-testing, **Button** click events, pointer-driven **Toggle** controls, draggable numeric **Slider** controls, and single-line **InputField** text entry. It is intentionally implemented from scratch; it does not bundle or depend on Unity code.

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

The UI renders over the resolved scene in the standalone OpenTK runtime and in the editor's off-screen scene viewport. Text supports multiple lines and horizontal/vertical alignment; set `Text.FontName` to a project-relative TrueType font path such as `Assets/Fonts/Display.ttf`, or leave it blank to use the bundled Roboto Mono fallback. Fonts referenced by scene text are added to the host's shared glyph atlas, including the hidden OpenGL context used by the headless/web editor viewport, so Text and InputField contents are rendered in both native and off-screen views. A missing custom font falls back to the bundled font. Viewport keyboard text is forwarded to the runtime input system. `Button.Clicked` is raised when the left mouse button is pressed and released on the same interactable button; editor-preview input is enabled only while playing. `Toggle.IsOn` changes on a matching pointer press/release, and `ValueChanged` fires only when the value changes. A Slider captures the pointer on press, updates while dragged (including drags beyond its bounds), clamps to its configured range, and supports horizontal/vertical direction and whole-number steps. `Slider.ValueChanged` fires only when the value changes. A hit on a child graphic can bubble to a Button, Toggle, Slider, or InputField on an ancestor. Tab and Shift+Tab move keyboard focus through enabled interactable controls in canvas draw order, and a focus outline is drawn. Enter or Space activates Buttons and Toggles; arrow keys adjust a focused Slider, with Home/End selecting its range endpoints. Images and text with `RaycastTarget` enabled block controls behind them. Buttons expose configurable normal, hover, pressed, and disabled tint multipliers; a Toggle's `Image` is tinted from its on/off/disabled colors. Sliders draw their own track, fill, and handle within their RectTransform. An `InputField` focuses on pointer press, displays its `Value` (or `Placeholder`) using the co-located `Text` style, and accepts keyboard text with caret movement, Backspace, Delete, Home, End, and Enter/Escape to finish editing. `ValueChanged` fires when the string changes and `EndEdit` fires when focus ends. A background `Image` is optional.

## Layout rules

- Rectangles use **top-left-origin pixel coordinates**; normalized anchor Y values also start at the top.
- `AnchorMin` / `AnchorMax` select points in the parent. `SizeDelta` adds pixels to the size implied by the anchor span. `AnchoredPosition` offsets the anchor reference, and `Pivot` selects the point in the child rectangle attached there.
- A Canvas root fills the viewport. Child GameObjects follow Transform parent-child order. A nested Canvas starts a new full-screen root; higher `SortingOrder` values draw above lower ones.
- `CanvasScaleMode.ConstantPixelSize` uses authored pixel sizes. `ScaleWithScreenSize` scales from `ReferenceWidth` / `ReferenceHeight`; `MatchWidthOrHeight` blends between width-based and height-based scale.
- The layout resolver clamps anchors and pivots to `[0, 1]` and prevents negative resolved dimensions.
- Add a `HorizontalLayoutGroup` or `VerticalLayoutGroup` to a parent to arrange its direct UI children. Groups support spacing, four-sided padding, start/center/end alignment on both axes, child-size control, and optional equal expansion into unused width or height. Each child's resolved `RectTransform` size supplies its preferred size; nested groups work by resolving each child container before laying out its descendants. A layout group does not arrange children without an enabled `RectTransform`.
- Add `RectMask2D` to a UI parent to clip its descendants to that parent's rectangle. Nested masks intersect; the mask object's own graphic is not clipped by its own mask. Hit-testing uses the same clip region as drawing.
- `Image.TextureName` names a project-relative texture asset; leave it blank for a solid rectangle. `UVMin` / `UVMax` select a region of a texture atlas, while `Color` and `Alpha` tint the sampled pixels. `SliceLeft`, `SliceTop`, `SliceRight`, and `SliceBottom` provide fractional source-region border metadata; nonzero borders render as a nine-slice whose corner/edge proportions follow the destination rectangle.

## Progress checklist

The runtime implementation has not yet been validated by a .NET build in this workspace; `dotnet` is unavailable, so checkmarks below describe implemented source and test coverage, not passing test results.

- [x] Canvas and RectTransform sizing, anchors, pivot, scaling, and sorting.
- [x] Images, atlas UV regions, fractional nine-slice borders, rectangular masking, and hit-test clipping.
- [x] Text alignment/line spacing, Buttons, Toggles, Sliders, layout groups, InputField editing, and keyboard focus navigation.
- [x] Scene/prefab serialization and generic property bridge for UI components; regression tests cover UI properties including custom font selection.
- [x] Project-relative TrueType font selection, a bundled Roboto Mono fallback, and shared native/headless glyph-atlas rendering.
- [ ] Fuller Sprite asset/import metadata and editor-facing sprite selection/inspection.
- [ ] Veldrid UI rendering/input parity.
- [ ] Run all UI and editor-session tests plus a full build on a machine with the .NET SDK; verify native and headless renders manually.

## Current boundaries and next increments

This is the foundation, not yet a full Unity uGUI clone. Rectangular clipping is supported, but there are no rounded/soft or texture-alpha masks, text does not wrap, Sprite texture import/selection metadata is still minimal, and Veldrid UI rendering is not implemented. The font and headless viewport paths now have source coverage but still need .NET build/test and manual render verification. `Clicked`, `ValueChanged`, and `EndEdit` delegates are runtime wiring and are not saved in YAML; attach listeners from a script's `Start` method. UI components and their serializable layout/style properties are supported by scene and prefab serialization.

Next UI increments should add Sprite import metadata and editor selection, implement the Veldrid UI/input path, and then close remaining behavior gaps such as text wrapping and non-rectangular masks. Those layers should build on the stable pixel-space `RectTransform.Resolve` contract rather than folding editor-specific behavior into the runtime components.
