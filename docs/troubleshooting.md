# 🛠️ Troubleshooting Guide

This document serves as a reference for debugging critical or recurring issues in the Cherris engine, ensuring they are not reintroduced in future updates.

---

## 👻 1. Invisible Viewport Texture (OpenGL)

**Problem:**
The main 3D scene renders as invisible in the Editor Viewport. The viewport panel appears "blank" (showing the ImGui window background color), as if no texture is being drawn.

**Observed Behavior:**

- The Viewport panel is open but empty.
- **ImGuizmo handles** (arrows/circles for Move/Rotate) **are visible** and functional.
- The 3D scene geometry, skybox, and debug lines are missing.
- No OpenGL errors are logged. Framebuffer handles appear valid.

### 🩺 Symptoms

- **👁️ UI:** Viewport texture is fully transparent or missing (shows panel background).
- **🖱️ Interaction:** ImGuizmo overlay works, confirming the Viewport panel is active and rendering ImGui layers.
- **📉 RenderDoc/Debugger:** The `Composite` framebuffer contains `(0,0,0,0)` (transparent black).
- **🖼️ Pipeline:** `MSAA` and `Resolved` framebuffers might contain correct data, but `Composite` output is empty.

### 🔍 Root Cause

**Render State Conflict (Face Culling):**

1. **Global State:** The `OpenTKRenderer` sets `GL.FrontFace(FrontFaceDirection.Cw)` (Clockwise) and enables `GL.CullFace(CullFaceMode.Back)` globally for 3D geometry.
2. **Post-Processing Quad:** The `OpenGLPostProcessor` renders a fullscreen quad for the final composition pass. This quad is defined with **Counter-Clockwise (CCW)** winding.
3. **The Conflict:** The GPU culls the fullscreen quad because it doesn't match the global Clockwise winding rule. The `Composite` shader never executes, leaving the framebuffer in its cleared (transparent) state.

### 💉 Solution

**Explicitly Disable Culling for Post-Processing:**

Screen-space passes (like Bloom or Composite) should not rely on 3D culling states. Explicitly disable culling and depth testing before rendering fullscreen quads.

```csharp
// In OpenGLPostProcessor.cs -> Composite()

// 1. Disable 3D geometry tests
GL.Disable(EnableCap.CullFace);
GL.Disable(EnableCap.DepthTest);

// 2. Render the Quad
_resources.FinalCompositeShader.Use();
_resources.RenderQuad();

// 3. Restore state (if necessary for subsequent passes)
GL.Enable(EnableCap.CullFace);
GL.Enable(EnableCap.DepthTest);
```

---

## 🎮 2. Runtime Input & Logic Freeze

**Problem:**
In the exported game (Runtime), the game appears frozen on the first frame, or input (Mouse Look / WASD) does not work, despite working perfectly in the Editor's "Play Mode".

**Observed Behavior:**

- The game launches, but objects (e.g., falling physics bodies) stay suspended in mid-air.
- The mouse cursor disappears (locks) correctly, but moving the mouse does not rotate the camera.
- Keyboard input seems unresponsive.
- Logs show initialization is successful, but scripts do not seem to execute.

### 🩺 Symptoms

- **🧊 Physics/Logic:** No movement, animations, or physics updates occur.
- **🖱️ Mouse:** `Input.MouseDelta` returns `(0,0)` even when moving the mouse.
- **📜 Scripts:** Custom scripts (like `PlayerController`) are not registered or running.

### 🔍 Root Causes

1. **Passive Engine Loop (Frozen Logic):** The base `Engine` class relied on the `Editor` to manually call `SceneManager.Update()`. In the Runtime, nothing was driving the scene logic.
2. **Editor-Biased Input (Input Blocking):** The low-level `OpenTKGameWindow` calculated `MouseDelta` **only** when the Right Mouse Button was held (an Editor-specific behavior). This prevented scripts from receiving delta data in the Runtime where the mouse is locked but buttons aren't held.
3. **Missing Script Discovery:** The Runtime lacked logic to search for and load the external `GameScripts.dll` at startup.

### 💉 Solution

**1. Auto-Update Default in Engine:**
The `Engine` class must drive the update loop by default. The Editor explicitly disables this to handle its own Edit/Play state.

```csharp
// In Engine.cs
protected bool AutoUpdateScene { get; set; } = true; // Default to true

protected virtual void Update(float deltaTime)
{
    if (AutoUpdateScene)
    {
        SceneManager.Update(deltaTime);
    }
}
```

**2. Unconditional Input Calculation:**
Input deltas must be calculated based on hardware events, not high-level logic. Scripts should filter input, not the windowing system.

```csharp
// In OpenTKGameWindow.cs -> OnMouseMove
// Calculate delta regardless of button state
var deltaX = currentPos.X - _lastMousePos.X;
Input.SetMouseDelta(new Vector2(deltaX, deltaY));
```

**3. Automatic Runtime Script Loading:**
The `Engine` constructor must assume it is in Runtime mode and attempt to load the game assembly.

```csharp
// In Engine.cs -> LoadRuntimeScripts()
if (File.Exists("GameScripts.dll")) {
    Assembly.LoadFrom("GameScripts.dll");
    // Register components...
}
```
