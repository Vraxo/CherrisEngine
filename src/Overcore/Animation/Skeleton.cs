using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Animation;

/// <summary>
/// The bone transforms a skinned mesh is bound to, and the inverse of each bone's bind-pose world matrix — the
/// glTF <c>skin</c>, and the data a vertex needs to follow a pose.
/// </summary>
/// <remarks>
/// <para>
/// Bone indices are the order of <see cref="Bones"/>, which is the order <see cref="Overcore.Vertex.BoneIndices"/>
/// refers to. A skeleton is data only: it holds no GPU state and knows nothing about a renderer, which is what lets
/// it be built and tested without a graphics context.
/// </para>
/// <para>
/// A mesh drawn at its bind pose must produce the identity for every joint matrix. That identity is the contract
/// between this type and the vertex shader, and the reason <see cref="GetJointMatrix"/> is written out longhand
/// instead of being folded into a renderer.
/// </para>
/// </remarks>
public class Skeleton
{
    private readonly List<GameObject> _bones = [];
    private readonly List<Matrix4x4> _bindPoseInverses = [];

    /// <summary>The skeleton's name, usually the armature's, for the inspector.</summary>
    public string Name { get; set; } = "Skeleton";

    /// <summary>The bone objects, in bone-index order.</summary>
    public IReadOnlyList<GameObject> Bones => _bones;

    /// <summary>Inverse bind matrices, one per bone, in the same order as <see cref="Bones"/>.</summary>
    public IReadOnlyList<Matrix4x4> BindPoseInverses => _bindPoseInverses;

    /// <summary>The armature root, when the file named one; null is normal and means nothing to skinning.</summary>
    public GameObject? RootBone { get; set; }

    public int BoneCount => _bones.Count;

    /// <summary>Adds a bone and its inverse bind matrix. Bones are appended, so their index never changes.</summary>
    public int AddBone(GameObject bone, Matrix4x4 bindPoseInverse)
    {
        ArgumentNullException.ThrowIfNull(bone);

        _bones.Add(bone);
        _bindPoseInverses.Add(bindPoseInverse);

        return _bones.Count - 1;
    }

    /// <summary>
    /// The matrix a vertex weighted to <paramref name="boneIndex"/> is multiplied by:
    /// <c>inverseBindMatrix * boneWorld * inverse(rendererWorld)</c>.
    /// </summary>
    /// <param name="boneIndex">Index into <see cref="Bones"/>.</param>
    /// <param name="rendererWorld">The skinned object's world matrix.</param>
    /// <remarks>
    /// <para>
    /// Written in the row-vector order <see cref="Matrix4x4"/> and <see cref="Transform"/> already use
    /// (<c>local * parent</c>), not the column-vector order the glTF specification writes it in. The two are the same
    /// transform; only the multiplication order differs.
    /// </para>
    /// <para>
    /// The result is in the renderer's <em>parent</em> space, not its own, because that is the space a glTF
    /// <c>inverseBindMatrices</c> accessor is written in: the inverse of each joint's transform in the scene the file
    /// was authored in. Cancelling the renderer's world out here is what lets the vertex then take the same
    /// <c>model * view * projection</c> path as every other mesh instead of needing a second one.
    /// </para>
    /// <para>
    /// The invariant that follows is <c>jointMatrix * rendererWorld == identity</c> at bind pose: the bone sits where
    /// the bind pose put it, so the vertex lands exactly where it was modelled. A transposed matrix, a reversed
    /// multiplication order or a missing inverse all break that, which is what the tests pin.
    /// </para>
    /// </remarks>
    public Matrix4x4 GetJointMatrix(int boneIndex, Matrix4x4 rendererWorld)
    {
        if (boneIndex < 0 || boneIndex >= _bones.Count)
        {
            return Matrix4x4.Identity;
        }

        if (!Matrix4x4.Invert(rendererWorld, out Matrix4x4 rendererWorldInverse))
        {
            rendererWorldInverse = Matrix4x4.Identity;
        }

        Matrix4x4 boneWorld = _bones[boneIndex].Transform.GetModelMatrix();

        // Row-vector order: the vertex goes through the bind inverse, then the bone, then back into the renderer's
        // space. Written this way it is the identity at bind pose, which is the contract the tests pin.
        return _bindPoseInverses[boneIndex] * boneWorld * rendererWorldInverse;
    }

    /// <summary>
    /// Every joint matrix for a pose, in bone order — what a renderer uploads as the bone palette. Allocates a
    /// new array; a renderer that draws every frame keeps its own buffer and calls
    /// <see cref="FillJointMatrices(Matrix4x4, Matrix4x4[])"/> instead.
    /// </summary>
    public Matrix4x4[] GetJointMatrices(Matrix4x4 rendererWorld)
    {
        Matrix4x4[] matrices = new Matrix4x4[_bones.Count];
        FillJointMatrices(rendererWorld, matrices);

        return matrices;
    }

    /// <summary>Fills a caller-owned palette, which is how a per-frame renderer avoids allocating.</summary>
    public void FillJointMatrices(Matrix4x4 rendererWorld, Matrix4x4[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Length < _bones.Count)
        {
            throw new ArgumentException(
                $"The palette holds {destination.Length} matrices but the skeleton has {_bones.Count} bones.",
                nameof(destination));
        }

        for (int index = 0; index < _bones.Count; index++)
        {
            destination[index] = GetJointMatrix(index, rendererWorld);
        }
    }
}
