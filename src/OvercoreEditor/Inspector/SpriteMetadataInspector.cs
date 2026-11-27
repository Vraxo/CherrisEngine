using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Rendering;
using ImGuiNET;
using System.Numerics;
using System.Text.Json;

namespace OvercoreEditor.Inspectors;

/// <summary>Creates and edits the project's small JSON sprite-sidecar format from the Image inspector.</summary>
internal static class SpriteMetadataInspector
{
    public static bool Draw(Editor editor, Image image)
    {
        if (!ImGui.TreeNode("Sprite Metadata")) return false;
        bool componentChanged = false;
        string projectRoot = editor.ProjectManager.CurrentProject?.RootPath ?? String.Empty;
        SpriteMetadata? metadata = SpriteAssetLoader.Load(image.SpriteName);

        if (metadata is null)
        {
            ImGui.TextWrapped("No valid sprite sidecar is assigned. Create one from the Image texture, then adjust its source rect and borders here.");
            if (ImGui.Button("Create Sidecar From Texture") && TryCreate(editor, image, out string relativePath))
            {
                image.SpriteName = relativePath;
                componentChanged = true;
                SpriteAssetLoader.ClearCache();
            }
            ImGui.TreePop();
            return componentChanged;
        }

        ImGui.Text($"Texture size: {metadata.TextureWidth} x {metadata.TextureHeight}");
        string textureName = metadata.TextureName;
        bool changed = ImGui.InputText("Source Texture", ref textureName, 1024);
        ImGui.Separator();

        int x = metadata.Rect.X;
        int y = metadata.Rect.Y;
        int width = metadata.Rect.Width;
        int height = metadata.Rect.Height;
        Vector2 pivot = metadata.Pivot;
        float pixelsPerUnit = metadata.PixelsPerUnit;
        Vector4 border = metadata.Border;
        changed |= ImGui.InputInt("Rect X", ref x);
        changed |= ImGui.InputInt("Rect Y", ref y);
        changed |= ImGui.InputInt("Rect Width", ref width);
        changed |= ImGui.InputInt("Rect Height", ref height);
        changed |= ImGui.InputFloat2("Pivot", ref pivot);
        changed |= ImGui.InputFloat("Pixels Per Unit", ref pixelsPerUnit, 0.1f, 1f);
        changed |= ImGui.InputFloat4("Border L T R B", ref border);

        if (changed && !String.IsNullOrWhiteSpace(textureName)
            && TryGetSidecarPath(projectRoot, image.SpriteName, out string fullPath, out _))
        {
            x = Math.Clamp(x, 0, metadata.TextureWidth - 1);
            y = Math.Clamp(y, 0, metadata.TextureHeight - 1);
            width = Math.Clamp(width, 1, metadata.TextureWidth - x);
            height = Math.Clamp(height, 1, metadata.TextureHeight - y);
            pivot = Vector2.Clamp(pivot, Vector2.Zero, Vector2.One);
            pixelsPerUnit = float.IsFinite(pixelsPerUnit) ? MathF.Max(0.01f, pixelsPerUnit) : 100f;
            border = new Vector4(
                Math.Clamp(float.IsFinite(border.X) ? border.X : 0f, 0f, width),
                Math.Clamp(float.IsFinite(border.Y) ? border.Y : 0f, 0f, height),
                Math.Clamp(float.IsFinite(border.Z) ? border.Z : 0f, 0f, width),
                Math.Clamp(float.IsFinite(border.W) ? border.W : 0f, 0f, height));
            if (border.X + border.Z > width)
            {
                float scale = width / (border.X + border.Z);
                border.X *= scale;
                border.Z *= scale;
            }
            if (border.Y + border.W > height)
            {
                float scale = height / (border.Y + border.W);
                border.Y *= scale;
                border.W *= scale;
            }

            try
            {
                Write(fullPath, textureName, metadata.TextureWidth, metadata.TextureHeight,
                    x, y, width, height, pivot, pixelsPerUnit, border);
                SpriteAssetLoader.ClearCache();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Logger.Warning($"[Sprite Inspector] Could not save sidecar '{fullPath}': {exception.Message}");
                ImGui.TextWrapped($"Could not save metadata: {exception.Message}");
            }
        }

        ImGui.TreePop();
        return componentChanged;
    }

    private static bool TryCreate(Editor editor, Image image, out string relativePath)
    {
        relativePath = String.Empty;
        string projectRoot = editor.ProjectManager.CurrentProject?.RootPath ?? String.Empty;
        if (String.IsNullOrWhiteSpace(projectRoot) || String.IsNullOrWhiteSpace(image.TextureName))
        {
            return false;
        }

        ImageData? source = ImageLoader.LoadFromFile(image.TextureName, flipVertical: false);
        if (source is null || source.Value.Width <= 0 || source.Value.Height <= 0)
        {
            return false;
        }

        string texturePath = image.TextureName;
        if (Path.IsPathRooted(texturePath))
        {
            texturePath = Path.GetRelativePath(projectRoot, texturePath);
        }
        texturePath = texturePath.Replace('\\', '/');
        string sidecar = Path.ChangeExtension(texturePath, ".sprite.json");
        if (!TryGetSidecarPath(projectRoot, sidecar, out string fullPath, out relativePath))
        {
            return false;
        }

        try
        {
            Write(fullPath, texturePath, source.Value.Width, source.Value.Height,
                0, 0, source.Value.Width, source.Value.Height, new Vector2(0.5f), 100f, Vector4.Zero);
            SpriteAssetLoader.ClearCache();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Warning($"[Sprite Inspector] Could not create sidecar '{fullPath}': {exception.Message}");
            return false;
        }
    }

    private static void Write(string fullPath, string textureName, int textureWidth, int textureHeight,
        int x, int y, int width, int height, Vector2 pivot, float pixelsPerUnit, Vector4 border)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string json = JsonSerializer.Serialize(new
        {
            texture = textureName,
            textureSize = new[] { textureWidth, textureHeight },
            rect = new[] { x, y, width, height },
            pivot = new[] { pivot.X, pivot.Y },
            pixelsPerUnit,
            border = new[] { border.X, border.Y, border.Z, border.W }
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fullPath, json + Environment.NewLine);
    }

    private static bool TryGetSidecarPath(string projectRoot, string spriteName, out string fullPath, out string projectPath)
    {
        fullPath = String.Empty;
        projectPath = String.Empty;
        if (String.IsNullOrWhiteSpace(projectRoot) || String.IsNullOrWhiteSpace(spriteName)) return false;
        try
        {
            string root = Path.GetFullPath(projectRoot);
            string relative = Path.HasExtension(spriteName) ? spriteName : spriteName + ".sprite.json";
            string full = Path.GetFullPath(Path.IsPathRooted(relative) ? relative : Path.Combine(root, relative));
            string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            fullPath = full;
            projectPath = Path.GetRelativePath(root, full).Replace('\\', '/');
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
