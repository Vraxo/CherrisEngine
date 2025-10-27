// --- REQUIRED NUGET PACKAGE ---
// This file requires the 'SharpGLTF.Core' package.
// Add it to your project:
// dotnet add package SharpGLTF.Core

using SharpGLTF.Schema2;
using System.Numerics;
using Veldrid;

namespace Cherris;

public static class ModelLoader
{
    public static Dictionary<string, Mesh> LoadMeshesFromFile(string path)
    {
        var loadedMeshes = new Dictionary<string, Mesh>();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[ModelLoader] File not found: {path}");
            return loadedMeshes;
        }

        try
        {
            var model = ModelRoot.Load(path);
            Console.WriteLine($"[ModelLoader] Loading model '{path}', found {model.LogicalMeshes.Count} logical mesh(es).");

            for (int i = 0; i < model.LogicalMeshes.Count; i++)
            {
                var gltfMesh = model.LogicalMeshes[i];

                var allVertices = new List<Vertex>();
                var allIndices = new List<ushort>();

                foreach (var primitive in gltfMesh.Primitives)
                {
                    var positionsAccessor = primitive.GetVertexAccessor("POSITION");
                    if (positionsAccessor == null) continue;
                    var positions = positionsAccessor.AsVector3Array();
                    if (positions.Count == 0) continue;

                    var normalsAccessor = primitive.GetVertexAccessor("NORMAL");
                    var texCoordsAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
                    var indicesAccessor = primitive.IndexAccessor;

                    if (indicesAccessor == null) continue;
                    var indices = indicesAccessor.AsIndicesArray();

                    IList<Vector3> normals = normalsAccessor?.AsVector3Array();
                    IList<Vector2> texCoords = texCoordsAccessor?.AsVector2Array();

                    ushort baseVertex = (ushort)allVertices.Count;

                    for (int v = 0; v < positions.Count; v++)
                    {
                        allVertices.Add(new Vertex(
                            positions[v],
                            normals != null ? normals[v] : Vector3.UnitY,
                            RgbaFloat.White,
                            texCoords != null ? texCoords[v] : Vector2.Zero
                        ));
                    }

                    // *** THE FIX IS HERE ***
                    // glTF uses Counter-Clockwise winding, but our renderer expects Clockwise.
                    // We must reverse the winding order of each triangle.
                    for (int tri = 0; tri < indices.Count; tri += 3)
                    {
                        uint i0 = indices[tri];
                        uint i1 = indices[tri + 1];
                        uint i2 = indices[tri + 2];

                        allIndices.Add((ushort)(baseVertex + i0));
                        allIndices.Add((ushort)(baseVertex + i2)); // Swapped i1 and i2
                        allIndices.Add((ushort)(baseVertex + i1));
                    }
                }

                if (allVertices.Any())
                {
                    string meshName = string.IsNullOrWhiteSpace(gltfMesh.Name) ? $"mesh_{i}" : gltfMesh.Name;
                    var newMesh = new Mesh(allVertices.ToArray(), allIndices.ToArray());
                    loadedMeshes[meshName] = newMesh;
                    Console.WriteLine($"[ModelLoader] Created mesh '{meshName}' with {newMesh.Vertices.Length} vertices and {newMesh.Indices.Length / 3} triangles.");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ModelLoader] Failed to load model from '{path}': {e.Message}");
        }

        return loadedMeshes;
    }
}