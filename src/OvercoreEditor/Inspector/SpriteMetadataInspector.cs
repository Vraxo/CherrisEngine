using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Rendering;
using Overcore.Utils;
using ImGuiNET;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OvercoreEditor.Inspectors;

/// <summary>Creates, previews, and edits the project's small JSON sprite-sidecar format from the Image inspector.</summary>
internal static class SpriteMetadataInspector
{
    private sealed class SelectionState
    {
        public bool Dragging { get; set; }
        public Vector2 Start { get; set; }
    }

    private static readonly ConditionalWeakTable<Image, SelectionState> s_selections = new();

    public static bool Draw(Editor editor, EditorTextureManager textures, Image image)
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

        string textureName = metadata.TextureName;
        bool changed = ImGui.InputText("Source Texture", ref textureName, 1024);
        SpriteMetadata activeMetadata = metadata;
        bool sourcePathValid = true;
        if (!String.Equals(textureName, metadata.TextureName, StringComparison.Ordinal))
        {
            if (EditorTextureManager.ImageExtensions.Contains(Path.GetExtension(textureName), StringComparer.OrdinalIgnoreCase)
                && ProjectFiles.Find(textureName) is { } sourcePath
                && IsWithinProject(projectRoot, sourcePath)
                && ImageLoader.LoadFromFile(sourcePath, flipVertical: false) is { } sourceData
                && sourceData.Width > 0 && sourceData.Height > 0)
            {
                activeMetadata = metadata with
                {
                    TextureName = textureName,
                    TextureWidth = sourceData.Width,
                    TextureHeight = sourceData.Height,
                    Rect = new SpritePixelRect(0, 0, sourceData.Width, sourceData.Height),
                    Border = Vector4.Zero
                };
                changed = true;
            }
            else
            {
                sourcePathValid = false;
                ImGui.TextWrapped("Source texture must resolve to a supported image inside the project.");
            }
        }

        ImGui.Text($"Texture size: {activeMetadata.TextureWidth} x {activeMetadata.TextureHeight}");
        ImGui.Separator();

        int x = activeMetadata.Rect.X;
        int y = activeMetadata.Rect.Y;
        int width = activeMetadata.Rect.Width;
        int height = activeMetadata.Rect.Height;
        Vector2 pivot = activeMetadata.Pivot;
        float pixelsPerUnit = activeMetadata.PixelsPerUnit;
        Vector4 border = activeMetadata.Border;
        changed |= ImGui.InputInt("Rect X", ref x);
        changed |= ImGui.InputInt("Rect Y", ref y);
        changed |= ImGui.InputInt("Rect Width", ref width);
        changed |= ImGui.InputInt("Rect Height", ref height);
        changed |= ImGui.InputFloat2("Pivot", ref pivot);
        changed |= ImGui.InputFloat("Pixels Per Unit", ref pixelsPerUnit, 0.1f, 1f);
        changed |= ImGui.InputFloat4("Border L T R B", ref border);
        changed |= DrawAtlasPreview(projectRoot, textures, image, activeMetadata, ref x, ref y, ref width, ref height);
        string newSpriteName = Path.GetFileNameWithoutExtension(image.SpriteName);
        ImGui.InputText("New Sprite Name", ref newSpriteName, 128);
        if (sourcePathValid)
        {
            ImGui.SameLine();
            if (ImGui.Button("Create From Selection")
                && TryCreateFromSelection(editor, image, activeMetadata, newSpriteName, x, y, width, height, out string newSidecar))
            {
                image.SpriteName = newSidecar;
                componentChanged = true;
            }
        }

