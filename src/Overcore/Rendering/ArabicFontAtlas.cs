using ImGuiNET;
using System.Runtime.InteropServices;

namespace Overcore.Rendering;

/// <summary>Adds a separate Arabic/Persian glyph font to an ImGui font atlas for fallback lookup.</summary>
internal static class ArabicFontAtlas
{
    private static readonly ushort[] s_glyphRanges =
    [
        0x0600, 0x06FF,
        0x0750, 0x077F,
        0x0870, 0x089F,
        0x08A0, 0x08FF,
        0xFB50, 0xFDFF,
        0xFE70, 0xFEFF,
        0
    ];
    private static readonly GCHandle s_pinnedRanges = GCHandle.Alloc(s_glyphRanges, GCHandleType.Pinned);
    private static readonly IntPtr s_rangePointer = s_pinnedRanges.AddrOfPinnedObject();

    public static unsafe ImFontPtr AddFallback(ImFontAtlasPtr fonts, float sizePixels)
    {
        if (!s_pinnedRanges.IsAllocated) return default;
        string? path = ProjectFontAssets.FindArabicFallback();
        if (path is null) return default;

        ImFontConfig* nativeConfig = ImGuiNative.ImFontConfig_ImFontConfig();
        if (nativeConfig == null) return default;
        ImFontConfigPtr config = new(nativeConfig);
        try
        {
            config.GlyphRanges = s_rangePointer;
            return fonts.AddFontFromFileTTF(path, sizePixels, config, s_rangePointer);
        }
        finally
        {
            config.Destroy();
        }
    }
}
