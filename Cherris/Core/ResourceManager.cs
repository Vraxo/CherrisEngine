using System;
using System.Collections.Generic;
using Cherris.Rendering;
using Veldrid;

namespace Cherris;

public class ResourceManager : IResourceManager
{
    private readonly Dictionary<string, Mesh> _meshes = new();
    private readonly Dictionary<string, ITexture> _textures = new();
    private readonly Dictionary<string, Skybox> _skyboxes = new();
    private readonly GraphicsDevice _graphicsDevice;

    public ResourceManager(GraphicsDevice gd)
    {
        _graphicsDevice = gd;
    }

    public void LoadInitialAssets()
    {
        // Meshes
        var cubeMesh = Mesh.CreateCube();
        _meshes.Add("Cube", cubeMesh);

        var planeMesh = Mesh.CreatePlane(20f);
        _meshes.Add("Plane", planeMesh);

        // Textures
        var white = CreateWhiteTexture("White");
        _textures.Add("White", white);
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

        string? filePath = AssetFinder.FindAssetPath(name);

        if (filePath is not null)
        {
            var loadedTexture = TextureLoader.LoadTextureFromFile(_graphicsDevice, filePath);
            if (loadedTexture is not null)
            {
                _textures.Add(name, loadedTexture);
                return loadedTexture;
            }
        }

        Console.WriteLine($"[ResourceManager] Warning: Could not find or load texture '{name}'. Using default white texture.");
        return _textures["White"];
    }

    public Skybox GetSkybox(string name)
    {
        if (_skyboxes.TryGetValue(name, out var skybox))
        {
            return skybox;
        }

        var loadedSkybox = SkyboxLoader.LoadSkybox(_graphicsDevice, name);
        if (loadedSkybox is not null)
        {
            _skyboxes.Add(name, loadedSkybox);
            return loadedSkybox;
        }

        Console.WriteLine($"[ResourceManager] Warning: Could not find or load skybox '{name}'.");
        return null;
    }

    private Texture CreateWhiteTexture(string name)
    {
        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        Veldrid.Texture veldridTexture = factory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        byte[] pixelData = { 255, 255, 255, 255 };
        _graphicsDevice.UpdateTexture(veldridTexture, pixelData, 0, 0, 0, 1, 1, 1, 0, 0);

        TextureView textureView = factory.CreateTextureView(veldridTexture);
        return new Texture(veldridTexture, textureView);
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