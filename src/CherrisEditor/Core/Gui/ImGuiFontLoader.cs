using Cherris.UI;
using Cherris.Utils;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;

namespace CherrisEditor.UI.Gui;

/// <summary>
/// Builds the ImGui font atlas: one entry per catalog theme, at that theme's body size and in its own face.
/// </summary>
/// <remarks>
/// Building every theme's font up front is what makes switching themes instant — changing a theme then only
/// moves <c>io.FontDefault</c> to another atlas entry, because the atlas itself cannot gain glyphs after it has
/// been uploaded. Each theme contributes exactly one entry; the first theme with an available font becomes the
/// default.
/// </remarks>
internal sealed class ImGuiFontLoader : IDisposable
{
    private readonly Dictionary<string, ImFontPtr> _fonts = new(StringComparer.Ordinal);
    private int _textureHandle;

    /// <summary>The font per theme id, for the themes whose font file was found.</summary>
    public IReadOnlyDictionary<string, ImFontPtr> Fonts => _fonts;

    /// <summary>Loads one font per theme and uploads the atlas.</summary>
    /// <param name="themes">Themes to build fonts for, in order.</param>
    public void Load(IReadOnlyList<EditorTheme> themes)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        foreach (EditorTheme theme in themes)
        {
            float pixels = theme.Typography.BodyPixels;
            string? fontPath = Resolve(theme.Typography.FontResource) ?? Resolve(theme.Typography.FallbackFontResource);

            if (fontPath is null)
            {
                continue;
            }

            _fonts[theme.Id] = io.Fonts.AddFontFromFileTTF(fontPath, pixels);
        }

        if (_fonts.Count == 0)
        {
            // Nothing shipped: fall back to ImGui's own bitmap font so the editor is still usable.
            _fonts[ThemeCatalog.Default.Id] = io.Fonts.AddFontDefault();
        }

        io.FontGlobalScale = 1.0f;

        CreateTexture();
    }

    /// <summary>Finds the atlas entry for a theme.</summary>
    /// <param name="themeId">The theme's id.</param>
    public ImFontPtr? FindFont(string themeId)
    {
        return _fonts.TryGetValue(themeId, out ImFontPtr font) ? font : null;
    }

    private static string? Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        string? path = EditorResources.Find(relativePath);

        return path is not null && File.Exists(path) ? path : null;
    }

    private void CreateTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        _textureHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _textureHandle);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

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
