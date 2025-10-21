using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cherris.Rendering;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace Cherris;

public class OpenTKTexture : ITexture
{
    public int Handle { get; }
    private readonly TextureTarget _target;

    public OpenTKTexture(int handle, TextureTarget target = TextureTarget.Texture2D)
    {
        Handle = handle;
        _target = target;
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(_target, Handle);
    }

    public object GetBackendHandle() => Handle;

    public void Dispose()
    {
        GL.DeleteTexture(Handle);
    }
}

public class OpenTKResourceManager : IResourceManager
{
    private readonly Dictionary<string, Mesh> _meshes = new();
    private readonly Dictionary<string, ITexture> _textures = new();
    private readonly Dictionary<string, Skybox> _skyboxes = new();
    private static readonly string[] FaceSuffixes = { "_right", "_left", "_top", "_bottom", "_front", "_back" };


    public void LoadInitialAssets()
    {
        Console.WriteLine("[OpenTKResourceManager] Initial assets loaded.");
        _meshes.Add("Cube", Mesh.CreateCube());
        _meshes.Add("Plane", Mesh.CreatePlane(20f));

        // Create a default white texture
        _textures.Add("White", CreateWhiteTexture());
    }

    private ITexture CreateWhiteTexture()
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);

        byte[] pixel = { 255, 255, 255, 255 };
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

        SetTextureParameters(TextureTarget.Texture2D);
        return new OpenTKTexture(handle);
    }

    public Mesh GetMesh(string name)
    {
        return _meshes.TryGetValue(name, out var mesh) ? mesh : null;
    }

    public ITexture GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out var texture))
        {
            return texture;
        }

        string path = AssetFinder.FindAssetPath(name);
        if (path == null)
        {
            Console.WriteLine($"[OpenTKResourceManager] Warning: Could not find texture '{name}'.");
            return _textures["White"];
        }

        try
        {
            // Flip vertically as OpenGL expects the origin at the bottom-left
            StbImage.stbi_set_flip_vertically_on_load(1);
            using var stream = File.OpenRead(path);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            int handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Srgb8Alpha8,
                image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            SetTextureParameters(TextureTarget.Texture2D);

            var newTexture = new OpenTKTexture(handle);
            _textures.Add(name, newTexture);
            return newTexture;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[OpenTKResourceManager] Error loading texture '{name}': {e.Message}");
            return _textures["White"];
        }
    }

    private void SetTextureParameters(TextureTarget target)
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

    public Skybox GetSkybox(string name)
    {
        if (_skyboxes.TryGetValue(name, out var skybox))
        {
            return skybox;
        }

        var facePaths = FaceSuffixes.Select(suffix => AssetFinder.FindAssetPath(name + suffix)).ToArray();
        if (facePaths.Any(p => p == null))
        {
            Console.WriteLine($"[OpenTKResourceManager] Error: Could not find all 6 faces for skybox '{name}'.");
            return null;
        }

        try
        {
            StbImage.stbi_set_flip_vertically_on_load(0); // Cubemaps should not be flipped

            int handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.TextureCubeMap, handle);

            for (int i = 0; i < facePaths.Length; i++)
            {
                using var stream = File.OpenRead(facePaths[i]);
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, PixelInternalFormat.Srgb8Alpha8,
                    image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            }

            SetTextureParameters(TextureTarget.TextureCubeMap);

            var newTexture = new OpenTKTexture(handle, TextureTarget.TextureCubeMap);
            var newSkybox = new Skybox(newTexture);
            _skyboxes.Add(name, newSkybox);
            return newSkybox;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[OpenTKResourceManager] Error loading skybox '{name}': {e.Message}");
            return null;
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(1); // Reset to default for other textures
        }
    }

    public void Dispose()
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