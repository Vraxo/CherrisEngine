# Web / PhotinoX editor host

The editor has a second front end next to the ImGui window: a **PhotinoX Blazor** desktop app that renders
the game view off-screen and shows it inside a native webview window. It is additive — `src/CherrisEditor`,
`src/CherrisRuntime` and the engine's ImGui path are untouched.

| Project | What it is |
| :--- | :--- |
| `src/CherrisEditorPhotino` | The editor window. `PhotinoX.Blazor` host, Blazor components, viewport canvas, scene panel. |
| `src/CherrisEditorSession` | The session it drives, as a library: graphics context and render thread, frame pipe, wire protocol, input bridge, scene commands, script compilation. It has no webview and no window of its own. |
| `tools/CherrisFrameViewer` | The same session in a terminal, for iterating on the protocol or on a machine without a webview runtime. |
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
- **Skip: demo scene** — opens the built-in scene (camera, light, floor, cube) without a project. The cube
  spins once you press Play; an edited scene does not run.

History lives in `%APPDATA%\Cherris\recent_projects` and `last_project` (`~/.config/Cherris/` on Linux).
The ImGui editor writes the same two files, so a project opened in one front end appears in the other's
launcher. The model behind it — `Cherris.Projects.ProjectConfig`, `ProjectWorkspace`, `RecentProjects` —
lives in the engine library and is unit-tested (`tests/Cherris.Tests/Projects/`).

### What opening a project does

1. `ProjectWorkspace.Validate` resolves the folder and rejects anything without `project.yaml`, so a typo
   cannot reach the renderer.
2. The session starts (see threading below) with an `EditorSceneRequest` naming the project and its
   `StartScenePath`.
3. On the render thread, `EditorSession.LoadContent` points `ProjectFiles` at the project root (this is
   what makes scene-relative asset paths and a packaged `Assets.pak` resolve), loads the start scene with
   the engine's `SceneLoader`, and opens it.
4. A scene with no camera gets a default one — a brand-new project's start scene is empty, and the renderer
   needs a camera to draw anything. The reason lands in the log.
5. **The file is opened as it is, empty included.** A new project's `Main.yaml` is `GameObjects: []`, so the
   session opens with that one scene and the camera from step 4 in it; nothing is invented on top of the file.
   Only a file that does not parse as a scene at all falls back to the demo scene.
6. Whatever step 4 or 5 had to do is stated in the hierarchy panel, in a notice above the search field:
   `Main.yaml has no objects yet. The editor added a camera so the viewport can draw.` The hierarchy shows the
   session and the content browser's tiles preview the file, so the two can honestly differ — this is where
   the difference is said out loud instead of being left to be guessed at.

### Switching projects

`Projects…` in the editor toolbar stops the session and returns to the launcher; opening another project
starts a fresh session. The engine is not re-entered: the previous project's GL resources, asset root and
scene are dropped with the session, and the frame socket keeps the same port, so the viewport canvas
reconnects on its own.

`tools/CherrisFrameViewer` runs the same session with a terminal as the front end, for iterating on the
protocol or on a machine without a webview runtime:

```bash
dotnet run --project tools/CherrisFrameViewer            # http://localhost:5178/
dotnet run --project tools/CherrisFrameViewer -- --no-browser --port 5179
```

It serves its own `tools/CherrisFrameViewer/wwwroot` — a bare frame viewer, not the editor UI — and has the same
options plus `--listen-all` and `--help`.

## The window

`src/CherrisEditorPhotino/Program.cs` is the whole host:

