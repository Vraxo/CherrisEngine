# Hosting the game view in a webview (PhotinoX / Blazor)

This note explains the off-screen render path in `src/Cherris/Rendering/Headless/` and the contract a
webview-hosted editor (or a remote viewer) has to implement to show the game view. It is the "can the
viewport work in a Blazor UI?" prototype: **yes, as a streamed image** — a webview cannot host a native
OpenGL surface, so the viewport becomes a pixel stream instead of a control.

## Why streaming instead of embedding

PhotinoX is a wrapper around the OS webview (WebView2 on Windows, WKWebView on macOS, WebKitGTK 4.1 on
Linux). The webview owns the whole window surface and sits above it in the compositor, and PhotinoX
exposes no API to parent a native `OpenTK.GameWindow` inside or below it. There is therefore no way to
put a GL surface in a `<div>`.

Cherris is already shaped for the alternative: the scene is rendered into an off-screen framebuffer and
the editor only blits that texture into ImGui (`OpenTKRenderer` → `OpenGLPostProcessor.FinalSceneTexture`
→ `ViewportSurface.Draw`). Replacing `ImGui.Image` with a canvas upload is the same data taking a
different route.

## Pieces

| Type | File | Role |
| :--- | :--- | :--- |
| `HeadlessBackend` | `src/Cherris/Rendering/Headless/HeadlessBackend.cs` | `IRenderingInterface` with a hidden GL context, a `HeadlessGameWindow`, and an `OpenTKRenderer` that has no UI controller. |
| `HeadlessGameWindow` | `src/Cherris/Rendering/Headless/HeadlessGameWindow.cs` | `IGameWindow` stand-in: settable size, no-op events, and frame pacing in place of vsync. |
| `HeadlessInput` | `src/Cherris/Rendering/Headless/HeadlessInput.cs` | Thread-safe queue that applies browser input to the engine's static `Input` at frame start. |
| `SceneFrameCapture` | `src/Cherris/Rendering/Headless/SceneFrameCapture.cs` | Double-buffered pixel-pack readback of the final scene texture into a managed RGBA8 buffer. |

Nothing in `src/CherrisEditor` or `src/CherrisRuntime` changes. `Engine` gained a protected constructor
that accepts an `IRenderingInterface`, so a host can pass a `HeadlessBackend`:

```csharp
protected Engine(string windowTitle, bool startWithMouseLocked, IRenderingInterface backend);
```

## Frame contract

The host owns the loop, because the webview's message loop owns the UI thread. Run the engine on its own
thread and follow this order every frame:

1. `backend.SetViewportSize(width, height)` — device pixels, once per frame **before** rendering. The
   renderer only takes its off-screen path (`isRuntimeMode == false`, no blit to the window) on frames
   where a viewport size was set.
2. The engine renders (its `GameLoop` calls `Update` then `Draw`). `Draw` ends in
   `HeadlessGameWindow.SwapBuffers()`, which sleeps/spins to `TargetFrameRate` and so replaces vsync.
3. `capture.TryCapture(renderer.GetSceneTextureHandle(), width, height, out SceneFrame frame)` — queues a
   readback of the frame just drawn and returns the **previous** frame's pixels. On the first call, and
   the first call after a resize, it returns `false`.

