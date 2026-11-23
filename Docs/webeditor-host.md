# Web editor host (`src/CherrisEditorWeb`)

A minimal editor host that runs the engine off-screen and streams the game view to a browser page:
HTML/JS for the UI, `Cherris.Rendering.Headless` for rendering, a local HTTP/WebSocket server for the
transport.

It is the runnable half of the PhotinoX/Blazor design: the same page and the same protocol work inside a
webview window, so it can be developed in a normal browser first. See
[photinox-blazor-viewport.md](photinox-blazor-viewport.md) for the rendering-side contract.

## Why a local server instead of PhotinoX packages in this repo

The host deliberately has **no NuGet dependencies**: `HttpListener` + `System.Net.WebSockets` ship with the
runtime. Two reasons:

1. It keeps `dotnet build Cherris.slnx` working everywhere, including machines where the WebView2 or
   WebKitGTK runtimes are not installed.
2. PhotinoX only needs a URL. Wrapping this host in a native window is a five-line program (below) rather
   than a load-bearing part of the build.

## Running it

```bash
dotnet run --project src/CherrisEditorWeb
```

Defaults to <http://localhost:5178/>, size 1280x720, 60 fps, and opens the default browser. Options:

| Option | Meaning |
| :--- | :--- |
| `--port <n>` | HTTP/WebSocket port (default 5178) |
| `--width <n>` / `--height <n>` | Initial render size in device pixels |
| `--fps <n>` | Render loop rate (default 60) |
| `--listen-all` | Bind all interfaces (needs an URL ACL reservation on Windows) |
| `--no-browser` | Do not launch a browser |
| `--help` | Usage |

The page shows a demo scene (floor, spinning cube, directional light) because project loading still lives
in the ImGui editor. Replace `WebEditorEngine.LoadContent` to load a real scene with
`SceneLoader.LoadScene(...)` when the web UI grows a project browser.

## Architecture

| Piece | Thread | Responsibility |
| :--- | :--- | :--- |
| `WebEditorEngine` | main thread | Engine + `HeadlessBackend`; captures the previous frame from `OnDrawUI` and publishes it. |
| `WebEditorHost` | any | Owns the server, the desired viewport size, the active client, and translation of input commands into `HeadlessInput`. |
| `WebEditorServer` | thread pool | Serves `wwwroot` and upgrades `/ws` to a WebSocket. |
| `ViewerConnection` | thread pool | Per-client greeting, frame sending, command receiving. |
| `FramePipe` | shared | Single-slot frame hand-off: newest frame wins, stale frames are dropped and pooled. |

```
main thread                           socket thread                    browser
-----------                           -------------                    -------
Draw() ..... RenderFrame -> FBO
Update() ... OnDrawUI -> TryCapture -> PublishFrame -> FramePipe --->  binary frame
                                     <- TryReadCommand <- text msg  <-  key/mouse/resize
Update() ... HeadlessInput queue -> Input state (next frame)
```

The engine owns the process' main thread: an OpenGL context is thread-affine, and on macOS GLFW and
NSWindow want the main thread. The HTTP listener and WebSocket loops run on the thread pool, so the
server keeps accepting requests while the engine renders. Shutdown is requested from any thread (Ctrl+C)
and delivered to the loop through `WebEditorEngine.RequestStop`, which is polled once per frame.

Frame timing: `HeadlessGameWindow.SwapBuffers` paces the loop to `--fps`; the transport never blocks the
render thread, so a slow client lowers the visible frame rate instead of adding latency.

## Protocol

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

`ViewportProtocol` (C#) and `wwwroot/app.js` (JS) are the two halves of this contract; they are
versioned together and the client refuses a mismatched `protocol`.

## Wrapping it in PhotinoX

The host only needs to be running; the window just points at its URL. A separate project with the PhotinoX
packages (`PhotinoX`, `PhotinoX.Native`) can do this without touching anything here:

On Windows and Linux, run the host beside the window loop:

```csharp
using Photino.NET;

var hostThread = new Thread(() => new WebEditorHost(options, webRoot).Run()) { IsBackground = true };
hostThread.Start();

var window = new PhotinoWindow()
    .SetTitle("Cherris Editor")
    .SetSize(new System.Drawing.Size(1600, 900))
    .Center()
    .Load("http://localhost:5178/");

window.WaitForClose();
window.Close();          // also stop the host: engine.RequestStop() through the host's shutdown path
```

Exact member names follow the `PhotinoX` package's samples; the important part is that no JS interop is
needed, so the same page works in a browser, in WebView2, in WKWebView and in WebKitGTK.

On macOS both the GL context and the window loop want the main thread. There, create the hidden context on
the main thread first (`new HeadlessBackend()`), then run the engine loop on a background thread after
`MakeCurrent` on that thread, and keep PhotinoX's loop on the main thread. That path is untested; see the
threading notes in [photinox-blazor-viewport.md](photinox-blazor-viewport.md).

To move to JS interop later (a Blazor or `.js`-driven UI in the same process), implement
`IViewportSink`/`IViewportInputSink` on the Blazor component host instead of the socket host; nothing in
`Cherris.Rendering.Headless` changes.

## Limitations

- **One editor window at a time.** A new connection replaces the previous one; the render loop keeps
  running with no client attached (frames are simply not published).
- **No HTTP security.** Loopback only by default, no authentication, no TLS. Do not expose `--listen-all`
  on an untrusted network.
- **Element-level picking and gizmos are not implemented.** The page draws the frames and forwards input;
  there is no HTML overlay yet, so `Engine.CreateRayFromMouse` has no caller.
- **Demo scene only.** Project/scene/script loading is still in the ImGui editor.
- **Uncompressed frames.** RGBA8 only, roughly 3.7 MB per 1280x720 frame. A JPEG/WebCodecs path would be
  the next step for remote use over a slow link.
- **Linux audio.** `SceneManager` initializes OpenAL; on machines without it the engine logs and continues.