```csharp
[STAThread]
private static int Main(string[] args)
{
    // The shell owns the webview; the session it opens later owns the GL context and the render thread.
    EditorHostService editorHost = new(Path.Combine(AppContext.BaseDirectory, "wwwroot"), ...);

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
| `EditorSession` | background ("Cherris Engine") | Makes the context current, runs the engine, captures the previous frame from `OnDrawUI` and publishes it. |
| `EditorHostService` | main, then any | One session at a time: validates the project, prepares the context on the main thread, starts the render thread, exposes URL, log, diagnostics, recent projects and failures. |
| `EditorSessionHost` | any | One session: the graphics context, the render thread, the listener, the viewport size, the active client and input translation into `HeadlessInput`. `Start()` is for a host with a message loop on its main thread, `Run()` for one without. |
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
stack holds the entry point — on every platform, not just macOS. `EditorSessionHost` owns that rule: `Start()`
creates the context on the thread that calls it (Blazor's dispatcher, which is the main thread) and then runs the
loop on a background thread, while `Run()` creates it on the thread that will also run the loop (the browser
host's main thread). The render thread only calls `MakeCurrent()`. A GL context is portable between threads and
nothing is ever presented from the hidden window, so the split is safe. Two details make it work:

- `CreateGraphicsContext` **releases** the context before returning — the host calls it from `Start`, and
  the render thread rebinds it. `NativeWindow`'s constructor makes the
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
| `ToolBar` — three centred transport buttons | `Components/ToolBar.razor` — Play/Pause, Stop, Restart, live (see [Play mode](#play-mode)) |
| Dockspace: left `Hierarchy` 20%, middle `Viewport` 72% + bottom tabs 28%, right `Inspector` 24% | `.dockspace` CSS grid with the same proportions; `Components/HierarchyPanel.razor`, `ViewportPanel.razor`, `ContentBrowserPanel.razor` / `ConsolePanel.razor` as the two bottom tabs, `InspectorPanel.razor` |
| `ViewportToolbar` — `Options` popup | same `Options` popup in `ViewportPanel`; **Show Grid** is a checkable item that turns the renderer's grid on and off (see [Flying the camera](#flying-the-camera)) |
| `ConsolePanel` — Clear, Auto-scroll, coloured levels | `ConsolePanel.razor` over `EditorHostService.LogEntries` |
| `ContentBrowserPanel` — `<- Back`, `Path: Project/…`, icon grid | `ContentBrowserPanel.razor`, a listing of the open project folder; scene tiles carry a preview and open the scene on double-click, and right-clicking creates files (see [Making things in the browser](#making-things-in-the-browser)) |
| Theme: `ImGuiTheme.Apply(EditorTheme)` | the same `EditorTheme`, applied as `--theme-*` tokens (see [themes.md](themes.md)) |

Colors come from the engine's own palette rather than an approximation of it — see
[UE5 Theme](ue5-theme.md) for every value, its source file, the widget styles it maps to, and the bundled Roboto/Roboto Mono fonts behind the typography.

Layout is additive: nothing in `src/CherrisEditor` changed. Splitters are dragged by `wwwroot/js/dock.js`,
which only writes three CSS custom properties (`--dock-left`, `--dock-right`, `--dock-bottom`), persists
them in `localStorage` the way ImGui persists its ini layout, and is reset by **Window > Reset Layout**.
Panels read mode-independent state (`SceneStore`, `SceneSelection`), so the hierarchy selection drives the
inspector without either component talking to the other.

### Selecting in the viewport

Clicking the rendered viewport selects what is under the pointer, exactly as clicking a hierarchy row does: the
same `SceneSelection` is written, so the inspector follows and the hierarchy highlights the row. Clicking empty
space clears the selection.

The rule is the ImGui editor's (`src/CherrisEditor/Scenes/EditorSelection.cs`): a ray from the main camera through
the clicked pixel, tested against every object's world-space bounds, nearest hit wins (`ViewportPicker`). Two
things are not clickable there and are not clickable here either — cameras and the skybox — and bounds are the
objects' boxes rather than their meshes, so a rotated object picks as if it filled its axis-aligned box.

The click comes in as an ordinary mouse press on the existing input path; what is new is that the press carries
the position it happened at (`mouseButton` with `x`/`y`), so a click that arrives before any move still picks
where it landed instead of wherever the pointer was last. Picking runs on the render thread, which owns the
scene, and the id crosses to the UI through `EditorSessionHost.ObjectPicked` and `EditorHostService.SelectionSink`
— that is the whole hand-off, and it is why `SceneSelection` guards its id with a lock. A press with the pointer
captured (the position frozen at the capture point) picks nothing.

### Flying the camera

The viewport's camera is flown with the mouse and the keyboard, using the desktop editor's own rules
(`ViewportCameraController`, mirroring `src/CherrisEditor/Core/EditorController.cs`):

| Input | What it does |
| :--- | :--- |
| Right-drag | Look around: 0.0015 rad per pixel, pitch stopped just short of straight up or down. |
| `W` `A` `S` `D` | Move along the view's yaw — 5 units a second, three times that with `Shift`. |
| `Q` `E` | Straight down and up, in world space. |
| Wheel | Fly along the view, half a unit per notch. |

Movement uses the yaw alone, so looking down and pressing `W` walks across the floor instead of diving into it.
The keys only count while the pointer is over the viewport, and nothing responds while the transport is playing,
where the scene's own scripts own the camera.

The right button captures the pointer (`requestPointerLock`) so a drag does not stop at the edge of the window,
and releases it on mouseup: a captured pointer is a frozen cursor, and clicking to select needs a real one. The
first movement after a capture is dropped, because browsers report the jump from wherever the cursor was to the
middle of the canvas.

The capture is a courtesy and the drag never depends on it, which is what makes the look work in every webview:

- The drag announces itself as its own message (`look`), sent *before* the capture is requested and before the
  button press is reported. The camera reads that flag rather than the engine's mouse button, so a capture that
  moves the focus, or a focus loss that clears held buttons mid-drag, cannot stop the camera while the browser is
  still sending movement. The button state is kept only as a fallback, for a client that never sends the flag.
- Deltas are the capture's own `movementX`/`movementY` when the pointer is locked, and the pointer's position
  otherwise, which is the only movement a webview without a capture can report. After a capture is given back the
  anchor is taken again, so the frozen pointer's last position is never read as one enormous flick.
- `requestPointerLock` is wrapped: some webviews throw rather than return a rejected promise, and an exception in
  the handler would otherwise take the press, the capture and the rest of that event with it.
- The canvas suppresses its context menu, so the right button is a look button on the page and not a menu.

Pointer events are taken from the `window` and matched against the canvas rectangle rather than listened for on the
canvas itself. Anything painted above the canvas would otherwise take the pointer with no trace on the host's side —
which is how the viewport ended up with working keys and a dead mouse for a while: `.viewport-overlay` set
`display: flex`, which outranks the user agent's `[hidden] { display: none }`, leaving an 85%-black pane permanently
over the canvas. The overlay is now `pointer-events: none`, has an explicit `[hidden]` rule, and the bridge sets its
`display` as well, so a message surface can never become a shield. Dialogs, menus and text fields keep their own
pointer events (`isEditorChrome`), so a dialog over the viewport does not start a look.

`wwwroot/js/viewport.js` and `wwwroot/css/app.css` are loaded with a version query in `index.html`; bump it when
either changes. The editor serves `wwwroot` from its build output, so a webview that holds an old copy would
otherwise keep running yesterday's input code — and the console shows one line per session, `A look drag reached the
render thread`, when a drag reaches the engine at all, plus `page: first right press landed on …` naming the element
the first right press hit. Between the two, "the page never sent it" and "the camera ignored it" are told apart
without guessing.

### Making and opening scenes

**File > New Scene…** writes a scene file into the open project and opens it. The file starts with a camera at the
default view and a sun, which is everything a scene needs to draw and be lit; the camera is the same one the editor
adds to a scene that has none, so a new scene and a repaired one look alike. The name has to be a plain file name in
the project folder (the extension is added when it is left off), and a name that is already taken is refused rather
than overwritten. `SceneTemplate` decides all of that, and writes the file through the engine's own
`SceneSerializer`, so what a new scene contains is decided by the code that saves scenes rather than by a template
kept beside it.

The dialog shows the exact path before anything is written, and reports the reason when it cannot write.

Opening a scene is not a new session: the render thread loads the file in place (`RequestSceneOpen`), closes the
scene it had, drops the undo stack — its commands name objects of the scene that has gone — and starts the new
scene the way a session starts the first one. The viewport keeps its connection and the panels keep polling, so a
switch costs a frame rather than a restart. A file that cannot be used leaves the open scene alone and says why in
the hierarchy, instead of falling back to the demo scene the way a broken start scene does.

Scenes are also opened by double-clicking their tile in the content browser — the way back to a scene another session
made. A tile the browser shows as a scene responds; the file is read by the session, not by the panel, so a double-click
never has to wait for a preview, and a YAML that turns out not to be a scene is refused in the hierarchy instead of
taking the viewport over with the demo scene.

### Making things in the browser

Right-clicking the content browser opens a menu at the pointer, and what it creates lands in the folder that was
under it: a folder tile means inside that folder, anything else means the folder being shown, and the project root
when neither applies. The menu is the same one everywhere — **New Scene…**, **New Script…**, **New Folder…** — plus
**Open** on a tile and **Refresh** for a folder that changed outside the editor.

All three go through one dialog (`NewItemDialog`), also used by **File > New Scene…**: a name field, the path the
name becomes before anything is written, and the reason when a name cannot be used. The dialog stays open on a
refusal, so a taken name costs a keystroke rather than the whole dialog, and closes itself when the file is written.

Where a thing may be written is the engine's rule, not a preference:

- **Scenes** go anywhere inside the project, and are opened when written.
- **Scripts** live in `Scripts/`, in either language — a Lua script is read by `LanguageScript`, a C# one is
  compiled into a component — and nowhere else. The menu offers one from the project root and from `Scripts/`
  itself, and is disabled with a reason anywhere else, rather than writing a file the engine would never run. The
  dialog carries a **Lua / C#** row, because the two make different things: a Lua file the engine reads when the
  scene runs, or a C# file compiled into a component that can be attached straight away.
- **Folders** go anywhere inside the project, and hold nothing of their own.

The name rules are one set for all of them (`ProjectFileName`): trimmed, no path separators, and none of the
characters Windows refuses, whatever the platform the editor is running on — a project is usually committed and
opened somewhere else. Scenes add their extension when it is left off. After creating, the browser follows the file:
a script created from anywhere leaves the browser in `Scripts/` with the new script selected, because that is where
it went.

### What a scene without a skybox is seen against

A `Skybox` is an asset reference, not a setting: the component holds a cube map loaded from a folder of six images
(`Right`, `Left`, `Top`, `Bottom`, `Front`, `Back`, square and all the same size), so a new scene cannot bring one
without also bringing the files it points at. `SceneTemplate` therefore still writes a camera and a sun and nothing
else, and the sky is the renderer's job instead: the skybox pass draws the scene's cube map when it has one and a
gradient when it does not, so the background is never just the buffer the frame was cleared to. That matters most
for a new scene, which otherwise opens as a black void with a grid fading out in it, but it is the same code path at
runtime — a game that never sets a skybox gets the horizon too.

The gradient's three colours live beside the pass that draws them (`OpenGLSkyboxRenderer.SkyZenith`, `SkyHorizon`,
`SkyGround`), so the look is tuned in one place, and they are linear values: the whole scene pass is composited
through the tone map and the gamma curve afterwards, which is why the numbers are darker than the picture they
produce. A skybox in the scene file changes none of this — a cube map that a backend cannot sample falls back to the
gradient rather than to an unbound texture.

### Scene previews

The content browser draws a scene file as a preview of the scene instead of a page icon: a top-down map of the
objects the file describes, with meshes as the ground they cover, lights as dots and cameras as markers pointing
where they look. The name stays under it and the tile's tooltip says what the preview found
("Main.yaml — 12 objects", or "empty scene").

Previews come from the YAML the serializer already writes — `GameObjects`, each object's `Transform`, and which
components it carries — read with the same YamlDotNet the serializer uses. Nothing is loaded, no components are
constructed and no GL context is involved, so a folder of scenes can be previewed without opening any of them.
`SceneThumbnail` holds the flattened scene, `SceneThumbnailLayout` fits it into the tile with one scale for both
axes so a square on the floor stays square, and `ScenePreviewStore` does the reading on a worker thread and
keeps each result until the file's write time or size changes.

What it is not: a render. A mesh is drawn at the footprint its scale describes, because how big a *named* mesh is
is not in the file — the demo's 20-unit floor is a plane at scale 1, so it reads as a 1×1 tile — and rotations
become headings rather than geometry. A file that does not parse as a scene, `project.yaml` included, keeps its
ordinary icon; a scene with no objects at all shows a dashed empty box, which is what a new project's start
scene is — and the hierarchy says the same thing in so many words when the session opens that empty scene.

One rule keeps the panels honest about what they can do: anything the web UI *cannot* do is drawn `disabled`
with a tooltip saying where that job lives, rather than as a button that silently does nothing — Save, Build and
the viewport's renderer options are all in that category today. Undo, Redo and the transport are not: they are
live, and greyed out only when there is nothing for them to do.

The inspector's Transform is the ImGui one's three Vector3 rows — Position, Rotation and Scale — with each
field labelled by its axis in the same red/green/blue (`TransformRows`, `.vec-axis`). Rotation is shown as
Euler degrees rather than as a quaternion, which is what the ImGui inspector edits
(`Quaternion.CreateFromYawPitchRoll` on the way back).

## Editing from the web UI

Everything the inspector shows is editable, and every edit is a command: **Add Component**, the object's name, the
transform rows, and each component property. The path is a queue rather than a lock, because the scene belongs
to the render thread while the panels render from published snapshots:

| Step | Where |
| :--- | :--- |
| The panel queues a command — `AddComponentCommand`, `AddScriptCommand`, `SetPropertyCommand`, `SetTransformCommand`, `RenameObjectCommand`, `UndoCommand`, `RedoCommand` | `InspectorPanel.razor` / `MenuBar.razor` → `EditorHostService` → `EditorSessionHost.Enqueue` |
| A dragged number queues **the same** command again every frame, so the scene moves with the pointer while the edit stays one step | `EditorHostService.SetPropertyLive` / `SetTransformLive` |
| The render thread drains the queue at the top of the frame, before it captures the frame | `EditorSession.ExecuteCommands` → `EditorCommandDispatcher.Drain` |
| The command edits the live scene, marks it dirty and records itself on the history — a fed command records and logs on its first effective run only | `IEditorCommand.Execute(EditorCommandContext)` |
| The console shows the result; the next snapshot carries the new values | `Logger` → `EditorHostService` log → `ConsolePanel` |

The same rule the ImGui inspector applies holds here: an object holds at most one of each type, so the menu
hides what is already there and the command refuses a duplicate if a stale menu asks anyway.

### Add Component: search, and scripts that do not exist yet

`Add Component` sits centred under the component list, with the same label and in the same place as the ImGui
inspector's button, and opens a search box with the focus already in it: an inline menu cannot use the `autofocus`
attribute, so the panel takes the focus in `OnAfterRenderAsync` once the box is on screen. Typing filters the
catalog, ignoring case and matching anywhere in the name. **Enter** takes the first match and **Escape** closes
the menu, so the whole thing works without the mouse.

A click anywhere else closes it too. The menu is inline rather than floating, so it carries its own scrim —
the same click-away the menubar's menus use — and is lifted over that scrim, which is what keeps the rows
clickable while every other click in the editor only dismisses it.

When the typed name matches no component — the way Unity's Add Component menu behaves — the menu offers to make
it a script, once per language:

| State of the typed name | Rows |
| :--- | :--- |
| not a usable name (`my script`, `2Player`) | `New Lua script: "my script"` and `New C# script: "my script"`, both disabled, tooltip saying which characters are allowed |
| no project open (the built-in demo scene) | both disabled, tooltip pointing at the project's `Scripts/` folder |
| `Scripts/Player.lua` already exists | `Script: "Player" (already in this project)` — it attaches that file |
| `Scripts/Player.cs` already exists | the C# row says the same, and asks for a compile first |
| otherwise | `New Lua script: "Player"` / `New C# script: "Player"` — each writes its own file and attaches it |

