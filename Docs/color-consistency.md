# 🎨 Color Consistency & Rendering Pipeline

**Date:** Feb 02, 2026  
**Context:** Fix for Editor Viewport vs. Runtime Standalone lighting mismatch.

---

## 🚨 The Issue

We observed a significant visual discrepancy between the **Editor Viewport** and the **Exported Game (Runtime)**:

* **Runtime:** Appeared bright, washed out, and gamma-corrected.
* **Editor:** Appeared dark, contrast-heavy, and linear.

This violated the **WYSIWYG** (What You See Is What You Get) principle, making it impossible to light scenes reliably.

---

## 🔍 Root Cause Analysis

The discrepancy stemmed from how **OpenGL Hardware Gamma Correction** interacts with **ImGui**.

1. **The Setup:**
   The `OpenGLPostProcessor` was creating the final composite framebuffer using `PixelInternalFormat.Srgb8Alpha8`.

2. **The Runtime Behavior (Correct Hardware Gamma):**
   
   * When the renderer wrote to this framebuffer, the GPU automatically converted the linear color values to sRGB (Gamma 2.2).
   * The final `BlitToScreen` call copied this gamma-corrected image to the monitor.
   * **Result:** Lighter image.

3. **The Editor Behavior (ImGui Sampling Issue):**
   
   * The renderer wrote the same gamma-corrected values to the texture.
   * However, the Editor renders the scene by passing this texture handle to **ImGui** (`ImGui.Image()`).
   * When ImGui's shader samples an `sRGB` texture, OpenGL often automatically converts it *back* to Linear space (linearization) for blending calculations.
   * **Result:** The Editor was effectively undoing the gamma correction, displaying raw Linear data (Darker) in the viewport.

---

## ✅ The Fix

We standardized the pipeline by **disabling hardware gamma correction** on the final composite buffer.

**Change:**
Modified `OpenGLPostProcessor.cs`:

```csharp
// Old (Hardware Gamma enabled)
.WithColorFormat(PixelInternalFormat.Srgb8Alpha8, PixelType.UnsignedByte)

// New (Raw Data / Linear)
.WithColorFormat(PixelInternalFormat.Rgba8, PixelType.UnsignedByte)
```

**Outcome:**

* The GPU no longer alters pixel values when writing to the framebuffer.
* **Runtime:** Blits raw pixels to screen.
* **Editor:** ImGui samples raw pixels to screen.
* **Result:** 100% Consistency. Editor and Runtime look identical.

---

## 🔮 Future Roadmap (Important)

Because we disabled hardware gamma correction, the engine is currently rendering in **Linear Space**. This explains why the scene may look "darker" than before.

**To restore brightness correctly:**
We should not rely on invisible hardware flags. Instead, we must implement **Explicit Gamma Correction** in the shader.

**Next Steps:**

1. Modify `Shaders/post_composite.frag`.

2. Add a final step to convert the color from Linear to sRGB manually:
   
   ```glsl
   // Simple Gamma Correction (approximate sRGB)
   const float gamma = 2.2;
   color = pow(color, vec3(1.0 / gamma));
   ```

3. This moves control from "Magic Hardware State" to "Explicit Shader Logic," giving us full control over tone mapping and exposure in the future.
