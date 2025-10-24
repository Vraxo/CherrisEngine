using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace CherrisEditor;

/// <summary>
/// A helper class to load and manage icon textures for the editor UI.
/// </summary>
public class IconManager : IDisposable
{
    private readonly Dictionary<string, int> _icons = new();

    /// <summary>
    /// Loads an image from a file into an OpenGL texture and caches it.
    /// </summary>
    /// <param name="key">A unique key to identify the icon (e.g., "Folder").</param>
    /// <param name="path">The file path to the image.</param>
    public void LoadIcon(string key, string path)
    {
        if (_icons.ContainsKey(key) || !File.Exists(path))
        {
            if (!File.Exists(path)) Console.WriteLine($"[IconManager] Icon not found at path: {path}");
            return;
        }

        // We don't need to flip UI textures
        StbImage.stbi_set_flip_vertically_on_load(0);
        using var stream = File.OpenRead(path);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);

        // Set texture parameters for UI icons
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _icons[key] = handle;
    }

    /// <summary>
    /// Gets the GPU texture handle for a loaded icon.
    /// </summary>
    /// <param name="key">The key of the icon to retrieve.</param>
    /// <returns>An IntPtr to the texture, or IntPtr.Zero if not found.</returns>
    public IntPtr GetIcon(string key)
    {
        return _icons.TryGetValue(key, out int handle) ? (IntPtr)handle : IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var handle in _icons.Values)
        {
            GL.DeleteTexture(handle);
        }
    }
}