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
```

Opens a 1600x900 native window: WebView2 on Windows, WKWebView on macOS, WebKitGTK 4.1 on Linux. The
Blazor UI fills the window, the game view fills the viewport panel, and the hierarchy panel shows the live
scene.

| Option | Meaning |
| :--- | :--- |
| `--port <n>` | Port for the internal frame socket. Default: a free loopback port, picked at startup. |
| `--width <n>` / `--height <n>` | Initial render size in device pixels (the viewport overrides it once it measures the canvas). |
| `--fps <n>` | Render loop rate (default 60). |

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
| `HeadlessBackend` hidden window | main, before the loop starts | Creates the GLFW window/context. OpenTK only allows GLFW init on the process' main thread. |
| `WebEditorEngine` | background ("Cherris Engine") | Makes the context current, runs the engine, captures the previous frame from `OnDrawUI` and publishes it. |
| `EditorHostService` | main, then any | Prepares the context on the main thread, starts the render thread, exposes URL, log, diagnostics and failures to the UI. |
| `WebEditorHost` | any | Owns the listener, the desired viewport size, the active client, and input translation into `HeadlessInput`. |
| `WebEditorServer` | thread pool | Serves `wwwroot` and upgrades `/ws` to a WebSocket. |
| `ViewerConnection` | thread pool | Per-client greeting, frame sending, command receiving. |
| `FramePipe` | shared | Single-slot frame hand-off: newest frame wins, stale frames are dropped and pooled. |
| `SceneSnapshotBuilder` | render thread | Copies the hierarchy into an immutable `SceneSnapshot` every 15 frames. |
| `ScenePanel` / `StatusBar` | UI thread | Poll (`250 ms` / `500 ms`) what the other threads publish; never touch engine state. |

```
main thread (native loop + Blazor)        engine thread                 socket thread
----------------------------------        -------------                 -------------
window / webview / components
  ViewportPanel --mount(ws)----> socket <---- PublishFrame <--- FramePipe
  ScenePanel ----poll---------- LatestScene <-- SceneSnapshotBuilder
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
window, so the split is safe. Once the context exists, `GLFWProvider.CheckForMainThread` is cleared —
its guard only tracks the creating thread and would otherwise reject the render thread's `MakeCurrent`.

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

The render thread publishes a `SceneSnapshot` (objects, transform, component type names) every 15 frames.
`ScenePanel` polls `EditorHostService.LatestScene` and diffs by reference, so the UI shows the hierarchy at
4 Hz without ever locking the engine. Snapshots are immutable copies: an older one cannot change while a
component is rendering it.

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
- **Demo scene only.** Project/scene/script loading, undo and asset editing are still in the ImGui editor.
- **Uncompressed frames.** RGBA8 only. A JPEG/WebCodecs path would be the next step for remote use.
- **Platform coverage.** The build is verified by CI on Linux (`dotnet build` + tests); running the window
  needs a webview runtime and a GL 3.3 driver. WebView2 (Windows) and WebKitGTK (Linux) are the primary
  targets; WKWebView on macOS is untested, including whether GLFW wants the main thread there.
- **Linux audio.** `SceneManager` initializes OpenAL; on machines without it the engine logs and continues.
