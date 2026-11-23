# Web / PhotinoX editor host

The editor has a second front end next to the ImGui window: a **PhotinoX Blazor** desktop app that renders
the game view off-screen and shows it inside a native webview window. It is additive — `src/CherrisEditor`,
`src/CherrisRuntime` and the engine's ImGui path are untouched.

| Project | What it is |
| :--- | :--- |
| `src/CherrisEditorPhotino` | The editor window. `PhotinoX.Blazor` host, Blazor components, viewport canvas, scene panel. |
| `src/CherrisEditorWeb` | The transport it reuses: render host, frame pipe, wire protocol, input bridge. Also runs standalone as a browser dev harness. |
| `src/Cherris/Rendering/Headless` | Off-screen rendering: hidden GL context, frame pacing, GPU readback. |

See [photinox-blazor-viewport.md](photinox-blazor-viewport.md) for the rendering-side contract: why the
viewport is a pixel stream and not an embedded GL surface.

## Running it

```bash
dotnet run --project src/CherrisEditorPhotino
dotnet run --project src/CherrisEditorPhotino -- --project "D:\Games\MyGame"
```

Opens a 1600x900 native window: WebView2 on Windows, WKWebView on macOS, WebKitGTK 4.1 on Linux. It starts
on the **project launcher**; opening a project starts a render session that shows that project's start
scene.

| Option | Meaning |
| :--- | :--- |
| `--project <path>` | Open this project on startup instead of waiting in the launcher. A bare existing folder works too. |
| `--width <n>` / `--height <n>` | Initial render size in device pixels (the viewport overrides it once it measures the canvas). |
| `--fps <n>` | Render loop rate (default 60). |

## Launcher and projects

The launcher mirrors the ImGui editor's (`src/CherrisEditor/Projects/ProjectSelector.cs`), because a project
folder is the same thing to both front ends:

- **Projects** — recent projects with a search box, a ready/missing badge per entry, Open and Remove.
  **Open folder…** uses the window's native folder dialog (`ShowOpenFolderAsync`); the path field works
  without it.
- **New project** — name plus location, with a live preview of the folder that will be created. A new
  project gets `project.yaml` and an empty start scene, exactly like the ImGui editor's.
- **Skip: demo scene** — opens the built-in scene (camera, light, floor, spinning cube) without a project.

History lives in `%APPDATA%\Cherris\recent_projects` and `last_project` (`~/.config/Cherris/` on Linux).
The ImGui editor writes the same two files, so a project opened in one front end appears in the other's
launcher. The model behind it — `Cherris.Projects.ProjectConfig`, `ProjectWorkspace`, `RecentProjects` —
lives in the engine library and is unit-tested (`tests/Cherris.Tests/Projects/`).

### What opening a project does

1. `ProjectWorkspace.Validate` resolves the folder and rejects anything without `project.yaml`, so a typo
   cannot reach the renderer.
2. The session starts (see threading below) with an `EditorSceneRequest` naming the project and its
   `StartScenePath`.
3. On the render thread, `WebEditorEngine.LoadContent` points `ProjectFiles` at the project root (this is
   what makes scene-relative asset paths and a packaged `Assets.pak` resolve), loads the start scene with
   the engine's `SceneLoader`, and opens it.
4. A scene with no camera gets a default one — a brand-new project's start scene is empty, and the renderer
   needs a camera to draw anything. The reason lands in the log.
5. If the scene is missing, unreadable or empty, the demo scene is shown and the log says why, so the
   viewport is never a blank rectangle without an explanation.

### Switching projects

`Projects…` in the editor toolbar stops the session and returns to the launcher; opening another project
starts a fresh session. The engine is not re-entered: the previous project's GL resources, asset root and
scene are dropped with the session, and the frame socket keeps the same port, so the viewport canvas
reconnects on its own.

The browser dev harness is still available when iterating on the protocol or on a machine without a webview
runtime:

```bash
dotnet run --project src/CherrisEditorWeb            # http://localhost:5178/
dotnet run --project src/CherrisEditorWeb --no-browser --port 5179
```

It serves `src/CherrisEditorWeb/wwwroot` and has the same options plus `--listen-all` and `--help`.

## The window

`src/CherrisEditorPhotino/Program.cs` is the whole host:

```csharp
[STAThread]
private static int Main(string[] args)
{
    EditorHostService editorHost = EditorHostService.Start(args);

    var appBuilder = PhotinoBlazorApp.CreateBuilder(args);
    appBuilder.RootComponents.Add<App>("app");          // <app> in wwwroot/index.html
    appBuilder.Services.AddSingleton(editorHost);

    var app = appBuilder.Build();
    app.MainWindow.SetTitle("Cherris Editor").SetSize(1600, 900);
    app.Run();                                          // native message loop, returns on close

    return editorHost.FailureMessage is null ? 0 : 1;
}
```

