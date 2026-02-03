using Cherris.Core.Logging;
using Cherris.Utils;
using SharpGLTF.Schema2;
using System.Numerics;
using Veldrid;
using Mesh = Cherris.Components.Mesh;

namespace Cherris.Core;

public static class ModelLoader
{
    public static Dictionary<string, Mesh> LoadMeshesFromFile(string path)
    {
        var loadedMeshes = new Dictionary<string, Mesh>();
        ModelRoot? model = null;

        try
        {
            // 1. Try loading from Disk (Preferred for .gltf with external refs)
            string? realPath = ProjectFiles.Find(path);
            if (realPath is not null)
            {
                // Verify extension to avoid loading non-model files by accident
                if (realPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
                    realPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                {
                    model = ModelRoot.Load(realPath);
                }
            }

            // 2. Try loading from VFS Stream (Packed assets)
            if (model is null)
            {
                using var stream = ProjectFiles.Open(path);
                if (stream is not null)
                {
                    if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    {
                        model = ModelRoot.ReadGLB(stream);
                    }
                    else
                    {
                        Logger.Warning($"[ModelLoader] Packed .gltf files ('{path}') are not supported. Please use .glb for packed assets.");
                        return loadedMeshes;
                    }
                }
            }

            if (model is null)
            {
                Logger.Warning($"[ModelLoader] Model not found or failed to load: {path}");
                return loadedMeshes;
            }

            Logger.Info($"[ModelLoader] Loading model '{path}', found {model.LogicalMeshes.Count} logical mesh(es).");

            for (int i = 0; i < model.LogicalMeshes.Count; i++)
            {
                var gltfMesh = model.LogicalMeshes[i];
                var allVertices = new List<Vertex>();
                var allIndices = new List<ushort>();

                foreach (var primitive in gltfMesh.Primitives)
                {
                    var positionsAccessor = primitive.GetVertexAccessor("POSITION");
                    if (positionsAccessor is null)
                    {
                        continue;
                    }

                    var positions = positionsAccessor.AsVector3Array();
                    if (positions.Count == 0)
                    {
                        continue;
                    }

                    var normalsAccessor = primitive.GetVertexAccessor("NORMAL");
                    var texCoordsAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
                    var indicesAccessor = primitive.IndexAccessor;
                    if (indicesAccessor is null)
                    {
                        continue;
                    }

                    var indices = indicesAccessor.AsIndicesArray();
                    IList<Vector3>? normals = normalsAccessor?.AsVector3Array();
                    IList<Vector2>? texCoords = texCoordsAccessor?.AsVector2Array();

                    ushort baseVertex = (ushort)allVertices.Count;

                    for (int v = 0; v < positions.Count; v++)
                    {
                        allVertices.Add(new Vertex(
                            positions[v],
                            normals is not null ? normals[v] : Vector3.UnitY,
                            RgbaFloat.White,
                            texCoords is not null ? texCoords[v] : Vector2.Zero
                        ));
                    }

                    for (int tri = 0; tri < indices.Count; tri += 3)
                    {
                        allIndices.Add((ushort)(baseVertex + indices[tri]));
                        allIndices.Add((ushort)(baseVertex + indices[tri + 2]));
                        allIndices.Add((ushort)(baseVertex + indices[tri + 1]));
                    }
                }

                if (allVertices.Any())
                {
                    string meshName = string.IsNullOrWhiteSpace(gltfMesh.Name) ? $"mesh_{i}" : gltfMesh.Name;
                    loadedMeshes[meshName] = new Mesh(allVertices.ToArray(), allIndices.ToArray());
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"[ModelLoader] Failed to load model from '{path}': {e.Message}");
        }

        return loadedMeshes;
    }
}