Two rows rather than one because the languages are not interchangeable: a Lua script becomes a `LanguageScript`
pointing at a file, a C# script becomes a compiled component. `Enter` takes the first row, the Lua one.

`ScriptTemplate` owns all of that knowledge — the `Scripts/` folder, the `[A-Za-z_][A-Za-z0-9_]*` name rule, the
extension and the starter source of each language — so the editor, the commands and `LanguageScript` cannot drift
apart on it. `TryCreate` writes the file **once**: a name whose file exists is reused and never overwritten,
because the user may have work in it, and undo removes the component rather than the file. The Lua template is the
two handlers the adapter calls, `start()` and `update(deltaTime)`, with nothing in them; the C# one is a
`Script` subclass named after the file, importing `Cherris.Components` — the namespace `Script` actually lives in,
which the ImGui editor's template got wrong by importing `Cherris`.

### C# scripts: compiled with the project

A `.cs` file under `Scripts/` is not something the engine can read at load time — it is a type that has to be
compiled first. That happens when the session opens the project: `ProjectScriptCompiler` (in the engine, shared with
the ImGui editor and the game builder) compiles every `.cs` under `Scripts/`, into a collectible load context, and
the non-abstract `Script` subclasses it finds are registered with the scene loader under their class names. The
registration happens **before the first scene is read**, because a factory that arrives afterwards is missing from
the scene that named it.