`PhotinoX.Blazor` 5.5.0 pulls in `PhotinoX.App` → `PhotinoX` → `PhotinoX.Native` and
`Microsoft.AspNetCore.Components.WebView`. It targets `net11.0` among others, so the project needs no
pinning tricks. The package's root namespace is still `Photino.Blazor`.

`wwwroot/index.html` is a standard Blazor WebView host page — `<app>` for the root component and
`_framework/blazor.webview.js` to boot the renderer — plus `js/viewport.js`, the frame bridge.

## Architecture

| Piece | Thread | Responsibility |
| :--- | :--- | :--- |
| `PhotinoBlazorApp` message loop | main (`[STAThread]`) | Window, webview, Blazor renderer. Owns the UI thread. |
| `Launcher` / `Editor` pages | main | Project picker and editor shell; they only call `EditorHostService` and read what it publishes. |
| `HeadlessBackend` hidden window | main, before the loop starts | Creates the GLFW window/context. OpenTK only allows GLFW init on the process' main thread. |
| `WebEditorEngine` | background ("Cherris Engine") | Makes the context current, runs the engine, captures the previous frame from `OnDrawUI` and publishes it. |
| `EditorHostService` | main, then any | One session at a time: validates the project, prepares the context on the main thread, starts the render thread, exposes URL, log, diagnostics, recent projects and failures. |
| `WebEditorHost` | any | Owns the listener, the desired viewport size, the active client, and input translation into `HeadlessInput`. |
| `WebEditorServer` | thread pool | Serves `wwwroot` and upgrades `/ws` to a WebSocket. |
| `ViewerConnection` | thread pool | Per-client greeting, frame sending, command receiving. |
| `FramePipe` | shared | Single-slot frame hand-off: newest frame wins, stale frames are dropped and pooled. |
| `SceneSnapshotBuilder` | render thread | Copies the hierarchy into an immutable `SceneSnapshot` every 15 frames. |
| `SceneStore` / `StatusBar` | UI thread | Poll (`200 ms` / `500 ms`) what the other threads publish; never touch engine state. |

```
main thread (native loop + Blazor)        engine thread                 socket thread
----------------------------------        -------------                 -------------
window / webview / components
  ViewportPanel --mount(ws)----> socket <---- PublishFrame <--- FramePipe
  SceneStore ----poll---------- LatestScene <-- SceneSnapshotBuilder
  StatusBar -----getStats()---> JS bridge ---> send key/mouse/resize ---> HeadlessInput
```

