using Overcore.Animation;

namespace Overcore.Components;

/// <summary>
/// Marks an object's <see cref="MeshRenderer"/> as deformed by a <see cref="Skeleton"/> — the data an importer
/// produces for a glTF node that names a skin.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a component of its own rather than a flag on <see cref="MeshRenderer"/>: the inspector, the
/// scene file and the renderer all ask "is this mesh skinned, and by what?" and a separate component answers that
/// without every mesh paying for a field it never sets.
/// </para>
/// <para>
/// Increment one of the animation layer: the link from mesh to bones exists and is saved, and nothing deforms a
/// vertex yet. The GPU-side palette and the skinning shader are the next increment, and they read this component to
/// find the skeleton they need. Until then a skinned mesh draws at its bind pose, which is exactly what the
/// importer left its bones in.
/// </para>
/// </remarks>
public class SkinnedMesh : Component
{
    public SkinnedMesh()
    {
    }

    public SkinnedMesh(Skeleton skeleton)
    {
        Skeleton = skeleton;
    }

    /// <summary>The bones this mesh follows, or null when the import found no skin for it.</summary>
    public Skeleton? Skeleton { get; set; }
}
