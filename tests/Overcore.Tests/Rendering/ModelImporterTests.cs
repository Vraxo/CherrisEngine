using AwesomeAssertions;
using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering;
using SharpGLTF.Schema2;
using System.Numerics;
using System.Text;
using Veldrid;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// The importer, exercised on a GLB written by hand into a byte array: no asset on disk, no graphics context, no
/// scene. The fixture is a two-node hierarchy whose leaf carries a triangle, four bones per vertex, and a
/// three-key translation channel — enough to pin the hierarchy, the vertex attributes, the winding and the clip
/// addressing all at once.
/// </summary>
public class ModelImporterTests
{
    private static readonly Vector3[] Positions =
    [
        new(0f, 0f, 0f),
        new(1f, 0f, 0f),
        new(0f, 1f, 0f)
    ];

    /// <summary>Four influences per vertex, deliberately not normalized, so normalizing is observable.</summary>
    private static readonly Vector4[] Joints =
    [
        new(0f, 1f, 2f, 3f),
        new(0f, 1f, 2f, 3f),
        new(0f, 1f, 2f, 3f)
    ];

    private static readonly Vector4[] Weights =
    [
        new(1f, 1f, 1f, 1f),
        new(0f, 2f, 2f, 0f),
        new(0f, 0f, 0f, 0f)
    ];

    private static readonly float[] Times = [0f, 0.5f, 1f];

    private static readonly Vector3[] Translations =
    [
        Vector3.Zero,
        Vector3.UnitY,
        new(0f, 2f, 0f)
    ];

    [Fact]
    public void Import_KeepsTheNodeHierarchy_InsteadOfFlatteningIt()
    {
        ImportedModel imported = ModelImporter.Import(ReadFixture(), "fixture.glb")!;

        // One root, and the mesh node is its child: the graph survived the import.
        imported.Roots.Should().ContainSingle();
        imported.Roots[0].Name.Should().Be("Root");

        imported.Find("Root/Body").Should().NotBeNull();

        ImportedObject body = imported.Find("Root/Body")!.Value;

        body.GameObject.Transform.Parent.Should().BeSameAs(imported.Roots[0].Transform);

        // The node's own local translation came through the file.
        body.GameObject.Transform.Position.Should().Be(new Vector3(0f, 1f, 0f));
    }

    [Fact]
    public void Import_PutsTheMeshAndItsSkinOnTheNodeThatCarriesThem()
    {
        ImportedModel imported = ModelImporter.Import(ReadFixture(), "fixture.glb")!;

        imported.Meshes.Should().ContainKey("Tri");
        imported.Meshes["Tri"].Vertices.Should().HaveCount(3);

        ImportedObject body = imported.Find("Root/Body")!.Value;

        MeshRenderer renderer = body.GameObject.GetComponent<MeshRenderer>()!;

        renderer.MeshName.Should().Be("Tri");
        renderer.Mesh.Vertices.Should().HaveCount(3);

        // A skinned mesh also records which skeleton it follows, without deforming anything yet.
        SkinnedMesh skinned = body.GameObject.GetComponent<SkinnedMesh>()!;

        skinned.Should().NotBeNull();
        skinned.Skeleton.Should().NotBeNull();
        skinned.Skeleton!.Bones.Should().HaveCount(2);
    }

    [Fact]
    public void ImportMesh_ReversesWinding_ToTheEnginesClockwiseFrontFaces()
    {
        ImportedModel imported = ModelImporter.Import(ReadFixture(), "fixture.glb")!;

        Components.Mesh mesh = imported.Meshes["Tri"];

        // The file's triangle is 0,1,2 counter-clockwise; the engine's pipelines wind clockwise, so the last two swap.
        mesh.Indices.Should().Equal([0u, 2u, 1u]);
    }

