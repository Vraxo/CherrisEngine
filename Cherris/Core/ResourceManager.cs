using Cherris.Components;
using Cherris.Rendering;
using Cherris.Utils;
using Veldrid;

namespace Cherris.Core;

public class ResourceManager : IResourceManager
{
    private readonly Dictionary<string, Mesh> _meshes = [];
    private readonly Dictionary<string, ITexture> _textures = [];
    private readonly Dictionary<string, Skybox> _skyboxes = [];
    private readonly GraphicsDevice _graphicsDevice;

    public ResourceManager(GraphicsDevice gd)
    {
        _graphicsDevice = gd;
    }

    public void LoadInitialAssets()
    {
        var cubeMesh = Mesh.CreateCube();
        _meshes.Add("Cube", cubeMesh);

        var planeMesh = Mesh.CreatePlane(20f);
        _meshes.Add("Plane", planeMesh);

        var white = CreateWhiteTexture("White");
        _textures.Add("White", white);
    }

    public Mesh? GetMesh(string name)
    {
        if (_meshes.TryGetValue(name, out var mesh))
        {
            return mesh;
        }

        string[] parts = name.Split('#');
        string filePathPart = parts[0];

        if (filePathPart.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
            filePathPart.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            if (_meshes.Keys.Any(k => k.StartsWith(filePathPart + "#")))
            {
                Console.WriteLine($"[ResourceManager] Mesh '{name}' not found in already loaded file '{filePathPart}'.");
                return null;
            }

            string? fullPath = ProjectFiles.Find(filePathPart);
            if (fullPath is null)
            {
                Console.WriteLine($"[ResourceManager] Could not find model file for '{filePathPart}'.");
                return null;
            }

            var loadedMeshes = ModelLoader.LoadMeshesFromFile(fullPath);
            if (!loadedMeshes.Any())
            {
                Console.WriteLine($"[ResourceManager] No meshes found in model file '{fullPath}'.");
                return null;
            }

            foreach (var (meshName, loadedMesh) in loadedMeshes)
            {
                string cacheKey = $"{filePathPart}#{meshName}";
                _meshes[cacheKey] = loadedMesh;
            }
            Console.WriteLine($"[ResourceManager] Loaded and cached {loadedMeshes.Count} mesh(es) from '{filePathPart}'.");

            if (_meshes.TryGetValue(name, out var finalMesh))
            {
                return finalMesh;
            }

            if (parts.Length == 1)
            {
                return loadedMeshes.Values.First();
            }
        }

        return null;
    }


    public ITexture GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out var texture))
        {
            return texture;
        }

        string? filePath = ProjectFiles.Find(name);

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

    public Skybox? GetSkybox(string name)
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

    public AudioClip? GetAudioClip(string name)
    {
        Console.WriteLine("[ResourceManager] Warning: Veldrid backend does not support audio. GetAudioClip will return null.");
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