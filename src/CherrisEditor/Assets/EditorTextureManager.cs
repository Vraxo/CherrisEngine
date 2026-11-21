using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace CherrisEditor;

public class EditorTextureManager : IDisposable
{
    private readonly Dictionary<string, int> _textures = [];
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
    public static readonly string[] PrefabExtensions = { ".prefab" };
    public static readonly string[] MeshExtensions = { ".obj", ".gltf", ".glb", ".fbx", ".dae" };

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

    public IntPtr GetTexture(string key)
    {
        return _textures.TryGetValue(key, out int handle) ? handle : IntPtr.Zero;
    }

    public IntPtr GetTextureForPath(string path)
    {
        if (Directory.Exists(path))
        {
            return GetTexture("Folder");
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension == ".cs")
        {
            return GetTexture("Script");
        }

        if (PrefabExtensions.Contains(extension))
        {
            return GetTexture("Prefab");
        }

        if (ImageExtensions.Contains(extension))
        {
            if (_textures.TryGetValue(path, out int handle))
            {
                return handle;
            }

            int newHandle = LoadTextureFromFile(path);
            if (newHandle != 0)
            {
                _textures[path] = newHandle;
                return newHandle;
            }
        }

        return GetTexture("File");
    }

    private int LoadTextureFromFile(string path)
    {
        try
        {
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