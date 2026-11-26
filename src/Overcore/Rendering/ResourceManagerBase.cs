using Overcore.Components;
using Overcore.Rendering;

namespace Overcore.Core;

public abstract class ResourceManagerBase : IResourceManager
{
    protected readonly Dictionary<string, Mesh> _meshes = [];

    /// <summary>
    /// A mesh by the name a scene uses: a built-in (<c>"Cube"</c>), or a model's mesh by its qualified key
    /// (<c>"Assets/Models/Hero.gltf#Head"</c>) or a bare name the import owns.
    /// </summary>
    /// <remarks>
    /// An import registers its meshes under both keys, so a file's single mesh is still reachable by its name. What
    /// changed is that a name is no longer a wildcard into whichever file was opened last: the first import to claim a
    /// bare name keeps it, which is what makes two models with a <c>"Body"</c> predictable rather than order-dependent.
    /// </remarks>
    public Mesh? GetMesh(string name)
    {
        if (_meshes.TryGetValue(name, out Mesh? mesh))
        {
            return mesh;
        }

        Mesh? imported = ModelAssets.FindMesh(name);

        if (imported is null)
        {
            return null;
        }

        _meshes[name] = imported;

        return imported;
    }

    public abstract void LoadInitialAssets();
    public abstract ITexture GetTexture(string name);
    public abstract Skybox GetSkybox(string name);
    public abstract AudioClip GetAudioClip(string name);
    public abstract void Dispose();
}