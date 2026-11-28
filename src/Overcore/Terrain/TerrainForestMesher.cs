using Overcore.Components;
using System.Numerics;
using Veldrid;

namespace Overcore.Terrain;

/// <summary>
/// One tree's solid trunk in terrain-local metres: a vertical cylinder from <see cref="Base"/> (sunk a
/// root's depth into the ground) to <see cref="Top"/>. The physics mesh wears it as a prism and the
/// analytic queries as a cylinder, and the two agree closely enough that a body meets the same tree
/// whichever asks.
/// </summary>
public readonly record struct TerrainTrunk(Vector3 Base, Vector3 Top, float Radius);

/// <summary>
/// The forest as one mesh: every planted tree's prototype mesh, stood on the ground at the tree's place,
/// turned to its facing, grown to its scale, and tinted by its prototype, all merged into a single
/// <see cref="Mesh"/> the terrain draws beside its ground.
/// </summary>
/// <remarks>
/// <para>
/// One merged mesh is one draw, which is the right shape for a forest that changes only when it is
/// planted or sculpted under. GPU instancing would draw the same trees for less memory, and remains the
/// upgrade path if forests grow past what a merged mesh holds comfortably; nothing in the data changes
/// when it arrives, only this builder.
/// </para>
/// <para>
/// A tree's height is read from the ground when the forest is built, never stored, so sculpting a hill
/// lifts the trees standing on it the next time either mesh is rebuilt. A tree planted in a hole has no
/// ground to stand on and is not drawn. What bodies meet is the trunk (see
/// <see cref="EnumerateTrunks"/>): a prototype-sized cylinder at each tree's base, solid to the solver,
/// to rays and to character controllers, while the canopy stays scenery.
/// </para>
/// </remarks>
public static class TerrainForestMesher
{
    /// <summary>How far a tree is sunk into the ground, in metres, so its roots meet sloped ground without floating.</summary>
    private const float RootSink = 0.05f;

    /// <summary>
    /// Builds the forest mesh, or null when there is nothing to draw: no trees, or every prototype's mesh
    /// unresolved.
    /// </summary>
    /// <param name="data">The ground and its forest.</param>
    /// <param name="resolveMesh">Turns a prototype's mesh name into the mesh itself, or null when it cannot.</param>
    public static Mesh? Build(TerrainData data, Func<string, Mesh?> resolveMesh)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(resolveMesh);
        if (data.Trees.Count == 0 || data.TreePrototypes.Count == 0)
        {
            return null;
        }

        Dictionary<int, Mesh?> prototypes = [];
        List<Vertex> vertices = [];
        List<uint> indices = [];

        foreach (TreeInstance tree in data.Trees)
        {
            if (tree.PrototypeIndex < 0 || tree.PrototypeIndex >= data.TreePrototypes.Count
                || data.IsHoleAt(tree.Position.X, tree.Position.Y))
            {
                continue;
            }

            if (!prototypes.TryGetValue(tree.PrototypeIndex, out Mesh? prototypeMesh))
            {
                TerrainTreePrototype prototype = data.TreePrototypes[tree.PrototypeIndex];
                prototypeMesh = string.IsNullOrWhiteSpace(prototype.MeshName) ? null : resolveMesh(prototype.MeshName);
                prototypes[tree.PrototypeIndex] = prototypeMesh;
            }

            if (prototypeMesh is null || prototypeMesh.Vertices.Length == 0)
            {
                continue;
            }

            TerrainTreePrototype kind = data.TreePrototypes[tree.PrototypeIndex];
            Vector3 place = new(
                tree.Position.X * data.Size.X,
                data.SampleHeight(tree.Position.X, tree.Position.Y) - RootSink,
                tree.Position.Y * data.Size.Z);

            float cos = MathF.Cos(tree.Rotation);
            float sin = MathF.Sin(tree.Rotation);
            uint baseIndex = (uint)vertices.Count;

            foreach (Vertex vertex in prototypeMesh.Vertices)
            {
                Vector3 scaled = new(
                    vertex.Position.X * tree.WidthScale,
                    vertex.Position.Y * tree.HeightScale,
                    vertex.Position.Z * tree.WidthScale);
                Vector3 turned = new(
                    (scaled.X * cos) + (scaled.Z * sin),
                    scaled.Y,
                    (scaled.Z * cos) - (scaled.X * sin));
                Vector3 normal = Vector3.Normalize(new Vector3(
                    (vertex.Normal.X * cos) + (vertex.Normal.Z * sin),
                    vertex.Normal.Y,
                    (vertex.Normal.Z * cos) - (vertex.Normal.X * sin)));

                RgbaFloat color = new(
                    vertex.Color.R * kind.Tint.X,
                    vertex.Color.G * kind.Tint.Y,
                    vertex.Color.B * kind.Tint.Z,
                    vertex.Color.A);

                vertices.Add(new Vertex(place + turned, normal, color, vertex.TexCoord));
            }

            foreach (uint index in prototypeMesh.Indices)
            {
                indices.Add(baseIndex + index);
            }
        }

        return vertices.Count == 0 ? null : new Mesh([.. vertices], [.. indices]);
    }

    /// <summary>
    /// Every solid trunk on the terrain, in terrain-local metres. A tree in a hole, or of a prototype
    /// with no trunk, contributes none.
    /// </summary>
    public static IEnumerable<TerrainTrunk> EnumerateTrunks(TerrainData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        foreach (TreeInstance tree in data.Trees)
        {
            if (tree.PrototypeIndex < 0 || tree.PrototypeIndex >= data.TreePrototypes.Count
                || data.IsHoleAt(tree.Position.X, tree.Position.Y))
            {
                continue;
            }

            TerrainTreePrototype kind = data.TreePrototypes[tree.PrototypeIndex];
            float radius = kind.TrunkRadius * tree.WidthScale;
            float height = kind.TrunkHeight * tree.HeightScale;
            if (radius <= 0f || height <= 0f)
            {
                continue;
            }

            Vector3 basePoint = new(
                tree.Position.X * data.Size.X,
                data.SampleHeight(tree.Position.X, tree.Position.Y) - RootSink,
                tree.Position.Y * data.Size.Z);
            yield return new TerrainTrunk(basePoint, basePoint + (Vector3.UnitY * height), radius);
        }
    }

    /// <summary>
    /// The trunks as triangles — eight-sided prisms with a capped top — for the physics mesh a
    /// <see cref="Overcore.Components.TerrainCollider"/> builds. Wound outward, like the ground's own
    /// triangles, so contact normals point away from the bark.
    /// </summary>
    public static List<(Vector3 A, Vector3 B, Vector3 C)> BuildTrunkTriangles(TerrainData data)
    {
        const int sides = 8;
        List<(Vector3 A, Vector3 B, Vector3 C)> triangles = [];

        foreach (TerrainTrunk trunk in EnumerateTrunks(data))
        {
            Vector3[] bottom = new Vector3[sides];
            Vector3[] top = new Vector3[sides];
            for (int side = 0; side < sides; side++)
            {
                float angle = side * MathF.Tau / sides;
                Vector3 rim = new(MathF.Cos(angle) * trunk.Radius, 0f, MathF.Sin(angle) * trunk.Radius);
                bottom[side] = trunk.Base + rim;
                top[side] = trunk.Top + rim;
            }

            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                triangles.Add((bottom[side], top[next], bottom[next]));
                triangles.Add((bottom[side], top[side], top[next]));
                triangles.Add((trunk.Top, top[next], top[side]));
            }
        }

        return triangles;
    }
}
