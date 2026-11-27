# 🔥 Overcore Engine

Overcore is a 3D game engine, visual scene editor, and standalone runtime written in C# on .NET 11. It pairs a GameObject-Component architecture with OpenGL 4 rendering, BepuPhysics v2 simulation, OpenAL 3D audio, YAML scene serialization, and Roslyn C# scripting.

## 🧱 Architecture

The solution (`Overcore.slnx`) contains four production projects under `src`:

| Project | Output | Purpose |
| :--- | :--- | :--- |
| [`Overcore`](src/Overcore/) | Library | Core engine: GameObject/Component model, rendering backends, physics, audio, asset loaders, `Assets.pak` virtual file system, and YAML serialization. |
| [`OvercoreEditor`](src/OvercoreEditor/) | Executable | Dockable ImGui/ImGuizmo editor with viewport, hierarchy, inspector, content browser, console, play/pause/stop state, undo/redo, and game builder. |
| [`OvercoreRuntime`](src/OvercoreRuntime/) | Executable | Standalone host that reads `project.yaml`, loads `GameScripts.dll`, and runs scenes from `Assets/` or `Assets.pak`. |
| [`OvercoreEditorPhotino`](src/OvercoreEditorPhotino/) | Executable | Experimental PhotinoX Blazor editor: a native webview window (WebView2 / WKWebView / WebKitGTK) hosting a Blazor UI, with the engine rendering off-screen (`Overcore.Rendering.Headless`), streaming the game view into a WebGL canvas, a project launcher that opens real `project.yaml` projects, and an editor window laid out like the ImGui editor (menu bar, transport toolbar, hierarchy / viewport / inspector docks) in the exact UE5 palette, Roboto typography and engine metrics. |
| [`OvercoreEditorSession`](src/OvercoreEditorSession/) | Library | The editor session the PhotinoX window drives: render host, frame pipe, wire protocol, input bridge, scene commands and script compilation. No webview, no window — the desktop shell and [`tools/OvercoreFrameViewer`](tools/OvercoreFrameViewer/) both own one. |

## 🔧 Capabilities

- **Rendering**: OpenGL 4 pipeline (`OpenTK`) with directional, point, and spot lights, cubemap skyboxes, MSAA, bloom, and exposure post-processing. Nothing casts a shadow yet: the cascade splits and light-space fit exist as tested arithmetic (`Rendering/Shadows/`) and the contract for the passes that will use them is [Shadows](docs/shadows.md). A partial `Veldrid` backend exists in `Overcore`, though both the editor and runtime currently target `OpenTK`.
- **Physics**: 60 Hz fixed-timestep rigid-body dynamics via `BepuPhysics` v2. `BoxCollider`, `SphereCollider` and `CapsuleCollider` are components of their own (a collider with no `RigidBody` is a fixed shape); `OnCollision*`/`OnTrigger*` callbacks, `Physics.Raycast`/`SphereCast`/`Overlap*` queries, layer collision matrix, friction and bounciness. See [Physics & Runtime Lifecycle](docs/physics.md).
- **Audio**: 3D positional audio via OpenAL (`AudioListener` and `AudioSource`) supporting 8/16-bit mono and stereo `.wav` files.
- **UI**: Screen-space `Canvas` and anchored `RectTransform` layout with horizontal/vertical layout groups, rectangular or rounded descendant clipping with feathered edges via `RectMask2D`, tinted texture/UV-region or solid-color `Image` graphics with fractional nine-slicing, atlas-backed `Text`, and mouse `Button` hover/press/click, `Toggle`, draggable `Slider`, and keyboard-editable `InputField` controls with Tab focus navigation in the standalone OpenTK runtime and native editor viewport. Arabic/Persian and extended-Arabic shaping with a bundled Noto Sans Arabic glyph fallback and basic right-to-left layout are supported; full Unicode shaping/bidi and verified Veldrid parity are not complete yet. Rounded-rectangle masks, feathered edges, texture-alpha masks, and visual atlas slicing/import are implemented. See [Screen-space UI](docs/ui.md).
- **Scripting**: Dynamic C# script compilation via Roslyn (`Microsoft.CodeAnalysis.CSharp`), plus inspector attributes (`[Range]`, `[ColorUsage]`, `[DragDropTarget]`, `[HideInInspector]`).
- **Assets & Serialization**: Human-readable `.yaml` scenes and `.prefab` hierarchies, `.gltf`/`.glb` models imported as hierarchies with 32-bit indices (`SharpGLTF`), `.png`/`.jpg`/`.bmp`/`.tga` textures (`StbImageSharp`), and optional `CHPK` asset bundling (`Assets.pak`).
- **Animation (import only)**: a `.gltf`/`.glb` imports as a `GameObject` hierarchy with its node graph intact, its skins as `Skeleton` bones with inverse bind matrices, its `JOINTS_0`/`WEIGHTS_0` on the vertices, and its channels as `AnimationClip` curves. Nothing deforms a vertex yet and there is no `Animator`, so a rigged model imports and holds its bind pose; [Animation & Skinning](docs/animation.md) is the contract for the increment that follows.

