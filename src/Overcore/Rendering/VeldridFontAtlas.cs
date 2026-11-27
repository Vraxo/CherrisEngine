using ImGuiNET;
using Overcore.Core.Logging;
using Overcore.Rendering;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;

namespace Overcore.Core;

/// <summary>CPU-generated ImGui glyph atlas uploaded as a backend-neutral Veldrid texture.</summary>
internal sealed class VeldridFontAtlas : IDisposable
{
    internal readonly record struct Glyph(float AdvanceX, float X0, float Y0, float X1, float Y1,
        float U0, float V0, float U1, float V1);

    private readonly GraphicsDevice _device;
    private readonly IntPtr _context;
    private readonly Dictionary<string, ImFontPtr> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<global::Overcore.Texture> _retiredTextures = [];
    private ImFontPtr _defaultFont;
    private ImFontPtr _arabicFallbackFont;
    private global::Overcore.Texture _texture;
    private bool _disposed;

    public ITexture Texture => _texture;

    public VeldridFontAtlas(GraphicsDevice device)
    {
        _device = device;
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        ImGuiIOPtr io = ImGui.GetIO();
        string? path = ProjectFontAssets.Find(null);
        if (path is not null && File.Exists(path))
        {
            _defaultFont = io.Fonts.AddFontFromFileTTF(path, 18f);
            _fonts[Path.GetFullPath(path)] = _defaultFont;
        }
        else
        {
            _defaultFont = io.Fonts.AddFontDefault();
        }
        _arabicFallbackFont = ArabicFontAtlas.AddFallback(io.Fonts, 18f);
        BuildTexture(uploadImmediately: true);
    }

    public unsafe void EnsureFont(string? fontName)
    {
        if (String.IsNullOrWhiteSpace(fontName)) return;
        string? path = ProjectFontAssets.Find(fontName);
        if (path is null || !File.Exists(path)) return;
        path = Path.GetFullPath(path);
        if (_fonts.ContainsKey(path) || _failedFonts.Contains(path)) return;

        try
        {
            ImGui.SetCurrentContext(_context);
            ImFontPtr font = ImGui.GetIO().Fonts.AddFontFromFileTTF(path, 18f);
            if (font.NativePtr == null)
            {
                _failedFonts.Add(path);
                Logger.Warning($"[Veldrid UI] Could not load font '{path}'; using the bundled font.");
                return;
            }
            _fonts[path] = font;
            BuildTexture(uploadImmediately: true);
        }
        catch (Exception exception)
        {
            _failedFonts.Add(path);
            Logger.Warning($"[Veldrid UI] Could not load font '{path}': {exception.Message}");
        }
    }

    public float GetFontPixelSize(string? fontName) => GetFont(fontName).FontSize;

    public unsafe bool TryGetGlyph(string? fontName, Rune codepoint, out Glyph glyph)
    {
        // ImGui.NET 1.89 exposes ushort glyph lookup; retain supplementary characters as one fallback glyph.
        ushort atlasCodepoint = codepoint.Value <= UInt16.MaxValue ? (ushort)codepoint.Value : (ushort)'?';
        ImFontPtr font = GetFont(fontName);
        ImFontGlyphPtr source = font.FindGlyphNoFallback(atlasCodepoint);
        if (source.NativePtr == null && _arabicFallbackFont.NativePtr != null)
        {
            source = _arabicFallbackFont.FindGlyphNoFallback(atlasCodepoint);
        }
        if (source.NativePtr == null) source = font.FindGlyph(atlasCodepoint);
        glyph = new Glyph(source.AdvanceX, source.X0, source.Y0, source.X1, source.Y1,
            source.U0, source.V0, source.U1, source.V1);
        return source.AdvanceX > 0f || (source.X0 != source.X1 && source.Y0 != source.Y1);
    }

    private ImFontPtr GetFont(string? fontName)
    {
        ImGui.SetCurrentContext(_context);
        if (!String.IsNullOrWhiteSpace(fontName) && ProjectFontAssets.Find(fontName) is { } path)
        {
            if (_fonts.TryGetValue(Path.GetFullPath(path), out ImFontPtr font)) return font;
        }
        return _defaultFont;
    }

    private void BuildTexture(bool uploadImmediately)
    {
        ImGui.SetCurrentContext(_context);
        ImGui.GetIO().Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);
        byte[] data = new byte[checked(width * height * 4)];
        Marshal.Copy(pixels, data, 0, data.Length);
        ImGui.GetIO().Fonts.ClearTexData();

        Veldrid.Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width, (uint)height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        TextureView view = _device.ResourceFactory.CreateTextureView(texture);
        global::Overcore.Texture next = new(texture, view);
        if (_texture is not null) _retiredTextures.Add(_texture);
        _texture = next;
        if (uploadImmediately)
        {
            _device.UpdateTexture(texture, data, 0, 0, 0, (uint)width, (uint)height, 1, 0, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texture?.Dispose();
        foreach (global::Overcore.Texture texture in _retiredTextures) texture.Dispose();
        ImGui.SetCurrentContext(_context);
        ImGui.DestroyContext(_context);
    }
}
