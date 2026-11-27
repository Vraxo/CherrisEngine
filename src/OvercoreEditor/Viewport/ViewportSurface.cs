using Overcore.Rendering.OpenTK;
using ImGuiNET;
using System.Numerics;

namespace OvercoreEditor.UI.Viewport;

public class ViewportSurface
{
    private Vector2 _currentSize;

    public bool IsHovered { get; private set; }
    public Vector2 Size => _currentSize;

    public void Draw(OpenTKRenderer renderer, Vector2 availableSize)
    {
        IsHovered = false;

        if (availableSize.X <= 0 || availableSize.Y <= 0)
        {
            return;
        }

        if (availableSize != _currentSize)
        {
            _currentSize = availableSize;
            renderer.SetViewportSize(_currentSize);
        }

        // The runtime UI is resolved in viewport-local pixels, while the native editor's mouse state is
        // window-local. Keep the current viewport origin even when docking moves it without resizing.
        renderer.SetViewportPosition(ImGui.GetCursorScreenPos());

        IntPtr textureHandle = renderer.GetSceneTextureHandle();

        if (textureHandle == IntPtr.Zero)
        {
            return;
        }

        ImGui.Image(textureHandle, _currentSize, new Vector2(0, 1), new Vector2(1, 0));
        IsHovered = ImGui.IsItemHovered();
    }
}