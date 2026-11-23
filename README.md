# 🍒 Cherris Engine

Cherris is a 3D game engine, visual scene editor, and standalone runtime written in C# on .NET 11. It pairs a GameObject-Component architecture with OpenGL 4 rendering, BepuPhysics v2 simulation, OpenAL 3D audio, YAML scene serialization, and Roslyn C# scripting.

## 🧱 Architecture

The solution (`Cherris.slnx`) contains four production projects under `src`:

| Project | Output | Purpose |
| :--- | :--- | :--- |
| [`Cherris`](src/Cherris/) | Library | Core engine: GameObject/Component model, rendering backends, physics, audio, asset loaders, `Assets.pak` virtual file system, and YAML serialization. |
| [`CherrisEditor`](src/CherrisEditor/) | Executable | Dockable ImGui/ImGuizmo editor with viewport, hierarchy, inspector, content browser, console, play/pause/stop state, undo/redo, and game builder. |
| [`CherrisRuntime`](src/CherrisRuntime/) | Executable | Standalone host that reads `project.yaml`, loads `GameScripts.dll`, and runs scenes from `Assets/` or `Assets.pak`. |
| [`CherrisEditorWeb`](src/CherrisEditorWeb/) | Executable | Experimental browser-hosted editor: renders off-screen (`Cherris.Rendering.Headless`), serves an HTML/WebGL page over a local WebSocket, and streams the game view to it. Also usable inside a PhotinoX window. |

## 🔧 Capabilities

- **Rendering**: OpenGL 4 pipeline (`OpenTK`) with directional, point, and spot lights, cubemap skyboxes, MSAA, bloom, and exposure post-processing. A partial `Veldrid` backend exists in `Cherris`, though both the editor and runtime currently target `OpenTK`.
- **Physics**: 60 Hz fixed-timestep rigid-body dynamics via `BepuPhysics` v2 with static and dynamic bodies (`Box`, `Sphere`, and `Capsule` colliders).
- **Audio**: 3D positional audio via OpenAL (`AudioListener` and `AudioSource`) supporting 8/16-bit mono and stereo `.wav` files.
- **Scripting**: Dynamic C# script compilation via Roslyn (`Microsoft.CodeAnalysis.CSharp`), plus inspector attributes (`[Range]`, `[ColorUsage]`, `[DragDropTarget]`, `[HideInInspector]`).
- **Assets & Serialization**: Human-readable `.yaml` scenes and `.prefab` hierarchies, `.gltf`/`.glb` models (`SharpGLTF`), `.png`/`.jpg`/`.bmp`/`.tga` textures (`StbImageSharp`), and optional `CHPK` asset bundling (`Assets.pak`).

## 🚀 Quick Start

### 📋 Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download) (`net11.0`)
- GPU with OpenGL 4.x support
- OpenAL runtime installed on the system (required for audio playback; the engine runs with audio disabled if unavailable)

### 🔨 Build and Run

```bash
dotnet build Cherris.slnx
dotnet run --project src/CherrisEditor
```

Build outputs and intermediate files are centralized in the repository's `artifacts/` directory, grouped by project and configuration.

Run all unit tests with `dotnet test Cherris.slnx`. Engine tests are in `tests/Cherris.Tests`, and editor tests are in `tests/CherrisEditor.Tests`. Both use xUnit and AwesomeAssertions.

On launch, the editor reopens the last project if available, or opens the project launcher. The launcher can create projects, open a folder containing `project.yaml`, and search or remove recent projects. Creating a project never overwrites an existing folder. From an open editor, use **File > New Project in New Window...** to create another project, or **File > Open Project in New Window...** to open one; your current project and unsaved work remain in the original window.

Open the launcher explicitly or pass a directory path to open a specific project directly:

```bash
dotnet run --project src/CherrisEditor -- --launcher
dotnet run --project src/CherrisEditor -- /path/to/MyGame
```

## 💻 Scripting

Place C# scripts in your project's `Scripts/` folder (or create them from **Add Component > New Script** in the Inspector). Scripts inherit from `Script` and are compiled in memory with Roslyn when the project loads.

```csharp
using System.Numerics;
using Cherris;
using Cherris.Attributes;
using Cherris.Components;

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

### Lua scripts

Cherris also supports Lua through the MoonSharp interpreter. Put a `.lua` file in your project's `Scripts/` folder, add **Language Script** from the Inspector's **Add Component** menu, and set `Language` to `lua` and `Source Path` to a project-relative path such as `Scripts/move.lua`. Save the scene and enter Play mode:

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

1. Publishes `CherrisRuntime` in `Release` mode named after the project (`project.yaml`).
2. Compiles `Scripts/*.cs` into `GameScripts.dll`.
3. Copies loose project assets into `Assets/`, or packs them into `Assets.pak` when **File > Pack Assets on Build** is enabled.

Current limitations:

- Packed builds (`Assets.pak`) support binary `.glb` models; multi-file `.gltf` assets require loose-file builds.
- The in-editor build pipeline assumes Windows (`explorer.exe` post-build open and `.exe` naming).

## 📚 Documentation

- [Color Consistency & Rendering Pipeline](Docs/color-consistency.md): Notes on linear vs. sRGB framebuffer behavior between the ImGui viewport and standalone runtime.
- [Troubleshooting Guide](Docs/troubleshooting.md): Root-cause writeups for viewport culling and runtime input/update issues.
- [Webview Host (PhotinoX/Blazor)](Docs/photinox-blazor-viewport.md): The off-screen render path (`src/Cherris/Rendering/Headless/`) and the frame, pixel, and input contracts a webview-hosted UI must implement to display the game view.
- [Web Editor Host](Docs/webeditor-host.md): The browser-hosted editor in `src/CherrisEditorWeb`, its wire protocol, how to wrap it in a PhotinoX window, and its current limitations.
