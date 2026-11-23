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
inspector without either component talking to the other.

One rule keeps the panels honest about what they can do: anything the web UI *cannot* do is drawn `disabled`
with a tooltip saying where that job lives, rather than as a button that silently does nothing — Save, Build,
play mode and the viewport's renderer options are all in that category today. Undo and Redo are not: they are
live, and greyed out only when their stack is empty.

The inspector's Transform is the ImGui one's three Vector3 rows — Position, Rotation and Scale — with each
field labelled by its axis in the same red/green/blue (`TransformRows`, `.vec-axis`). Rotation is shown as
Euler degrees rather than as a quaternion, which is what the ImGui inspector edits
(`Quaternion.CreateFromYawPitchRoll` on the way back).

## Editing from the web UI

Everything the inspector shows is editable, and every edit is a command: **Add ▾**, the object's name, the
transform rows, and each component property. The path is a queue rather than a lock, because the scene belongs
to the render thread while the panels render from published snapshots:

| Step | Where |
| :--- | :--- |
| The panel queues a command — `AddComponentCommand`, `AddScriptCommand`, `SetPropertyCommand`, `SetTransformCommand`, `RenameObjectCommand`, `UndoCommand`, `RedoCommand` | `InspectorPanel.razor` / `MenuBar.razor` → `EditorHostService` → `WebEditorHost.Enqueue` |
| A dragged number queues **the same** command again every frame, so the scene moves with the pointer while the edit stays one step | `EditorHostService.SetPropertyLive` / `SetTransformLive` |
| The render thread drains the queue at the top of the frame, before it captures the frame | `WebEditorEngine.ExecuteCommands` → `EditorCommandDispatcher.Drain` |
| The command edits the live scene, marks it dirty and records itself on the history — a fed command records and logs on its first effective run only | `IEditorCommand.Execute(EditorCommandContext)` |
| The console shows the result; the next snapshot carries the new values | `Logger` → `EditorHostService` log → `ConsolePanel` |

The same rule the ImGui inspector applies holds here: an object holds at most one of each type, so the menu
hides what is already there and the command refuses a duplicate if a stale menu asks anyway.

### Add ▾: search, and scripts that do not exist yet

`Add ▾` sits centred under the component list, and opens a search box with the focus already in it — an inline
menu cannot use the `autofocus` attribute, so the panel takes the focus in `OnAfterRenderAsync` once the box is
on screen. Typing filters the catalog, ignoring case and matching anywhere in the name. **Enter** takes the
first match and **Escape** closes the menu, so the whole thing works without the mouse.

When the typed name matches no component — the way Unity's Add Component menu behaves — the only row becomes an
offer to make it a script:

| State of the typed name | Row |
| :--- | :--- |
| not a usable name (`my script`, `2Player`) | `New script: "my script"`, disabled, tooltip saying which characters are allowed |
| no project open (the built-in demo scene) | disabled, tooltip pointing at the project's `Scripts/` folder |
| `Scripts/Player.lua` already exists | `Script: "Player" (already in this project)` — it attaches that file |
| otherwise | `New script: "Player"` — it writes the file and attaches it |

`ScriptTemplate` owns all of that knowledge — the `Scripts/` folder, the `[A-Za-z_][A-Za-z0-9_]*` name rule, and
the starter source — so the editor, the command and `LanguageScript` cannot drift apart on it. `AddScriptCommand`
writes the file **once**: a name whose file exists is reused and never overwritten, because the user may have
work in it, and undo removes the component rather than the file. The template is the two handlers the lua
adapter calls, `start()` and `update(deltaTime)`, with nothing in them; the script runs when the scene starts.

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
typed value, are separate steps.

The drag lives in `wwwroot/js/numberdrag.js`, is wired by class (`drag-number`) with a document-level listener,
and needs no interop at all — fields Blazor renders later scrub without being registered.
`node tests/js/numberdrag.test.js` checks it (per-frame `change`, one final value, one `input` per drag), and CI
runs that after `dotnet test`; it needs no browser because the script only touches `window`, `document` and the
input it is handed.

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