It also happens when this editor makes a script, because otherwise a script created from a menu could not be
attached until the project was reopened:

| Path | What compiles |
| :--- | :--- |
| opening a project | everything under `Scripts/`, before the first frame |
| the C# row in Add Component | inside the command, after the file is written and before the component is attached |
| New Script… → C#, in the content browser | a reload is requested, so the type is in the menu by the next frame |

`AddCSharpScriptCommand` is where the order matters most: it writes the file, asks the session to compile, looks for
a compiled class named after the file — the menu calls `PlayerController` "Player Controller", but the file, the
class and the scene all say `PlayerController` — and only then runs an ordinary `AddComponentCommand`. A file whose
class is named something else is left on disk with an explanation in the console, rather than attached by guesswork.

A reload swaps the collectible load context instead of unloading it: a component already attached keeps working
through its old types, and what changes is what the catalog and the scene loader know from then on. That is the
rebuild-or-lose trade, made deliberately — the alternative is a stale type staying registered forever.

Those types then behave like any other component:

| Where | What it does |
| :--- | :--- |
| Add Component menu | lists them after the built-ins, named the way the ImGui inspector names them (`PlayerController` reads as `Player Controller`) |
| a scene file | stores the class name, so a scene written by either editor opens in the other |
| Play | runs them through the same update the built-ins get |

