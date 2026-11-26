using System.Numerics;

namespace Overcore.Animation;

/// <summary>
/// A skeleton's joint matrices packed the way the skinning shader samples them: a bone per row of texels, three
/// texels wide, ready to hand to a <c>RGBA32F</c> 2D texture.
/// </summary>
/// <remarks>
/// <para>
/// A palette goes up as a <em>texture</em> rather than a uniform array because a uniform block's size is
/// driver-limited. A palette that silently truncates at 64 bones is a bug that only appears on other people's
/// hardware, and it appears as a character whose arms stop moving rather than as an error — the worst kind of
/// failure to ship. A texture's size is bounded instead by <c>GL_MAX_TEXTURE_SIZE</c>, which is thousands of bones.
/// </para>
/// <para>
/// The layout is three texels per bone, each an <c>RGBA32F</c>: four floats. That is twelve floats a bone, which is a
/// 4×4 matrix minus its fourth column — the column that is always <c>(0, 0, 0, 1)</c> for an affine transform.
/// </para>
/// <para>
/// A texel holds a <em>column</em> of the matrix, not a row. In the row-vector order this engine uses
/// (<c>[x y z 1] * M</c>) each output component is a dot of the position against a column, and the translation sits
/// in the fourth row <c>(M41, M42, M43)</c> — which is the fourth component of the first three columns and therefore
/// survives. Packing rows instead would drop the translation and leave a character that rotates about the origin.
/// </para>
/// <para>
/// This type holds no GPU state. It packs numbers into a caller's array, which is what makes it testable without a
/// context — the arithmetic a shader depends on is exactly the arithmetic CI can check, and only the upload itself
/// is left to the eye.
/// </para>
/// </remarks>
public static class BonePalette
{
    /// <summary>Floats a bone occupies: three <c>vec4</c> texels, the first three columns of its matrix.</summary>
    public const int FloatsPerBone = 12;

    /// <summary>Texels a bone occupies, one per column of its matrix.</summary>
    public const int TexelsPerBone = 3;

    /// <summary>Components a texel carries, since an <c>RGBA32F</c> texel is four floats.</summary>
    public const int ComponentsPerTexel = 4;

    /// <summary>Texels a palette of <paramref name="boneCount"/> bones occupies in its texture.</summary>
    public static int TexelCount(int boneCount) => Math.Max(boneCount, 0) * TexelsPerBone;

    /// <summary>
    /// The smallest texture that holds <paramref name="boneCount"/> bones as a single row, never zero. A zero-width
    /// texture is a GL error, and a skeleton with no bones still needs a valid handle to bind.
    /// </summary>
    public static int TextureWidth(int boneCount) => Math.Max(TexelCount(boneCount), 1);

    /// <summary>
    /// Packs a palette into <paramref name="destination"/>, which must hold
    /// <see cref="TexelCount"/> × <see cref="ComponentsPerTexel"/> floats. Returns the number of floats written.
    /// </summary>
    /// <param name="jointMatrices">One matrix per bone, in bone order — what <see cref="Skeleton.GetJointMatrices"/> returns.</param>
    /// <param name="destination">The array the texels are written into.</param>
    /// <remarks>
    /// Each texel is a column of the matrix. In the row-vector order the engine uses (<c>local * parent</c>) the
    /// translation sits in the fourth row <c>(M41, M42, M43)</c>, which is exactly the fourth component of the first
    /// three columns — so a column pack carries it and a row pack would not. The shader reconstructs the fourth
    /// column as <c>(0, 0, 0, 1)</c>, because an affine transform always has it.
    /// </remarks>
    public static int Pack(IReadOnlyList<Matrix4x4> jointMatrices, float[] destination)
    {
        ArgumentNullException.ThrowIfNull(jointMatrices);
        ArgumentNullException.ThrowIfNull(destination);

        int needed = TexelCount(jointMatrices.Count) * ComponentsPerTexel;

        if (destination.Length < needed)
        {
            throw new ArgumentException(
                $"The palette buffer holds {destination.Length} floats but {jointMatrices.Count} bones need {needed}.",
                nameof(destination));
        }

        int written = 0;

        for (int bone = 0; bone < jointMatrices.Count; bone++)
        {
            Matrix4x4 matrix = jointMatrices[bone];

            // A column becomes a texel. In row-vector order the translation is the fourth row, so it survives here
            // as the fourth component of each column; packing rows would drop it.
            Write(destination, ref written, matrix.M11, matrix.M21, matrix.M31, matrix.M41);
            Write(destination, ref written, matrix.M12, matrix.M22, matrix.M32, matrix.M42);
            Write(destination, ref written, matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        }

        return written;
    }

    /// <summary>
    /// A palette that leaves every vertex where it is: the identity for each bone. What a renderer uploads for a
    /// skeleton it cannot bind, so a misconfigured character draws at its bind pose instead of collapsing. Returns the
    /// number of floats written, like <see cref="Pack"/>.
    /// </summary>
    public static int PackIdentity(int boneCount, float[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        Matrix4x4 identity = Matrix4x4.Identity;

        int needed = TexelCount(boneCount) * ComponentsPerTexel;

        if (destination.Length < needed)
        {
            throw new ArgumentException(
                $"The palette buffer holds {destination.Length} floats but {boneCount} bones need {needed}.",
                nameof(destination));
        }

        int written = 0;

        for (int bone = 0; bone < boneCount; bone++)
        {
            Write(destination, ref written, identity.M11, identity.M21, identity.M31, identity.M41);
            Write(destination, ref written, identity.M12, identity.M22, identity.M32, identity.M42);
            Write(destination, ref written, identity.M13, identity.M23, identity.M33, identity.M43);
        }

        return written;
    }

    private static void Write(float[] destination, ref int offset, float x, float y, float z, float w)
    {
        destination[offset] = x;
        destination[offset + 1] = y;
        destination[offset + 2] = z;
        destination[offset + 3] = w;

        offset += ComponentsPerTexel;
    }
}
