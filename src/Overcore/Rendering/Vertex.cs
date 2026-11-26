using System.Numerics;
using Veldrid;

namespace Overcore;

public struct Vertex
{
    /// <summary>How many bones may influence one vertex. Four is Unity's default skin quality.</summary>
    public const int MaxBoneInfluences = 4;

    public Vector3 Position;
    public Vector3 Normal;
    public RgbaFloat Color;
    public Vector2 TexCoord;

    /// <summary>
    /// The four bones that move this vertex, as indices into the <see cref="Overcore.Animation.Skeleton"/> it is
    /// skinned to. Unused slots are zero, and a vertex that is not skinned has all four zero and no weight.
    /// </summary>
    public Vector4 BoneIndices;

    /// <summary>The weight of each entry in <see cref="BoneIndices"/>; normalized so the four sum to one.</summary>
    public Vector4 BoneWeights;

    public const uint SizeInBytes = (12 + 12 + 16 + 8) + (16 + 16); // 80

    public Vertex(Vector3 position, Vector3 normal, RgbaFloat color, Vector2 texCoord)
        : this(position, normal, color, texCoord, Vector4.Zero, Vector4.Zero)
    {
    }

    public Vertex(Vector3 position, Vector3 normal, RgbaFloat color, Vector2 texCoord, Vector4 boneIndices, Vector4 boneWeights)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TexCoord = texCoord;
        BoneIndices = boneIndices;
        BoneWeights = boneWeights;
    }

    /// <summary>True when at least one bone moves this vertex.</summary>
    public bool IsSkinned =>
        BoneWeights.X > 0f || BoneWeights.Y > 0f || BoneWeights.Z > 0f || BoneWeights.W > 0f;
}
