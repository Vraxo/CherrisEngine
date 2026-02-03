using Cherris.Components;
using Cherris.Core.Logging;
using Cherris.Rendering;
using StbImageSharp;
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

        ImageResult[]? images = SkyboxLoader.LoadSkyboxImages(name);
        if (images is null)
        {
            return null;
        }

        Texture cubemapTexture = CreateCubemapTexture(images);
        var newSkybox = new Skybox(cubemapTexture, name);

        _skyboxes.Add(name, newSkybox);
        return newSkybox;
    }

    private Texture CreateCubemapTexture(ImageResult[] images)
    {
        ImageResult firstImage = images[0];
        ResourceFactory factory = _graphicsDevice.ResourceFactory;

        TextureDescription textureDescription = TextureDescription.Texture2D(
            (uint)firstImage.Width, (uint)firstImage.Height, 1, (uint)images.Length,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Cubemap | TextureUsage.Sampled);

        Veldrid.Texture cubemap = factory.CreateTexture(textureDescription);

        for (uint i = 0; i < images.Length; i++)
        {
            _graphicsDevice.UpdateTexture(cubemap, images[i].Data, 0, 0, 0, (uint)images[i].Width, (uint)images[i].Height, 1, 0, i);
        }

        TextureView textureView = factory.CreateTextureView(new TextureViewDescription(cubemap));
        return new Texture(cubemap, textureView);
    }

    public AudioClip? GetAudioClip(string name)
    {
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