A compile error does not close the project: the errors go to the console with their file and line, and the session
carries on with the components the scene's built-in types provide, so the mistake can be fixed and the project
reopened. What is still missing is a compile *before it matters*: there is no compile on save and none before Play,
so changing an existing script and then reopening the project is what picks the change up today. A **Reload scripts**
command that swaps the context on demand is the next step, and `RequestScriptReload` is the hook it would use.

Writing happens on the render thread, inside the command, so the UI thread never touches the project folder. The
one read the panel does itself is `File.Exists`, to label the row honestly.

### Which properties, and how they are drawn

`ComponentProperties` is the bridge, and it follows `DefaultInspector`'s filter: public instance properties
that are readable *and* writable and not `[HideInInspector]`, in declaration order, limited to the types the
ImGui drawer handles (string, bool, int, float, enum, Vector2, Vector3). Each one becomes a `PropertySnapshot`
with a `Kind`, which picks the control:

| Attribute / type | `PropertyKind` | Control |
| :--- | :--- | :--- |
| `bool` | `Boolean` | checkbox |
| `enum` | `Choice` | select |
| `float` with a usable `[Range]` | `Number` | slider plus a number field |
| `float` with an open-ended `[Range]` (`0 … float.MaxValue`) | `Number` | number field only — a slider cannot show it |
| `int`, `string` | `Integer`, `Text` | text field |
| `Vector2`, `Vector3` | `Vector2`, `Vector3` | one field per axis |
| `Vector3` with `[ColorUsage]` | `Vector3`, `IsColor` | colour well plus the axes |

