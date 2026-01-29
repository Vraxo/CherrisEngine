using Cherris;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.UI;

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

        IntPtr textureHandle = renderer.GetSceneTextureHandle();

        if (textureHandle == IntPtr.Zero)
        {
            return;
        }

        ImGui.Image(textureHandle, _currentSize, new Vector2(0, 1), new Vector2(1, 0));
        IsHovered = ImGui.IsItemHovered();
    }
}