## 🚀 Quick Start

### 📋 Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download) (`net11.0`)
- GPU with OpenGL 4.x support
- OpenAL runtime installed on the system (required for audio playback; the engine runs with audio disabled if unavailable)

### 🔨 Build and Run

```bash
dotnet build Overcore.slnx
dotnet run --project src/OvercoreEditor
```

Build outputs and intermediate files are centralized in the repository's `artifacts/` directory, grouped by project and configuration.

Run all unit tests with `dotnet test Overcore.slnx`. Engine tests are in `tests/Overcore.Tests`, and editor tests are in `tests/OvercoreEditor.Tests`. Both use xUnit and AwesomeAssertions.

On launch, the editor reopens the last project if available, or opens the project launcher. The launcher can create a blank project, create a copy of the built-in 3D/UI samples, open a folder containing `project.yaml`, and search or remove recent projects. Project creation never overwrites an existing folder; sample copies are independent and editable. From an open editor, use **File > New Project in New Window...** to create another project, or **File > Open Project in New Window...** to open one; your current project and unsaved work remain in the original window.

Open the launcher explicitly or pass a directory path to open a specific project directly:

```bash
dotnet run --project src/OvercoreEditor -- --launcher
dotnet run --project src/OvercoreEditor -- /path/to/MyGame
```

## 💻 Scripting

Place C# scripts in your project's `Scripts/` folder (or create them from **Add Component > New Script** in the Inspector). Scripts inherit from `Script` and are compiled in memory with Roslyn when the project loads.

```csharp
using System.Numerics;
using Overcore;
using Overcore.Attributes;
using Overcore.Components;

public class Spinner : Script
{
    [Range(0f, 360f, 1f)]
    public float DegreesPerSecond { get; set; } = 90f;

    public override void Update(float deltaTime)
    {
        if (GameObject is null)
        {
            return;
        }

        float radians = DegreesPerSecond * (MathF.PI / 180f) * deltaTime;
        GameObject.Transform.Rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians);

        if (Input.WasKeyPressed(Key.Space))
        {
            Log("Space pressed");
        }
    }
}
```

Public read/write properties on C# components and scripts are automatically exposed in the Inspector and serialized to scene `.yaml` files when modified from their defaults.

### Lifecycle and coroutines

Scripts get the full Unity-shaped lifecycle — `Awake`, `OnEnable`, `Start`, `FixedUpdate`, `Update(deltaTime)`, `LateUpdate`, `OnDisable`, `OnDestroy` — plus the collision and trigger callbacks, `GameObject.Instantiate`/`Destroy`, `Time.deltaTime`/`timeScale`/`frameCount`, and coroutines with `WaitForSeconds`, `WaitForFixedUpdate`, `WaitUntil` and `Invoke`:

```csharp
public class Alarm : Script
{
    public override void Start()
    {
        StartCoroutine(Blink());
        Invoke(nameof(Shout), 2f);
    }

    private IEnumerator Blink()
    {
        yield return new WaitForSeconds(0.5f);
        yield return new WaitUntil(() => Input.WasKeyPressed(Key.Space));
    }

    public override void OnCollisionEnter(Collision collision) => Log($"hit {collision.GameObject?.Name}");

    public void Shout() => Log("two seconds have gone by");
}
```

### Lua scripts

Overcore also supports Lua through the MoonSharp interpreter. Put a `.lua` file in your project's `Scripts/` folder, add **Language Script** from the Inspector's **Add Component** menu, and set `Language` to `lua` and `Source Path` to a project-relative path such as `Scripts/move.lua`. Save the scene and enter Play mode:

```lua
function start()
    log("Hello from Lua")
end

function update(dt)
    translate(dt, 0, 0)
end
```