Text is the wire format in both directions — the panel sends what the user typed, and `TryApply` parses it once,
on the render thread. `[Range]` clamps the written value, exactly as the ImGui slider does. A value that will
not parse is logged as one `[editor]` line and changes nothing, so a typo can never half-apply an edit or
throw on the render thread.

### Dragging a number

Every numeric field — the transform rows, a float property, the number box beside a slider, an integer, one
component of a Vector2/3 — also scrubs: press and move sideways and the value follows the pointer, the way
ImGui's `DragFloat` behaves. The cursor says so (`ResizeEW`, which is the cursor ImGui puts on its drag
widgets), and a plain click without movement still puts the caret in so the value can be typed.

| Field | Pixels per step | Mirrors |
| :--- | :--- | :--- |
| float property, no usable `[Range]` | 0.01 | `DragFloat(…, 0.01f)` |
| float property with a bounded `[Range]` | the range over 200 px (a 0…1 volume moves 0.005) | `SliderFloat`'s width |
| int property | 1 | `DragInt` |
| transform row, Vector2/3 component | 0.1 | `TransformInspector`'s `DragFloat(…, 0.1f)`, `DragFloat3` |
| a `[ColorUsage]` colour's components | 1/255, held at 0 and up | `ColorEdit3` |

**Shift** multiplies the step by ten and **Alt** divides it by a hundred, as ImGui's drag behaviour does.
`[Range]` clamps the drag too, and integers round, so what the field shows is what the command will accept.

