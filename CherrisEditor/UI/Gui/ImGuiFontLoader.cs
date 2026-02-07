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

        const float fontSize = 18.0f;
        string? fontPath = EditorResources.Find("Fonts/RobotoMono-Regular.ttf");

        if (fontPath is not null && File.Exists(fontPath))
        {
            io.Fonts.AddFontFromFileTTF(fontPath, fontSize);
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