Each component has its own Lua state. Optional `start()` and `update(dt)` functions run on scene start and each update. The initial engine API is `log(message)`, `get_position()` (returns x, y, z), `set_position(x, y, z)`, and `translate(x, y, z)`. Lua source is loaded when play starts, so changes are picked up on the next play session. Lua files are included in loose and packed builds; a project does not need C# scripts to build or play.

The scene format currently supports one language-script component per object; Lua-defined inspector properties, state-preserving hot reload, and a full engine API are not yet supported. The interpreter omits filesystem and module-loading libraries, but has no execution time or memory limits and must not be treated as a security boundary for untrusted code.

For additional languages, implement `IScriptLanguage` and `IScriptInstance`, and register the adapter with `ScriptLanguages.Register` in both the editor and runtime before scripts start. Adapters and their dependencies must also be included in exported builds. The existing Roslyn C# workflow remains separate.

## 🎮 Editor Controls

The editor opens with the hierarchy on the left, inspector on the right, viewport in the center, and content browser and console docked below it. Your layout is saved between sessions; use **Window > Reset Layout** to restore the default.

| Input | Action |
| :--- | :--- |
| `Right Mouse + Drag` | Look around in the viewport |
| `W` / `A` / `S` / `D` | Move camera horizontally while hovering viewport (`Shift` for 3x speed) |
| `Q` / `E` | Move camera down / up while hovering viewport |
| `Mouse Wheel` | Zoom camera forward / backward |
| `W` / `E` / `R` | Translate / Rotate / Scale gizmo (Edit mode) |
| `Ctrl + S` | Save active scene |
| `Ctrl + Z` / `Ctrl + Y` | Undo / Redo |

## 📦 Building a Game

Use **File > Build Game...** in the editor to export a standalone build:

1. Publishes `OvercoreRuntime` in `Release` mode named after the project (`project.yaml`).
2. Compiles `Scripts/*.cs` into `GameScripts.dll`.
3. Copies loose project assets into `Assets/`, or packs them into `Assets.pak` when **File > Pack Assets on Build** is enabled.

Current limitations:

- Packed builds (`Assets.pak`) support binary `.glb` models; multi-file `.gltf` assets require loose-file builds.
- The in-editor build pipeline assumes Windows (`explorer.exe` post-build open and `.exe` naming).

## 📚 Documentation

- [Physics & Runtime Lifecycle](docs/physics.md): Colliders and rigid bodies, contact and trigger callbacks, physics queries and layers, the script lifecycle, `Time`, coroutines and `Invoke`, `Instantiate`/`Destroy`, and changing scenes with `SceneManager`.
- [Animation & Skinning](docs/animation.md): The animation layer, increment by increment: what the importer and skeleton data model do today, what sampling, the `Animator` and GPU skinning will do, and the gaps (no Avatar, no IK, no root motion) that stay out of scope.
- [Shadows](docs/shadows.md): The contract for cascaded directional shadow maps — where cascade splits, the light-space fit, texel snapping, the depth pass and the receiver's bias belong, the per-light and per-scene settings, and the gaps (no point or spot shadows, no culling, no baked occlusion) that stay out of scope.
- [Color Consistency & Rendering Pipeline](docs/color-consistency.md): Notes on linear vs. sRGB framebuffer behavior between the ImGui viewport and standalone runtime.
- [Troubleshooting Guide](docs/troubleshooting.md): Root-cause writeups for viewport culling and runtime input/update issues.
- [Webview Host (PhotinoX/Blazor)](docs/photinox-blazor-viewport.md): The off-screen render path (`src/Overcore/Rendering/Headless/`) and the frame, pixel, and input contracts a webview-hosted UI must implement to display the game view.
- [UE5 Theme](docs/ue5-theme.md): The Unreal Engine 5 editor palette, typography and metrics both front ends use, with the engine source each value comes from, the widget-style mapping and the bundled Roboto fonts.
- [Web Editor Host](docs/webeditor-host.md): The PhotinoX Blazor editor in `src/OvercoreEditorPhotino`, the session in `src/OvercoreEditorSession` it drives, the wire protocol, threading model and current limitations.
- [Screen-space UI](docs/ui.md): Canvas, anchored RectTransform, Image, Button, layout rules, and the current implementation boundary.
- [Project templates](docs/project-templates.md): The built-in blank, 3D starter, and UI showcase projects available from the launchers.