The window's main thread belongs to the native message loop, so the engine gets its own thread — the
opposite arrangement from the browser harness, where the engine owns the main thread. GL stays
thread-affine: the context, the renderer, `SceneFrameCapture` and every GL call live on the engine thread,
and nothing there touches Blazor state or `StateHasChanged`. Cross-thread data moves only through
`Volatile` fields holding immutable objects (`SceneSnapshot`, `FramePipe`'s slot).

The one thing that cannot move off the main thread is **context creation**. `Engine`'s constructor calls
`IRenderingInterface.Initialize`, and OpenTK's `GLFWProvider.EnsureInitialized` throws
`GLFWException("GLFW can only be called from the main thread!")` for anything that is not the thread whose
stack holds the entry point — on every platform, not just macOS. So `EditorHostService.Start` runs on the
main thread and calls `HeadlessBackend.CreateGraphicsContext(...)` there; the render thread only calls
`MakeCurrent()`. A GL context is portable between threads and nothing is ever presented from the hidden
window, so the split is safe. Two details make it work:

- `CreateGraphicsContext` **releases** the context before returning. `NativeWindow`'s constructor makes the
  context current on the creating thread, and a context can only be current on one thread at a time, so
  leaving it bound makes the render thread's `MakeCurrent` fail with
  `WGL: Failed to make context current: The requested resource is in use` (`ERROR_BUSY`). The render thread
  rebinds it, and unbinds it again when the render loop ends so the host can destroy the hidden window.
- Once the context exists, `GLFWProvider.CheckForMainThread` is cleared — its guard only tracks the creating
  thread and would otherwise reject the render thread's `MakeCurrent`.

### Why pixels still travel over a socket

The Blazor UI is in-process, so the frame path could be JS interop. It is a loopback WebSocket instead:

- A 1080p frame is 8.3 MB; serialising that through `IJSRuntime` per frame puts it on the Blazor renderer's
  dispatcher path, where it competes with UI work and is copied as base64 or a stream per call.
- The socket transfer is binary, zero-copy on the C# side (`ArraySegment<byte>` straight from the pool) and
  naturally drops frames when the client is slow — `FramePipe` keeps one pending frame.
- The listener binds loopback only, inside the same process, and picks a free port at startup, so two
  editor instances can never collide and nothing is exposed beyond the machine.

`wwwroot/js/viewport.js` is the other half: it mounts a WebGL2 canvas on the socket, uploads each frame with
`texSubImage2D`, and forwards keyboard/mouse/wheel/pointer-lock events back. It is the same code that the
browser harness uses, wrapped in `mount()` / `dispose()` / `getStats()` so Blazor can own its lifetime.

### Diagnostics

Everything the host learns goes to three places: the console, an in-memory ring buffer (the "Host log" in
the window) and `%LOCALAPPDATA%/Cherris/editor-host.log` (`~/.local/share/Cherris/editor-host.log` on
Linux; the exact path is shown in the window and in the diagnostics block).

- When the renderer or the render loop fails, the window shows the exception verbatim — type, message and
  stack — plus the log tail and a **Copy diagnostics** button (environment, base directory, whether
  `EditorResources/` and `wwwroot/` exist, the host URL, the renderer string, the failure).
- Engine `Logger` output is mirrored into the same log, so shader and asset errors are visible in the
  window instead of only on a console that a windowed app may not have.
- The status bar shows the GL renderer string once the context is live, e.g.
  `NVIDIA GeForce RTX 3060 (NVIDIA Corporation) · GL 4.6.0`. If that line stays at "renderer starting", the
  failure panel explains why.

### Scene snapshots

The render thread publishes a `SceneSnapshot` (objects, transform, component type names, parent id) every
15 frames. `SceneStore` — one singleton, not one timer per panel — polls
`EditorHostService.LatestScene` and diffs by reference, so the UI shows the hierarchy at 5 Hz without ever
locking the engine. Snapshots are immutable copies: an older one cannot change while a component is
rendering it. `SceneTree` rebuilds the `Transform.Parent` hierarchy from the flat node list for the
hierarchy panel, promoting objects with a missing parent or a parent cycle to roots so a damaged scene still
renders.

### Editor layout

`Pages/Editor.razor` replicates the ImGui editor's dock layout rather than inventing its own one; the
chrome is deliberately the same so muscle memory carries over:

| ImGui (`src/CherrisEditor`) | PhotinoX editor |
| :--- | :--- |
| `MenuBar` — File / Edit / Window | `Components/MenuBar.razor` — File (New Project…, Open Project…, Exit), Edit, Window (Reset Layout) |
| `ToolBar` — three centred transport buttons | `Components/ToolBar.razor` — Play / Pause / Stop, disabled: play mode is engine-side and stays in the ImGui editor |
| Dockspace: left `Hierarchy` 20%, middle `Viewport` 72% + bottom tabs 28%, right `Inspector` 24% | `.dockspace` CSS grid with the same proportions; `Components/HierarchyPanel.razor`, `ViewportPanel.razor`, `ContentBrowserPanel.razor` / `ConsolePanel.razor` as the two bottom tabs, `InspectorPanel.razor` |
| `ViewportToolbar` — `Options` popup | same `Options` popup in `ViewportPanel` (Show Grid is a renderer setting, so it is shown disabled) |
| `ConsolePanel` — Clear, Auto-scroll, coloured levels | `ConsolePanel.razor` over `EditorHostService.LogEntries` |
| `ContentBrowserPanel` — `<- Back`, `Path: Project/…`, icon grid | `ContentBrowserPanel.razor`, read-only over the open project folder |
| Theme: `ImGuiTheme.Apply(EditorTheme)` | the same `EditorTheme`, applied as `--theme-*` tokens (see [themes.md](themes.md)) |

Colors come from the engine's own palette rather than an approximation of it — see
[UE5 Theme](ue5-theme.md) for every value, its source file, the widget styles it maps to, and the bundled Roboto/Roboto Mono fonts behind the typography.

Layout is additive: nothing in `src/CherrisEditor` changed. Splitters are dragged by `wwwroot/js/dock.js`,
which only writes three CSS custom properties (`--dock-left`, `--dock-right`, `--dock-bottom`), persists
them in `localStorage` the way ImGui persists its ini layout, and is reset by **Window > Reset Layout**.
Panels read mode-independent state (`SceneStore`, `SceneSelection`), so the hierarchy selection drives the
inspector without either component talking to the other. Editing stays in the ImGui editor: names, values
and the Add-component menu are drawn read-only/enabled-off rather than faked.

The inspector's Transform is the ImGui one's three Vector3 rows — Position, Rotation and Scale — with each
field labelled by its axis in the same red/green/blue (`TransformRows`, `.vec-axis`). Rotation is shown as
Euler degrees rather than as a quaternion, which is what the ImGui inspector edits
(`Quaternion.CreateFromYawPitchRoll` on the way back).

## Protocol

`ViewportProtocol` (C#) and `js/viewport.js` (JS) are the two halves of this contract; they are versioned
together and the client refuses a mismatched `protocol`.

Server to client, text, once on connect:

```json
{"type":"hello","protocol":1,"width":1280,"height":720,"fps":60}
```

Server to client, binary, per frame (13-byte header + pixels):

| Offset | Size | Meaning |
| :--- | :--- | :--- |
| 0 | 1 | format, `1` = RGBA8 |
| 1 | 4 | width, int32 little-endian |
| 5 | 4 | height, int32 little-endian |
| 9 | 4 | sequence, int32 little-endian (monotonic; lets the client ignore stale frames) |
| 13 | w*h*4 | pixels, RGBA8, **row 0 is the top of the image** |

Client to server, text, one command per message:

| Message | Effect |
| :--- | :--- |
| `{"type":"resize","width":w,"height":h}` | Render at this size (device pixels) |
| `{"type":"key","code":"KeyW","down":true}` | `KeyboardEvent.code` → `BrowserKeyMap` → `Input` |
| `{"type":"mouseButton","button":2,"down":true}` | `MouseEvent.button` → `MouseButton` |
| `{"type":"mouseMove","dx":4,"dy":-2}` | Pointer-locked relative movement |
| `{"type":"mouse","x":10,"y":200}` | Absolute position, viewport device pixels |
| `{"type":"wheel","x":0,"y":-1}` | Wheel notches |
| `{"type":"lock","locked":true}` | Pointer lock acquired/released |
| `{"type":"blur"}` | Page hidden or unfocused: clears held keys |

Frame timing: `HeadlessGameWindow.SwapBuffers` paces the loop to `--fps`; the transport never blocks the
render thread, so a slow client lowers the visible frame rate instead of adding latency.

## Demo scene

The viewport shows a built-in scene (floor, spinning cube, directional light) because project loading still
lives in the ImGui editor. Replace `WebEditorEngine.LoadContent` with `SceneLoader.LoadScene(...)` when the
Blazor UI grows a project browser; the hierarchy panel then shows real project content without changes.

## Troubleshooting

| Symptom | Cause |
| :--- | :--- |
| Panel says the graphics context could not be created; detail mentions `GLFW`, `WGL`, `GLX` or `EGL` | No OpenGL 3.3 driver, a remote session without GPU acceleration, or no X/Wayland display on Linux. |
| Detail mentions `GLFW can only be called from the main thread` | The hidden window was created off the main thread — see the threading section; `EditorHostService.Start` must run on the thread that called `Main`. |
| Detail mentions `WGL: Failed to make context current: The requested resource is in use` | The context is still current on the thread that created it. `HeadlessBackend.CreateGraphicsContext` releases it; a host that creates its own context must do the same before handing it to another thread. |
| Window stays blank, no failure panel, log says the host is listening | The frame socket never connected: check `_framework/blazor.webview.js` and `js/viewport.js` loaded (the log records the WebSocket URL), and that the webview supports WebGL2. |
| No window at all | Missing WebView2 runtime (Windows) or WebKitGTK 4.1 (Linux). |
| `EditorResources MISSING` in diagnostics | The build did not copy `EditorResources/`; build from the repository root so the `Content Include` in `CherrisEditorPhotino.csproj` runs. |

## Limitations

- **One editor window at a time.** A new connection replaces the previous one; the render loop keeps
  running with no client attached (frames are simply not published).
- **Local only.** The frame socket is loopback-only, unauthenticated and unencrypted. It is an
  implementation detail of one process, not a remote protocol.
- **Viewport cost.** One frame of readback latency, plus a GPU readback and a texture upload per frame. A
  1080p viewport is 8.3 MB per frame; the canvas is capped at 1920x1200 and scaled up by CSS.
- **Element-level picking and gizmos are not implemented.** The hierarchy panel selects by object; there is
  no viewport picking, so `Engine.CreateRayFromMouse` has no caller yet.
- **Scenes, not projects.** Opening a project loads its start scene and renders it; the hierarchy panel is
  read-only, there is no scene switching inside a session, no script compilation and no undo. Those stay in
  the ImGui editor for now.
- **No fly camera.** The web viewport forwards input, but a project scene is shown from its own camera; the
  ImGui editor's `EditorController` is editor-specific and is not attached here.
- **Uncompressed frames.** RGBA8 only. A JPEG/WebCodecs path would be the next step for remote use.
- **Platform coverage.** The build is verified by CI on Linux (`dotnet build` + tests); running the window
  needs a webview runtime and a GL 3.3 driver. WebView2 (Windows) and WebKitGTK (Linux) are the primary
  targets; WKWebView on macOS is untested, including whether GLFW wants the main thread there.
- **Linux audio.** `SceneManager` initializes OpenAL; on machines without it the engine logs and continues.
