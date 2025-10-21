using System;
using System.Collections.Generic;
using System.IO;
using Cherris.Rendering;
using OpenTK.Graphics.OpenGL;
using StbImageSharp;

namespace Cherris;

public class OpenTKTexture : ITexture
{
    public int Handle { get; }

    public OpenTKTexture(int handle)
    {
        Handle = handle;
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2d, Handle);
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
        GL.BindTexture(TextureTarget.Texture2d, handle);

        byte[] pixel = { 255, 255, 255, 255 };
        GL.TexImage2D(TextureTarget.Texture2d, 0, InternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

        SetTextureParameters();
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
            GL.BindTexture(TextureTarget.Texture2d, handle);

            GL.TexImage2D(TextureTarget.Texture2d, 0, InternalFormat.Rgba,
                image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);

            GL.GenerateMipmap(TextureTarget.Texture2d);
            SetTextureParameters();

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

    private void SetTextureParameters()
    {
        GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameteri(TextureTarget.Texture2d, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
    }

    public Skybox GetSkybox(string name)
    {
        Console.WriteLine($"[OpenTKResourceManager] Stub: GetSkybox '{name}' not implemented yet.");
        return null;
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }
    }
}