Step 3 can also be done from `engine.OnDrawUI` (invoked at the start of `Update`, i.e. after the previous
frame's `Draw`), which avoids touching the engine at all.

### Pixel format

| Property | Value |
| :--- | :--- |
| Encoding | RGBA8, R in the first byte (matches `PixelFormat.Rgba` / `PixelType.UnsignedByte`) |
| Row order | top-down when `SceneFrameCapture.FlipVertically` is `true` (the default), because the browser canvas expects row 0 at the top and OpenGL produces row 0 at the bottom |
| Stride | `SceneFrame.RowPitch`, tightly packed (`Width * 4`) |
| Y flip | done on the render thread, one extra pass over the buffer |
| Lifetime | `SceneFrame.Pixels` is a reused buffer; upload it before the next `TryCapture` call |
| Cadence | one frame of latency by design; `SceneFrame.Sequence` increments per captured frame so a consumer can drop stale ones |

`glReadPixels` is issued from a pixel-pack buffer that was bound to `PixelPackBuffer`, so the transfer
returns immediately and the pixels are pulled out of the *other* buffer on the next call. That is what
keeps a 1080p readback from stalling the pipeline every frame.

### Sizing

Use the canvas's device pixels (`canvas.width` / `canvas.height`), not CSS pixels: `1920x1080` CSS at
`devicePixelRatio = 2` is a `3840x2160` readback and will not keep up. Cap the render resolution and let
the `<canvas>` element scale it up; a 1280x720 to 1600x900 viewport is a comfortable default.

Budget: 1920x1080 RGBA8 is 8.3 MB per frame, so 60 fps is roughly 500 MB/s of readback plus the same
again for the JS upload. 720p is about 3.7 MB per frame and is usually indistinguishable inside a docked
panel.

## Input contract

`Input` is static and unsynchronized, so it must be mutated on the render thread only. Browser events
therefore go through `HeadlessInput`, which queues them and applies them at the start of the next frame
(`HeadlessGameWindow.ProcessEvents` does this automatically).

| Browser event | Call |
| :--- | :--- |
| `keydown` / `keyup` | `HeadlessInput.PressKey(Key.W)` / `ReleaseKey(Key.W)` |
| `mousedown` / `mouseup` | `HeadlessInput.PressMouseButton(MouseButton.Left)` / `ReleaseMouseButton(...)` |
| `mousemove` (not locked) | `HeadlessInput.SetMousePosition(x, y)` — viewport-local device pixels |
| `mousemove` (pointer locked) | `HeadlessInput.MoveMouse(movementX, movementY)` — deltas accumulate per frame |
| `wheel` | `HeadlessInput.AddMouseWheel(0, deltaY)` |
| pointer lock change | `HeadlessInput.SetMouseLocked(bool)` |
| webview focus loss | `HeadlessInput.Reset()` so a held key cannot stay stuck |

Gameplay code that sets `Input.IsMouseLocked = true` raises `HeadlessGameWindow.MouseLockChanged` on the
render thread; the host marshals that to `element.requestPointerLock()` (and handles the browser's own
unlock, e.g. Escape, by calling `HeadlessInput.SetMouseLocked(false)`).

Keyboard mapping is the host's job: map `KeyboardEvent.code` (layout independent) onto `Cherris.Key` and
use `Key.Unknown` for anything unmapped.

## Host wiring example

```csharp
public sealed class WebEditorEngine : Engine
{
    public HeadlessBackend Backend { get; }

    public WebEditorEngine(HeadlessBackend backend)
        : base("Cherris", startWithMouseLocked: false, backend)
    {
        Backend = backend;
        Exposure = 0.5f;
    }

    protected override void LoadContent() { /* same as the desktop editor */ }
}

// Render thread:
var backend = new HeadlessBackend();               // creates the hidden GL context on this thread
var engine = new WebEditorEngine(backend);
using var capture = new SceneFrameCapture(1280, 720);

engine.OnDrawUI = _ =>
{
    backend.SetViewportSize(viewportWidth, viewportHeight);

    if (capture.TryCapture(backend.OpenGLRenderer!.GetSceneTextureHandle(), viewportWidth, viewportHeight, out var frame))
    {
        Publish(frame);                            // hand off to the webview
    }
};

engine.Run();
```

`Run()` blocks until something calls `backend.Close()`, so start it on a dedicated thread. The `Publish`
step is deliberately not implemented: it can be a `JSInterop` call into
`canvas.getContext('webgl2').texSubImage2D(...)`, a `WebSocket`/MJPEG endpoint, or shared memory. In JS
the upload looks like:

```js
function drawFrame(bytes, width, height) {
    const gl = canvas.getContext('webgl2');
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE,
        new Uint8Array(bytes));   // row 0 is the top of the image
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
}
```

## Host project setup

- The host executable needs `EditorResources/` next to it, because shaders are loaded from
  `<BaseDirectory>/EditorResources/Shaders/...` (`EditorResources.Find`). `GameBuilder` already copies that
  folder for exported builds; a webview editor project has to do the same (`Content Include="..\..\src\CherrisEditor\EditorResources\**\*"` with `CopyToOutputDirectory`).
- PhotinoX packages target .NET 8/9/10. A `net11.0` host can consume them, but check the `PhotinoX.Blazor`
  dispatcher does not assume it owns the thread you start the engine on.
- Windows needs the WebView2 runtime (present on Windows 10/11 by default, missing on clean server images);
  Linux needs WebKitGTK 4.1 plus its dev packages.

## Known limits

- **Latency and throughput.** One frame of readback latency plus the transport. Fine for an editor
  viewport; not appropriate for shipping a 60 fps game inside the same webview. Keep the standalone
  runtime on its native window.
- **Threading.** The hidden `NativeWindow`, the renderer, and `SceneFrameCapture` must all live on one
  thread, and that thread must create them before the webview handles UI work. On macOS, GLFW also needs
  the process' main thread to initialize, so create the backend before starting PhotinoX's message loop.
- **Recreate on resize cost.** A viewport resize reallocates the post-processor framebuffers and both
  pack buffers, and drops the pending frame (`TryCapture` returns `false` once). Debounce CSS animations
  instead of chasing every frame of a resize.
- **No in-engine UI.** `OpenTKRenderer` accepts a null `ImGuiController`; the UI pass is skipped. Docking,
  panels, gizmos and picking must be reimplemented in the Blazor UI. `ImGuizmo` has no webview
  equivalent, so gizmo interaction becomes an HTML overlay plus `Engine.CreateRayFromMouse`, which
  expects viewport-local coordinates for the webview path.
- **Selection outlines and debug draws** are part of the scene pass and are captured, but the editor's
  ImGui-drawn overlays are not.
- **Not included in this prototype:** the Blazor project itself, the transport (JS interop/WebSocket),
  browser key mapping, and a compressed (JPEG/WebCodecs) variant for low-bandwidth remoting.
