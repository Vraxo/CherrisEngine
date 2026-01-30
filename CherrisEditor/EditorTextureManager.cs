using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace CherrisEditor;

/// <summary>
/// A helper class to load, cache, and manage editor textures (icons and thumbnails).
/// </summary>
public class EditorTextureManager : IDisposable
{
    private readonly Dictionary<string, int> _textures = [];
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
    private static readonly string[] ScriptExtensions = { ".cs" };
    public static readonly string[] PrefabExtensions = { ".prefab" };
    public static readonly string[] MeshExtensions = { ".obj", ".gltf", ".glb", ".fbx", ".dae" };


    /// <summary>
    /// Loads an image from a file into an OpenGL texture and caches it.
    /// </summary>
    /// <param name="key">A unique key to identify the texture (e.g., "Folder" or a file path).</param>
    /// <param name="path">The file path to the image.</param>
    public void LoadTexture(string key, string path)
    {
        if (_textures.ContainsKey(key) || !File.Exists(path))
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[TextureManager] Icon not found at path: {path}");
            }

            return;
        }

        int handle = LoadTextureFromFile(path);
        if (handle != 0)
        {
            _textures[key] = handle;
        }
    }

    /// <summary>
    /// Gets the GPU texture handle for a loaded texture.
    /// </summary>
    /// <param name="key">The key of the texture to retrieve.</param>
    /// <returns>An IntPtr to the texture, or IntPtr.Zero if not found.</returns>
    public IntPtr GetTexture(string key)
    {
        return _textures.TryGetValue(key, out int handle) ? handle : IntPtr.Zero;
    }

    /// <summary>
    /// Gets a texture handle for a given asset path. If it's a supported image,
    /// it loads it as a thumbnail. If not, it returns a default icon.
    /// </summary>
    /// <param name="path">The full file path of the asset.</param>
    /// <returns>An IntPtr to the appropriate texture.</returns>
    public IntPtr GetTextureForPath(string path)
    {
        if (Directory.Exists(path))
        {
            return GetTexture("Folder");
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (ScriptExtensions.Contains(extension))
        {
            return GetTexture("Script");
        }

        if (PrefabExtensions.Contains(extension))
        {
            return GetTexture("Prefab");
        }

        if (ImageExtensions.Contains(extension))
        {
            // It's an image file, try to load it as a thumbnail.
            if (_textures.TryGetValue(path, out int handle))
            {
                return handle; // Return cached thumbnail
            }

            // Not cached, load it now.
            int newHandle = LoadTextureFromFile(path);
            if (newHandle != 0)
            {
                _textures[path] = newHandle;
                return newHandle;
            }
        }

        return GetTexture("File"); // Fallback for any other file type
    }

    private int LoadTextureFromFile(string path)
    {
        try
        {
            // We don't need to flip UI textures
            StbImage.stbi_set_flip_vertically_on_load(0);
            using var stream = File.OpenRead(path);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            int handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            return handle;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[TextureManager] Failed to load texture from '{path}': {e.Message}");
            return 0;
        }
    }

    public void Dispose()
    {
        foreach (var handle in _textures.Values)
        {
            GL.DeleteTexture(handle);
        }
        _textures.Clear();
    }
}