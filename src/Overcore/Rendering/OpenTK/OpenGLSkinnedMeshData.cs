using Overcore.Animation;
using Overcore.Components;
using OpenTK.Graphics.OpenGL4;
using System.Numerics;

namespace Overcore.Rendering.OpenTK;

/// <summary>
/// A skinned mesh's GPU state: the shared vertex/index arrays, the bone texture, and the palette buffer that is
/// refilled only when the pose has actually moved.
/// </summary>
/// <remarks>
/// <para>
/// The vertex and index buffers are the ordinary ones — <see cref="OpenGLMeshRendererData"/> already declares the
/// bone attributes at locations four and five, because the vertex layout carries them whether or not a mesh has
/// bones. So this type owns only what is skinned about a mesh: the palette and its upload.
/// </para>
/// <para>
/// <see cref="UpdatePalette"/> is guarded by a comparison against the last uploaded matrices. A character standing
/// still, or a prop made of skinned parts, costs no upload; the joint matrices are still recomputed by the skeleton,
/// which is cheap arithmetic, but the texture traffic is not paid.
/// </para>
/// </remarks>
internal sealed class OpenGLSkinnedMeshData : IDisposable
{
    private readonly OpenGLMeshRendererData _geometry;
    private readonly OpenGLBoneTexture _boneTexture;
    private float[] _palette;
    private Matrix4x4[] _lastUploaded;
    private int _boneCount;
    private bool _disposed;

    /// <param name="mesh">The mesh to draw, skinned or not.</param>
    /// <param name="boneCapacity">How many bones the skeleton has, which fixes the palette's size.</param>
    /// <param name="boneTextureUnit">The unit the bone texture lives on, away from the material's.</param>
    public OpenGLSkinnedMeshData(Mesh mesh, int boneCapacity, TextureUnit boneTextureUnit)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        _geometry = new OpenGLMeshRendererData(mesh);
        _boneTexture = new OpenGLBoneTexture(boneTextureUnit);

        _boneCount = Math.Max(boneCapacity, 0);
        _palette = new float[BonePalette.TexelCount(_boneCount) * BonePalette.ComponentsPerTexel];
        _lastUploaded = [];

        // A palette that has never been uploaded still has to be a valid texture, or the first draw samples a handle
        // with no storage. Identities leave every vertex where the mesh was modelled.
        BonePalette.PackIdentity(_boneCount, _palette);
        _boneTexture.Upload(_palette, _boneCount);
    }

    public int IndexCount => _geometry.IndexCount;

    /// <summary>The bone count the palette was sized for, so a rebound skeleton can be spotted rather than resized in place.</summary>
    public int BoneCapacity => _boneCount;

    public int VaoHandle => _geometry.VaoHandle;

    public OpenGLBoneTexture BoneTexture => _boneTexture;

    /// <summary>
    /// Rebuilds the palette from the skeleton and uploads it if any joint moved. Returns true when something was
    /// uploaded, which is what the renderer reports for a frame that cost GPU traffic.
    /// </summary>
    public bool UpdatePalette(Skeleton skeleton, Matrix4x4 rendererWorld)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        if (skeleton.BoneCount != _boneCount)
        {
            // A different skeleton was bound to a mesh that already had data. Rebuilding the storage is cheaper and
            // less surprising than drawing with half a palette.
            _boneCount = skeleton.BoneCount;
            _palette = new float[BonePalette.TexelCount(_boneCount) * BonePalette.ComponentsPerTexel];
            _lastUploaded = [];

            BonePalette.PackIdentity(_boneCount, _palette);
            _boneTexture.Upload(_palette, _boneCount);

            return true;
        }

        Matrix4x4[] jointMatrices = new Matrix4x4[_boneCount];
        skeleton.FillJointMatrices(rendererWorld, jointMatrices);

        if (SameAsLastUpload(jointMatrices))
        {
            return false;
        }

        BonePalette.Pack(jointMatrices, _palette);
        _boneTexture.Upload(_palette, _boneCount);

        _lastUploaded = jointMatrices;

        return true;
    }

    private bool SameAsLastUpload(Matrix4x4[] jointMatrices)
    {
        if (_lastUploaded.Length != jointMatrices.Length)
        {
            return false;
        }

        for (int index = 0; index < jointMatrices.Length; index++)
        {
            if (!_lastUploaded[index].Equals(jointMatrices[index]))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _geometry.Dispose();
        _boneTexture.Dispose();
        _disposed = true;
    }
}