The value is **live**, not applied on release: while the pointer moves, one `change` event goes out per
animation frame — the same event typing sends — so the scene, the gizmos and every other panel follow the
pointer at frame rate, exactly as `DragFloat` feeds its value once per frame. Moves that land between two
frames are coalesced, and a frame whose scrubbed value equals the one already sent is dropped.

A whole drag is still **one** command and **one** undo step, which is what `PropertyDrawer` gets from
`IsItemDeactivatedAfterEdit`:

* the panel sends every frame into `EditorHostService.SetPropertyLive` / `SetTransformLive`, which re-queue
  **the same** command object under a key of `property:{objectId}:{Type}.{Name}` or
  `transform:{objectId}:{Field}`;
* `SetPropertyCommand`/`SetTransformCommand` therefore run many times, but they record history, capture the
  value they replaced and log their console line **only on their first effective run**. Undo restores the
  value the drag found; Redo lands on the value it ended on; the console prints one line, not sixty a second;
* a frame that changes nothing records nothing, so dragging out and back to the starting value leaves no step.

The step opens and closes on the UI side without a call per frame: the script sends one `input` event when a
drag begins and the panel's `@oninput` closes the previous live session, while typing, `Undo`, `Redo`,
`SetProperty` and `SetTransform` all close it too — so two drags of the same field, or a drag followed by a
typed value, are separate steps. A drag also takes the focus while it runs and gives it up when the pointer
comes up, which is what rebuilds the field from the snapshot: a value the engine held back cannot stay on
screen as if it had been stored.

The drag lives in `wwwroot/js/numberdrag.js`, is wired by class (`drag-number`) with a document-level listener,
and needs no interop at all — fields Blazor renders later scrub without being registered.
`node tests/js/numberdrag.test.js` checks it (per-frame `change`, one final value, one `input` per drag), and CI
runs that after `dotnet test`; it needs no browser because the script only touches `window`, `document` and the
input it is handed.

### Resetting a value

Every value row ends in a reset button, in the narrow third column the ImGui inspector keeps for its own reset
(`DefaultInspector`/`TransformInspector`, `Reset` texture), and it restores the same thing: the value a freshly
constructed component of that type has, which is what the type's own initialisers write. The defaults are read
once per type and cached (`ComponentProperties.Defaults`), because the snapshot is rebuilt every frame; a type
that cannot be constructed with `Activator.CreateInstance` simply gets no button rather than a wrong value.

| Row | Reset to |
| :--- | :--- |
| a component property | its default: `Light.Intensity` → 1, `AudioSource.Volume` → 0.5, a `[ColorUsage]` colour → 1, 1, 1 |
| Transform → Position | 0, 0, 0 |
| Transform → Rotation | the identity, i.e. 0, 0, 0 Euler degrees |
| Transform → Scale | 1, 1, 1 |

The button has two looks and each says one thing, so the arrow's brightness never has to be decoded:

| The row | The button |
| :--- | :--- |
| holds its default | a washed-out arrow (30% of `AccentGray`), inert, and it does not react to the row being hovered |
| is away from the default | a filled button (`Secondary`) with the arrow at full reading strength, `cursor: pointer` |

Hovering a button only lightens that fill; the arrow keeps its colour in both states, so `#ffffff` never appears
on a control that was already bright. `PropertySnapshot.CanReset` and `TransformRow.CanReset` decide which state a
row is in, and neither 0.3 shown as "0.3" nor a quaternion read back as Euler degrees at 1e-6 counts as a change.
The object's name has no reset button: a name has no default to go back to, only whatever the author typed.

A reset is an ordinary edit — the same `SetPropertyCommand`/`SetTransformCommand` with
`DefaultText`/`TransformRow.Default` as the value — so it is one undo step, one console line, and it closes any
drag in progress before it records.

### Values the engine holds back

A field is never the only opinion about a value. `Camera` clamps its own three numbers as they are written —
`FieldOfView` into 1…179°, `NearClipPlane` at or above 0.001, `FarClipPlane` at least twice the near plane, and
a value that is not a number ignored rather than stored — because a near clip plane of -0.02 reaches
`Matrix4.CreatePerspectiveFieldOfView`, which throws `ArgumentOutOfRangeException: depthNear cannot be negative`
and takes the render thread with it. That crash is why the camera owns the rule instead of the inspector: the
editor's number field, a drag, a reset, a scene file and a script all write through the same setters
(`src/Cherris/Rendering/Camera.cs`), and `OpenTKRenderer` builds its projection from those values with the
aspect floored, so no camera a scene can hold stops the render loop.

