using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering;

namespace Cherris;

public abstract class ResourceManagerBase : IResourceManager
{
    protected readonly Dictionary<string, Mesh> _meshes = new();

    public Mesh GetMesh(string name)
    {
        // Case 1: Mesh is already cached (primitive or from a previously loaded model).
        if (_meshes.TryGetValue(name, out var mesh))
        {
            return mesh;
        }

        // Case 2: Mesh name looks like a model file that needs to be loaded.
        string[] parts = name.Split('#');
        string filePathPart = parts[0];

        if (filePathPart.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
            filePathPart.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            // Check if we've already processed this file by looking for any of its meshes in the cache.
            // If we find one, it means all meshes from that file are already cached, but the specific
            // one requested (`name`) was not found. So we can return null early.
            if (_meshes.Keys.Any(k => k.StartsWith(filePathPart + "#")))
            {
                Console.WriteLine($"[ResourceManager] Mesh '{name}' not found in already loaded file '{filePathPart}'.");
                return null;
            }

            // It's a new model file, let's load it.
            string? fullPath = AssetFinder.FindAssetPath(filePathPart);
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

            // Cache all meshes from the file.
            foreach (var (meshName, loadedMesh) in loadedMeshes)
            {
                string cacheKey = $"{filePathPart}#{meshName}";
                _meshes[cacheKey] = loadedMesh;
            }
            Console.WriteLine($"[ResourceManager] Loaded and cached {loadedMeshes.Count} mesh(es) from '{filePathPart}'.");


            // Now that the file is loaded and meshes are cached, try to retrieve the requested mesh again.
            if (_meshes.TryGetValue(name, out var finalMesh))
            {
                return finalMesh;
            }

            // If the user didn't specify a mesh name (e.g., "model.gltf"), return the first one.
            if (parts.Length == 1)
            {
                return loadedMeshes.Values.First();
            }
        }

        // If it's not cached and not a model file path, it doesn't exist.
        return null;
    }

    public abstract void LoadInitialAssets();
    public abstract ITexture GetTexture(string name);
    public abstract Skybox GetSkybox(string name);
    public abstract AudioClip GetAudioClip(string name);
    public abstract void Dispose();
}