using Cherris.UI;
using Cherris.Utils;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;

namespace CherrisEditor.UI.Gui;

internal sealed class ImGuiFontLoader : IDisposable
{
    private int _textureHandle;

    public void Load()
    {
        ImGuiIOPtr io = ImGui.GetIO();

        // UE5 renders its UI in Roboto at 10pt (FStarshipCoreStyle::FStyleFonts); 13px is that size rounded to
        // the pixel grid. Roboto Mono is the fallback the editor shipped with, and the last resort is ImGui's
        // own bitmap font.
        float fontSize = Ue5Typography.NormalPixels;

        string? uiFontPath = EditorResources.Find(Ue5Typography.UiFontResource);
        string? monoFontPath = EditorResources.Find(Ue5Typography.MonospaceFontResource);

        if (uiFontPath is not null && File.Exists(uiFontPath))
        {
            io.Fonts.AddFontFromFileTTF(uiFontPath, fontSize);
        }
        else if (monoFontPath is not null && File.Exists(monoFontPath))
        {
            io.Fonts.AddFontFromFileTTF(monoFontPath, fontSize);
        }
        else
        {
            io.Fonts.AddFontDefault();
        }

        io.FontGlobalScale = 1.0f;

        CreateTexture();
    }

    private void CreateTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        _textureHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _textureHandle);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        io.Fonts.SetTexID(_textureHandle);
        io.Fonts.ClearTexData();
    }

    public void Dispose()
    {
        if (_textureHandle != 0)
        {
            GL.DeleteTexture(_textureHandle);
            _textureHandle = 0;
        }
    }
}