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
