using Cherris.Components;
using Cherris.Core.Logging;
using Cherris.Rendering;
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
        _meshes.Add("Cube", Mesh.CreateCube());
        _meshes.Add("Plane", Mesh.CreatePlane(20f));
        _textures.Add("White", CreateWhiteTexture("White"));
    }

    public Mesh? GetMesh(string name)
    {
        if (_meshes.TryGetValue(name, out var mesh))
        {
            return mesh;
        }

        string[] parts = name.Split('#');
        string filePathPart = parts[0];

        // Ensure we handle virtual files properly.
        // If we are packed, Find() won't return a disk path if it's purely virtual, 
        // but ModelLoader now handles ProjectFiles.Open.
        // We use Find just to check existence or rely on ModelLoader failing gracefully.

        // Actually, let's just try to load if the extension matches.
        if (filePathPart.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
            filePathPart.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            var loadedMeshes = ModelLoader.LoadMeshesFromFile(filePathPart);
            if (!loadedMeshes.Any())
            {
                return null;
            }

            foreach (var (meshName, loadedMesh) in loadedMeshes)
            {
                _meshes[$"{filePathPart}#{meshName}"] = loadedMesh;
            }

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

        // TextureLoader uses ImageLoader which uses ProjectFiles.Open.
        // We just pass the name (path).
        var loadedTexture = TextureLoader.LoadTextureFromFile(_graphicsDevice, name);

        if (loadedTexture is not null)
        {
            _textures.Add(name, loadedTexture);
            return loadedTexture;
        }

        Logger.Warning($"[ResourceManager] Texture '{name}' not found. Using default.");
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
        return null;
    }

    public AudioClip? GetAudioClip(string name)
    {
        // Veldrid backend doesn't support audio here, 
        // but if it did, it would use AudioLoader which is now stream-ready.
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