    [Fact]
    public void ImportMesh_RenormalizesBoneWeights_AndPinsAZeroWeightVertexToBoneZero()
    {
        ImportedModel imported = ModelImporter.Import(ReadFixture(), "fixture.glb")!;

        Vertex[] vertices = imported.Meshes["Tri"].Vertices;

        // Vertex 0: four equal weights.
        Sum(vertices[0].BoneWeights).Should().BeApproximately(1f, 1e-5f);
        vertices[0].BoneWeights.X.Should().BeApproximately(0.25f, 1e-5f);
        vertices[0].BoneIndices.Should().Be(new Vector4(0f, 1f, 2f, 3f));

        // Vertex 1: two weights of 2, which normalize to 0.5 each rather than staying at 2.
        Sum(vertices[1].BoneWeights).Should().BeApproximately(1f, 1e-5f);
        vertices[1].BoneWeights.X.Should().BeApproximately(0.5f, 1e-5f);

        // Vertex 2: no weight at all. It is pinned to bone 0 instead of collapsing to the origin.
        vertices[2].BoneWeights.Should().Be(new Vector4(1f, 0f, 0f, 0f));
        vertices[2].BoneIndices.Should().Be(Vector4.Zero);
    }

    [Fact]
    public void Import_AddressesClipCurvesByNodePath()
    {
        ImportedModel imported = ModelImporter.Import(ReadFixture(), "fixture.glb")!;

        imported.Clips.Should().ContainKey("Wave");

        AnimationClip clip = imported.Clips["Wave"];

        clip.Curves.Should().NotBeEmpty();
        clip.Curves.Should().OnlyContain(curve => curve.BonePath == "Root/Body");

        AnimationCurve height = clip.Curves.Single(curve =>
            curve.Property == AnimationProperty.LocalPosition && curve.Axis == AnimationAxis.Y);

        height.Keys.Should().HaveCount(3);
        height.Evaluate(0f, AnimationInterpolation.Linear).Should().BeApproximately(0f, 1e-5f);
        height.Evaluate(0.5f, AnimationInterpolation.Linear).Should().BeApproximately(1f, 1e-5f);
        height.Evaluate(1f, AnimationInterpolation.Linear).Should().BeApproximately(2f, 1e-5f);

        // A quarter of the way into the second segment: halfway between 1 and 2.
        height.Evaluate(0.75f, AnimationInterpolation.Linear).Should().BeApproximately(1.5f, 1e-5f);

        clip.Length.Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void NormalizeInfluences_DropsSlotsThatAreNotPositive()
    {
        (Vector4 _, Vector4 weights) = ModelImporter.NormalizeInfluences(
            new Vector4(0.5f, 0f, -0.25f, 0.5f),
            [new Vector4(0f, 1f, 2f, 3f)],
            0);

        weights.Should().Be(new Vector4(0.5f, 0.5f, 0f, 0f));
    }

    [Fact]
    public void Mesh_UsesThirtyTwoBitIndices_AndStillFitsSmallMeshesInSixteen()
    {
        Components.Mesh small = Components.Mesh.CreateCube();

        small.Indices.Should().BeOfType<uint[]>();
        small.UsesSixteenBitIndices.Should().BeTrue();

        // 70,000 vertices: past the 16-bit ceiling an index array used to wrap at.
        Vertex[] many = Enumerable
            .Range(0, 70_000)
            .Select(_ => new Vertex(Vector3.Zero, Vector3.UnitY, RgbaFloat.White, Vector2.Zero))
            .ToArray();

        Components.Mesh large = new(many, [0u, 1u, 2u]);

        large.UsesSixteenBitIndices.Should().BeFalse();
    }

    [Fact]
    public void Vertex_CarriesBoneData_AndKnowsWhetherItIsSkinned()
    {
        Vertex plain = new(Vector3.Zero, Vector3.UnitY, RgbaFloat.White, Vector2.Zero);

        plain.IsSkinned.Should().BeFalse();

        Vertex skinned = new(
            Vector3.Zero,
            Vector3.UnitY,
            RgbaFloat.White,
            Vector2.Zero,
            new Vector4(0f, 1f, 2f, 0f),
            new Vector4(0.5f, 0.5f, 0f, 0f));

        skinned.IsSkinned.Should().BeTrue();
        skinned.BoneIndices.Should().Be(new Vector4(0f, 1f, 2f, 0f));
    }

    [Fact]
    public void ModelAssets_QualifiesNamesByFile_AndKeepsTheFirstBareName()
    {
        ModelAssets.Clear();

        ImportedModel first = new() { SourcePath = "Hero.glb" };
        first.Meshes["Head"] = Components.Mesh.CreateCube();

        ImportedModel second = new() { SourcePath = "Enemy.glb" };
        second.Meshes["Head"] = Components.Mesh.CreatePlane(1f);

        ModelAssets.Adopt("Hero.glb", first);
        ModelAssets.Adopt("Enemy.glb", second);

        // Both are reachable by their qualified keys.
        ModelAssets.FindMesh("Hero.glb#Head").Should().BeSameAs(first.Meshes["Head"]);
        ModelAssets.FindMesh("Enemy.glb#Head").Should().BeSameAs(second.Meshes["Head"]);

        // The bare name belongs to whoever imported first, and does not silently switch.
        ModelAssets.FindMesh("Head").Should().BeSameAs(first.Meshes["Head"]);

        ModelAssets.Clear();
    }

    private static float Sum(Vector4 value) => value.X + value.Y + value.Z + value.W;

    /// <summary>Reads a hand-written GLB: Root -> Body, a triangle with four influences and a translation channel.</summary>
    private static ModelRoot ReadFixture() => ModelRoot.ParseGLB(new ArraySegment<byte>(BuildGlb()));

    /// <summary>
    /// Writes a valid binary glTF file: a JSON chunk describing two nodes, a mesh with one triangle, a two-joint
    /// skin, and one animation; then a binary chunk holding exactly the bytes those accessors point at.
    /// </summary>
    private static byte[] BuildGlb()
    {
        // Offsets are laid out in the same order the JSON below refers to them.
        int positionOffset = 0;
        int positionLength = Positions.Length * 12;

        int jointsOffset = positionOffset + positionLength;
        int jointsLength = Joints.Length * 16;

        int weightsOffset = jointsOffset + jointsLength;
        int weightsLength = Weights.Length * 16;

        int indicesOffset = weightsOffset + weightsLength;
        int indicesLength = 3 * 4;

        int timesOffset = indicesOffset + indicesLength;
        int timesLength = Times.Length * 4;

        int translationsOffset = timesOffset + timesLength;
        int translationsLength = Translations.Length * 12;

        int bindOffset = translationsOffset + translationsLength;
        int bindLength = 2 * 64;

        int binaryLength = bindOffset + bindLength;

        byte[] binary = new byte[binaryLength];

        WriteVector3Array(binary, positionOffset, Positions);
        WriteVector4Array(binary, jointsOffset, Joints);
        WriteVector4Array(binary, weightsOffset, Weights);

        WriteUInt32(binary, indicesOffset, 0);
        WriteUInt32(binary, indicesOffset + 4, 1);
        WriteUInt32(binary, indicesOffset + 8, 2);

        WriteFloatArray(binary, timesOffset, Times);
        WriteVector3Array(binary, translationsOffset, Translations);

        // Two identity inverse bind matrices: the bones are at the origin in bind pose.
        WriteIdentityMatrix(binary, bindOffset);
        WriteIdentityMatrix(binary, bindOffset + 64);

        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [0] } ],
          "nodes": [
            { "name": "Root", "children": [1] },
            { "name": "Body", "translation": [0, 1, 0], "mesh": 0, "skin": 0 }
          ],
          "meshes": [
            {
              "name": "Tri",
              "primitives": [
                {
                  "attributes": { "POSITION": 0, "JOINTS_0": 1, "WEIGHTS_0": 2 },
                  "indices": 3
                }
              ]
            }
          ],
          "skins": [
            { "name": "Rig", "joints": [0, 1], "inverseBindMatrices": 6 }
          ],
          "animations": [
            {
              "name": "Wave",
              "samplers": [ { "input": 4, "output": 5, "interpolation": "LINEAR" } ],
              "channels": [ { "sampler": 0, "target": { "node": 1, "path": "translation" } } ]
            }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
            { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC4" },
            { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC4" },
            { "bufferView": 3, "componentType": 5125, "count": 3, "type": "SCALAR" },
            { "bufferView": 4, "componentType": 5126, "count": 3, "type": "SCALAR", "min": [0], "max": [1] },
            { "bufferView": 5, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 6, "componentType": 5126, "count": 2, "type": "MAT4" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": {{positionOffset}}, "byteLength": {{positionLength}} },
            { "buffer": 0, "byteOffset": {{jointsOffset}}, "byteLength": {{jointsLength}} },
            { "buffer": 0, "byteOffset": {{weightsOffset}}, "byteLength": {{weightsLength}} },
            { "buffer": 0, "byteOffset": {{indicesOffset}}, "byteLength": {{indicesLength}} },
            { "buffer": 0, "byteOffset": {{timesOffset}}, "byteLength": {{timesLength}} },
            { "buffer": 0, "byteOffset": {{translationsOffset}}, "byteLength": {{translationsLength}} },
            { "buffer": 0, "byteOffset": {{bindOffset}}, "byteLength": {{bindLength}} }
          ],
          "buffers": [ { "byteLength": {{binaryLength}} } ]
        }
        """;

        return Glb(json, binary);
    }

    /// <summary>Wraps a JSON document and a binary chunk in the GLB container the specification defines.</summary>
    private static byte[] Glb(string json, byte[] binary)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        // Both chunks are padded on four-byte boundaries, JSON with spaces and the binary chunk with zeros.
        int jsonPadded = (jsonBytes.Length + 3) & ~3;
        int binaryPadded = (binary.Length + 3) & ~3;

        int total = 12 + 8 + jsonPadded + 8 + binaryPadded;

        using MemoryStream stream = new(total);
        using BinaryWriter writer = new(stream);

        writer.Write(0x46546C67u);          // "glTF"
        writer.Write(2u);                   // version
        writer.Write((uint)total);

        writer.Write((uint)jsonPadded);
        writer.Write(0x4E4F534Au);          // "JSON"
        writer.Write(jsonBytes);
        writer.Write(new byte[jsonPadded - jsonBytes.Length]);

        writer.Write((uint)binaryPadded);
        writer.Write(0x004E4942u);          // "BIN\0"
        writer.Write(binary);
        writer.Write(new byte[binaryPadded - binary.Length]);

        writer.Flush();

        return stream.ToArray();
    }

    private static void WriteVector3Array(byte[] target, int offset, Vector3[] values)
    {
        foreach (Vector3 value in values)
        {
            WriteFloat(target, offset, value.X);
            WriteFloat(target, offset + 4, value.Y);
            WriteFloat(target, offset + 8, value.Z);
            offset += 12;
        }
    }

    private static void WriteVector4Array(byte[] target, int offset, Vector4[] values)
    {
        foreach (Vector4 value in values)
        {
            WriteFloat(target, offset, value.X);
            WriteFloat(target, offset + 4, value.Y);
            WriteFloat(target, offset + 8, value.Z);
            WriteFloat(target, offset + 12, value.W);
            offset += 16;
        }
    }

    private static void WriteFloatArray(byte[] target, int offset, float[] values)
    {
        foreach (float value in values)
        {
            WriteFloat(target, offset, value);
            offset += 4;
        }
    }

    private static void WriteFloat(byte[] target, int offset, float value)
    {
        BitConverter.TryWriteBytes(target.AsSpan(offset, 4), value);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(target, offset, 4);
        }
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        BitConverter.TryWriteBytes(target.AsSpan(offset, 4), value);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(target, offset, 4);
        }
    }

    /// <summary>A glTF MAT4 is column-major; the identity is the same either way.</summary>
    private static void WriteIdentityMatrix(byte[] target, int offset)
    {
        for (int column = 0; column < 4; column++)
        {
            for (int row = 0; row < 4; row++)
            {
                WriteFloat(target, offset + ((column * 4) + row) * 4, column == row ? 1f : 0f);
            }
        }
    }
}
