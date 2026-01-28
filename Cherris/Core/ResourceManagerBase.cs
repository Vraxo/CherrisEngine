using Cherris.Components;
using Cherris.Rendering;
using Cherris.Utils;

namespace Cherris.Core;

public abstract class ResourceManagerBase : IResourceManager
{
    protected readonly Dictionary<string, Mesh> _meshes = [];

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

    public abstract void LoadInitialAssets();
    public abstract ITexture GetTexture(string name);
    public abstract Skybox GetSkybox(string name);
    public abstract AudioClip GetAudioClip(string name);
    public abstract void Dispose();
}