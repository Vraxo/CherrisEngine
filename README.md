# 🍒 Cherris Engine

Cherris is a 3D game engine, visual scene editor, and standalone runtime written in C# on .NET 10. It pairs a GameObject-Component architecture with OpenGL 4 rendering, BepuPhysics v2 simulation, OpenAL 3D audio, YAML scene serialization, and Roslyn C# scripting.

## 🧱 Architecture

The solution (`Cherris.slnx`) is split into three projects:

| Project | Output | Purpose |
| :--- | :--- | :--- |
| [`Cherris`](Cherris/) | Library | Core engine: GameObject/Component model, rendering backends, physics, audio, asset loaders, `Assets.pak` virtual file system, and YAML serialization. |
| [`CherrisEditor`](CherrisEditor/) | Executable | Dockable ImGui/ImGuizmo editor with viewport, hierarchy, inspector, content browser, console, play/pause/stop state, undo/redo, and game builder. |
| [`CherrisRuntime`](CherrisRuntime/) | Executable | Standalone host that reads `project.yaml`, loads `GameScripts.dll`, and runs scenes from `Assets/` or `Assets.pak`. |

## 🔧 Capabilities

- **Rendering**: OpenGL 4 pipeline (`OpenTK`) with directional, point, and spot lights, cubemap skyboxes, MSAA, bloom, and exposure post-processing. A partial `Veldrid` backend exists in `Cherris`, though both the editor and runtime currently target `OpenTK`.
- **Physics**: 60 Hz fixed-timestep rigid-body dynamics via `BepuPhysics` v2 with static and dynamic bodies (`Box`, `Sphere`, and `Capsule` colliders).
- **Audio**: 3D positional audio via OpenAL (`AudioListener` and `AudioSource`) supporting 8/16-bit mono and stereo `.wav` files.
- **Scripting**: Dynamic C# script compilation via Roslyn (`Microsoft.CodeAnalysis.CSharp`), plus inspector attributes (`[Range]`, `[ColorUsage]`, `[DragDropTarget]`, `[HideInInspector]`).
- **Assets & Serialization**: Human-readable `.yaml` scenes and `.prefab` hierarchies, `.gltf`/`.glb` models (`SharpGLTF`), `.png`/`.jpg`/`.bmp`/`.tga` textures (`StbImageSharp`), and optional `CHPK` asset bundling (`Assets.pak`).

## 🚀 Quick Start

### 📋 Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`net10.0`)
- GPU with OpenGL 4.x support
- OpenAL runtime installed on the system (required for audio playback; the engine runs with audio disabled if unavailable)

### 🔨 Build and Run

```bash
dotnet build Cherris.slnx
dotnet run --project CherrisEditor
```

On launch, the editor reopens the last project if available, or opens the Project Selector to create or browse for one. Pass a directory path to open a specific project directly:

```bash
dotnet run --project CherrisEditor -- /path/to/MyGame
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

Public read/write properties on components and scripts are automatically exposed in the Inspector and serialized to scene `.yaml` files when modified from their defaults.

## 🎮 Editor Controls

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
