using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering;
using OpenTK.Graphics.OpenGL4;

namespace Cherris;

public class OpenTKResourceManager : ResourceManagerBase
{
    private readonly Dictionary<string, ITexture> _textures = new();
    private readonly Dictionary<string, Skybox> _skyboxes = new();

    public override void LoadInitialAssets()
    {
        Console.WriteLine("[OpenTKResourceManager] Initial assets loaded.");
        _meshes.Add("Cube", Mesh.CreateCube());
        _meshes.Add("Plane", Mesh.CreatePlane(20f));

        // Create a default white texture
        _textures.Add("White", CreateWhiteTexture());
    }

    private static ITexture CreateWhiteTexture()
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);

        byte[] pixel = { 255, 255, 255, 255 };
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

        SetTextureParameters(TextureTarget.Texture2D);
        return new OpenTKTexture(handle);
    }

    public override ITexture GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out var texture))
        {
            return texture;
        }

        string path = AssetFinder.FindAssetPath(name);
        if (path is null)
        {
            Console.WriteLine($"[OpenTKResourceManager] Warning: Could not find texture '{name}'.");
            return _textures["White"];
        }

        var imageData = ImageLoader.LoadFromFile(path);
        if (imageData is null)
        {
            return _textures["White"];
        }

        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Srgb8Alpha8,
            imageData.Value.Width, imageData.Value.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, imageData.Value.Data);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        SetTextureParameters(TextureTarget.Texture2D);

        var newTexture = new OpenTKTexture(handle);
        _textures.Add(name, newTexture);
        return newTexture;
    }

    private static void SetTextureParameters(TextureTarget target)
    {
        if (target == TextureTarget.Texture2D)
        {
            GL.TexParameter(target, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(target, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }
        else if (target == TextureTarget.TextureCubeMap)
        {
            GL.TexParameter(target, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(target, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(target, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }
    }

    public override Skybox GetSkybox(string name)
    {
        if (_skyboxes.TryGetValue(name, out var skybox))
        {
            return skybox;
        }

        var faceImages = GenericCubemapLoader.LoadCubemapFaces(name);
        if (faceImages is null)
        {
            Console.WriteLine($"[OpenTKResourceManager] Error: Could not load faces for skybox '{name}'.");
            return null;
        }

        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, handle);

        for (int i = 0; i < faceImages.Length; i++)
        {
            var image = faceImages[i];
            GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, PixelInternalFormat.Srgb8Alpha8,
                image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
        }

        SetTextureParameters(TextureTarget.TextureCubeMap);

        var newTexture = new OpenTKTexture(handle, TextureTarget.TextureCubeMap);
        var newSkybox = new Skybox(newTexture, name);
        _skyboxes.Add(name, newSkybox);
        return newSkybox;
    }

    public override void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }
        foreach (var skybox in _skyboxes.Values)
        {
            skybox.Dispose();
        }
    }
}