        if (changed && sourcePathValid && !String.IsNullOrWhiteSpace(textureName)
            && TryGetSidecarPath(projectRoot, image.SpriteName, out string fullPath, out _))
        {
            x = Math.Clamp(x, 0, activeMetadata.TextureWidth - 1);
            y = Math.Clamp(y, 0, activeMetadata.TextureHeight - 1);
            width = Math.Clamp(width, 1, activeMetadata.TextureWidth - x);
            height = Math.Clamp(height, 1, activeMetadata.TextureHeight - y);
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
                Write(fullPath, textureName, activeMetadata.TextureWidth, activeMetadata.TextureHeight,
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

    private static bool DrawAtlasPreview(string projectRoot, EditorTextureManager textures, Image image,
        SpriteMetadata metadata, ref int x, ref int y, ref int width, ref int height)
    {
        if (!EditorTextureManager.ImageExtensions.Contains(Path.GetExtension(metadata.TextureName), StringComparer.OrdinalIgnoreCase)
            || ProjectFiles.Find(metadata.TextureName) is not { } texturePath
            || !IsWithinProject(projectRoot, texturePath))
        {
            ImGui.TextWrapped("Atlas preview unavailable: the source texture is not a supported on-disk image.");
            return false;
        }

        IntPtr texture = textures.GetTextureForPath(texturePath);
        if (texture == IntPtr.Zero)
        {
            ImGui.TextWrapped("Atlas preview unavailable: the source texture could not be decoded.");
            return false;
        }

        float availableWidth = MathF.Max(64f, ImGui.GetContentRegionAvail().X);
        float displayScale = MathF.Min(1f, MathF.Min(480f, availableWidth) / metadata.TextureWidth);
        Vector2 displaySize = new(metadata.TextureWidth * displayScale, metadata.TextureHeight * displayScale);
        ImGui.Text("Drag on the atlas to choose the sprite rect.");
        ImGui.Image(texture, displaySize, Vector2.Zero, Vector2.One);
        Vector2 screenMin = ImGui.GetItemRectMin();
        bool hovered = ImGui.IsItemHovered();
        SelectionState selection = s_selections.GetValue(image, static _ => new SelectionState());
        bool changed = false;

        Vector2 ToPixel(Vector2 point) => new(
            Math.Clamp((point.X - screenMin.X) / displaySize.X * metadata.TextureWidth, 0f, metadata.TextureWidth),
            Math.Clamp((point.Y - screenMin.Y) / displaySize.Y * metadata.TextureHeight, 0f, metadata.TextureHeight));

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            selection.Dragging = true;
            selection.Start = ToPixel(ImGui.GetMousePos());
        }

        Vector2 rectStart = new(x, y);
        Vector2 rectEnd = new(x + width, y + height);
        if (selection.Dragging)
        {
            Vector2 current = ToPixel(ImGui.GetMousePos());
            rectStart = Vector2.Min(selection.Start, current);
            rectEnd = Vector2.Max(selection.Start, current);
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                x = Math.Clamp((int)MathF.Floor(rectStart.X), 0, metadata.TextureWidth - 1);
                y = Math.Clamp((int)MathF.Floor(rectStart.Y), 0, metadata.TextureHeight - 1);
                width = Math.Clamp((int)MathF.Ceiling(rectEnd.X) - x, 1, metadata.TextureWidth - x);
                height = Math.Clamp((int)MathF.Ceiling(rectEnd.Y) - y, 1, metadata.TextureHeight - y);
                selection.Dragging = false;
                changed = true;
            }
        }

        Vector2 boxMin = screenMin + new Vector2(rectStart.X / metadata.TextureWidth, rectStart.Y / metadata.TextureHeight) * displaySize;
        Vector2 boxMax = screenMin + new Vector2(rectEnd.X / metadata.TextureWidth, rectEnd.Y / metadata.TextureHeight) * displaySize;
        ImGui.GetWindowDrawList().AddRect(boxMin, boxMax, ImGui.GetColorU32(new Vector4(1f, 0.7f, 0.1f, 1f)), 0f, ImDrawFlags.None, 2f);
        return changed;
    }

    private static bool TryCreateFromSelection(Editor editor, Image image, SpriteMetadata metadata,
        string spriteName, int x, int y, int width, int height, out string relativePath)
    {
        relativePath = String.Empty;
        spriteName = spriteName.Trim();
        if (String.IsNullOrWhiteSpace(spriteName) || spriteName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || spriteName.Contains('/') || spriteName.Contains('\\'))
        {
            ImGui.TextWrapped("Enter a valid filename for the new sprite sidecar.");
            return false;
        }

        string projectRoot = editor.ProjectManager.CurrentProject?.RootPath ?? String.Empty;
        string directory = Path.GetDirectoryName(image.SpriteName) ?? String.Empty;
        string newPath = Path.Combine(directory, spriteName + ".sprite.json").Replace('\\', '/');
        if (!TryGetSidecarPath(projectRoot, newPath, out string fullPath, out relativePath) || File.Exists(fullPath))
        {
            ImGui.TextWrapped("Could not create the sidecar; check the project path and choose a name that is not already used.");
            return false;
        }

        x = Math.Clamp(x, 0, metadata.TextureWidth - 1);
        y = Math.Clamp(y, 0, metadata.TextureHeight - 1);
        width = Math.Clamp(width, 1, metadata.TextureWidth - x);
        height = Math.Clamp(height, 1, metadata.TextureHeight - y);
        Vector4 border = Vector4.Zero;
        try
        {
            Write(fullPath, metadata.TextureName, metadata.TextureWidth, metadata.TextureHeight,
                x, y, width, height, metadata.Pivot, metadata.PixelsPerUnit, border);
            SpriteAssetLoader.ClearCache();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Warning($"[Sprite Inspector] Could not create sidecar '{fullPath}': {exception.Message}");
            ImGui.TextWrapped($"Could not create sidecar: {exception.Message}");
            return false;
        }
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

    private static bool IsWithinProject(string projectRoot, string path)
    {
        try
        {
            string root = Path.GetFullPath(projectRoot);
            string full = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(root, full);
            return !Path.IsPathRooted(relative) && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
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
