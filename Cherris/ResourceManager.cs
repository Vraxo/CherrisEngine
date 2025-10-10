using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StbImageSharp;
using Veldrid;

namespace Cherris;

public class ResourceManager
{
    private readonly Dictionary<string, Mesh> _meshes = new();
    private readonly Dictionary<string, Texture> _textures = new();
    private readonly Dictionary<string, Skybox> _skyboxes = new();
    private readonly GraphicsDevice _graphicsDevice;
    private const string AssetRootPath = "Assets";

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
        // Create a default white texture for untextured objects or if a texture fails to load.
        var white = CreateWhiteTexture("White");
        _textures.Add("White", white);
    }

    public Mesh GetMesh(string name)
    {
        return _meshes.TryGetValue(name, out var mesh) ? mesh : null;
    }

    public Texture GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out var texture))
        {
            return texture;
        }

        // Texture not in cache, try to load it from file by searching the asset directory.
        string filePath = FindTextureFile(name);

        if (filePath != null)
        {
            var loadedTexture = TextureLoader.LoadTextureFromFile(_graphicsDevice, filePath);
            if (loadedTexture != null)
            {
                _textures.Add(name, loadedTexture);
                return loadedTexture;
            }
        }

        // Fallback to the default white texture if loading fails or file doesn't exist.
        Console.WriteLine($"[ResourceManager] Warning: Could not find or load texture '{name}'. Using default white texture.");
        return _textures["White"];
    }

    public Skybox GetSkybox(string name)
    {
        if (_skyboxes.TryGetValue(name, out var skybox))
        {
            return skybox;
        }

        var loadedSkybox = LoadSkyboxFromFile(name);
        if (loadedSkybox != null)
        {
            _skyboxes.Add(name, loadedSkybox);
            return loadedSkybox;
        }

        Console.WriteLine($"[ResourceManager] Warning: Could not find or load skybox '{name}'.");
        return null;
    }

    private Skybox LoadSkyboxFromFile(string name)
    {
        // The order corresponds to the cubemap array layers: +X, -X, +Y, -Y, +Z, -Z
        // This mapping is now consistent with most cubemap authoring tools.
        string[] faceSuffixes = { "_right", "_left", "_top", "_bottom", "_front", "_back" };
        string[] facePaths = new string[6];

        for (int i = 0; i < 6; i++)
        {
            var path = FindTextureFile(name + faceSuffixes[i]);
            if (path == null)
            {
                Console.WriteLine($"[ResourceManager] Could not find face '{name}{faceSuffixes[i]}' for skybox.");
                return null;
            }
            facePaths[i] = path;
        }

        ImageResult[] faceImages = new ImageResult[6];
        try
        {
            StbImage.stbi_set_flip_vertically_on_load(0);
            for (int i = 0; i < 6; i++)
            {
                using (var stream = File.OpenRead(facePaths[i]))
                {
                    faceImages[i] = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                }
            }
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(1);
        }


        var firstImage = faceImages[0];
        if (firstImage.Width != firstImage.Height)
        {
            Console.WriteLine($"[ResourceManager] Error: Skybox face textures must be square. Texture '{facePaths[0]}' has dimensions {firstImage.Width}x{firstImage.Height}.");
            return null;
        }
        if (faceImages.Any(img => img.Width != firstImage.Width || img.Height != firstImage.Height))
        {
            Console.WriteLine("[ResourceManager] Error: All faces of a skybox must have the same dimensions.");
            return null;
        }

        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        Veldrid.Texture cubemap = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)firstImage.Width, (uint)firstImage.Height, 1, 6, // arrayLayers must be 6 for a cubemap
            PixelFormat.R8_G8_B8_A8_UNorm_SRgb, // Use sRGB for correct sampling
            TextureUsage.Cubemap | TextureUsage.Sampled));

        for (uint i = 0; i < 6; i++)
        {
            var img = faceImages[i];
            _graphicsDevice.UpdateTexture(cubemap, img.Data, 0, 0, 0, (uint)img.Width, (uint)img.Height, 1, 0, i);
        }

        TextureView textureView = factory.CreateTextureView(new TextureViewDescription(cubemap));
        var texture = new Texture(cubemap, textureView);
        return new Skybox(texture);
    }

    private string FindTextureFile(string name)
    {
        if (!Directory.Exists(AssetRootPath))
        {
            return null;
        }

        // We'll check for a few common extensions.
        string[] extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
        foreach (var ext in extensions)
        {
            // Search for "name.ext" in the root asset directory and all subdirectories.
            try
            {
                var files = Directory.GetFiles(AssetRootPath, name + ext, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    if (files.Length > 1)
                    {
                        Console.WriteLine($"[ResourceManager] Warning: Found multiple files for texture '{name}'. Using '{files[0]}'.");
                    }
                    return files[0]; // Return the first match.
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ResourceManager] Error while searching for textures: {e.Message}");
                return null;
            }
        }
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