When that happens the panel does not argue: the drag releases the focus, the field is rebuilt from the next
snapshot and shows what was actually stored, and the console line names that value rather than the one that was
typed.

### Undo and Redo

`EditorHistory` holds two stacks capped at 200; recording a change clears the redo branch. Only commands that
really changed something record themselves, and the commands that record capture the value they replaced, so
undo restores it exactly:

| Command | What undo restores |
| :--- | :--- |
| `AddComponentCommand` | removes the component again |
| `AddScriptCommand` | removes the script component; the file stays in the project |
| `SetPropertyCommand` | the previous text of that property, re-applied through the same parser; on a drag, the value the drag started from |
| `SetTransformCommand` | the whole position/rotation/scale triple, as it stood when the drag started |
| `RenameObjectCommand` | the previous name; an empty name is refused |

The stacks live on the dispatcher, so they belong to the render thread; `CanUndo`, `CanRedo`, `UndoLabel` and
`RedoLabel` are lock-guarded so the Edit menu can read them while it draws. The menu shows what it would take
back ("Undo add Light") and is disabled only when there is nothing there. Ctrl+Z and Ctrl+Y are not bound yet —
the menu items are the working path.

`IEditorCommand` is the extension point: play mode and save can become commands without changing this path, and
each one stays testable off the render thread because `Execute` takes the scene as an argument.

`ComponentCatalog` lives with the components in `src/Cherris`, so both front ends offer the same list.

## Play mode

The transport drives the scene for real. `EditorPlaySession` (`src/CherrisEditorSession/Engine/EditorPlaySession.cs`)
holds the same three states as the desktop editor's `PlayModeManager`, and the engine only advances the world
while the state is `Playing`:

| Button | Enabled when | What it does |
| :--- | :--- | :--- |
| Play / Pause — one toggle, like ImGui's | a scene is open | `Editing` or `Paused` → `Playing`; `Playing` → `Paused` |
| Stop | playing or paused | writes the authored values back, then returns to `Editing` |
| Restart | playing or paused | writes the authored values back, then plays again from them |

`EditorSession` sets `AutoUpdateScene = false` and calls `EditorPlaySession.Tick` itself, so physics, audio and
scripts are stepped only while playing. Everything else — camera input, the viewport, the panels, editing — keeps
running in all three states, which is what makes pause a freeze rather than a stop. The built-in demo's cube
spins only while playing, for the same reason: an edited scene is not a running one.

The snapshot is taken on the way out of editing, kept across a pause, and thrown away by Stop. It holds
everything the inspector can change, by value: the name, the transform, parenting, which objects exist, which
components are on them, and every editable property of those components (`ComponentProperties`, the same list the
inspector draws). Stop writes those back in place, so ids survive and the selection, hierarchy and inspector keep
working across play and stop. Objects play created are removed through `SceneManager` so their physics bodies go
with them; a `RigidBody` is moved with `Teleport` and its velocity zeroed, because the body — not the transform —
is the authority once physics has stepped, and a leftover velocity would throw it straight back out of position.

What it does not do is replace the desktop editor's reload:

- a script keeps its own fields, so the second play carries on from where the first left off rather than from a
  fresh instance, because a web session has no compiler to rebuild the assembly;
- `Scene.Start()` is not run again, because the engine already started the scene when it loaded it and a second
  `Start()` would register a second physics body for every `RigidBody`;
- a live edit made while playing is undone by Stop, but the undo stack is not: undoing a play-time edit afterwards
  writes back the play-time value.

Transport actions cross the thread the same way edits do, as a queued value rather than a lock: `EditorSessionHost`
writes one `PlaybackRequest` that the render loop takes at the top of the frame, before the commands and the
snapshot. The toolbar reads `EditorHostService.PlayState` — the action last asked for, which is what the buttons
should show while the render thread catches up — and the engine logs what it actually did (`[play] Playing.`,
`[play] Stopped; the scene is back to how it was authored.`) to the console, so the two never disagree for longer
than one frame.
