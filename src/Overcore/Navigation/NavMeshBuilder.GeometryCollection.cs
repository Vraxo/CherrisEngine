using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Navigation;

public static partial class NavMeshBuilder
{
    private static void AppendMeshTriangles(
        Mesh mesh,
        Matrix4x4 world,
        int area,
        BoundingBox? clipBounds,
        List<NavMeshBuildSourceTriangle> output)
    {
        Vertex[] verts = mesh.Vertices;
        uint[] indices = mesh.Indices;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Vector3 a = Vector3.Transform(verts[indices[i]].Position, world);
            Vector3 b = Vector3.Transform(verts[indices[i + 1]].Position, world);
            Vector3 c = Vector3.Transform(verts[indices[i + 2]].Position, world);

            // Check whether world matrix has negative determinant (mirrored scale) or mesh is wound CW in OpenGL;
            // Mesh.CreatePlane winds (0,2,1) with normal +Y: let's ensure upward-facing vertex normals orient the triangle!
            Vector3 avgVertexNormal = Vector3.TransformNormal(
                verts[indices[i]].Normal + verts[indices[i + 1]].Normal + verts[indices[i + 2]].Normal,
                world);
            Vector3 geoCross = Vector3.Cross(b - a, c - a);
            if (avgVertexNormal.LengthSquared() > 1e-6f && Vector3.Dot(geoCross, avgVertexNormal) < 0f)
            {
                (b, c) = (c, b);
            }

            if (clipBounds is { } clip && !TriangleOverlapsAabb(a, b, c, clip))
            {
                continue;
            }

            output.Add(new NavMeshBuildSourceTriangle(a, b, c, area));
        }
    }

    private static void AppendColliderTriangles(
        Collider collider,
        int area,
        BoundingBox? clipBounds,
        List<NavMeshBuildSourceTriangle> output)
    {
        collider.GetWorldPose(out Vector3 center, out Quaternion rotation, out Vector3 rawScale);
        Vector3 scale = Vector3.Abs(rawScale);

        Mesh? approxMesh = collider switch
        {
            BoxCollider box => CreateBoxApproximation(Vector3.Abs(box.Size) * scale),
            SphereCollider sphere => CreateSphereApproximation(sphere.Radius * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z))),
            CapsuleCollider capsule => CreateBoxApproximation(new Vector3(
                capsule.Radius * 2f * MathF.Max(scale.X, scale.Z),
                MathF.Max(capsule.Height * scale.Y, capsule.Radius * 2f * MathF.Max(scale.X, scale.Z)),
                capsule.Radius * 2f * MathF.Max(scale.X, scale.Z))),
            _ => null
        };

        if (approxMesh is null)
        {
            return;
        }

        Matrix4x4 pose = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(center);
        AppendMeshTriangles(approxMesh, pose, area, clipBounds, output);
    }

    private static Mesh CreateBoxApproximation(Vector3 size)
    {
        Mesh cube = Mesh.CreateCube();
        Vertex[] scaled = new Vertex[cube.Vertices.Length];
        for (int i = 0; i < scaled.Length; i++)
        {
            Vertex v = cube.Vertices[i];
            scaled[i] = new Vertex(v.Position * size, v.Normal, v.Color, v.TexCoord);
        }

        return new Mesh(scaled, cube.Indices);
    }

    private static Mesh CreateSphereApproximation(float radius)
    {
        const int latSegments = 8;
        const int lonSegments = 12;

        List<Vertex> verts = [];
        List<uint> indices = [];

        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = lat * MathF.PI / latSegments;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float phi = lon * MathF.Tau / lonSegments;
                Vector3 normal = new(sinTheta * MathF.Cos(phi), cosTheta, sinTheta * MathF.Sin(phi));
                verts.Add(new Vertex(normal * radius, normal, Veldrid.RgbaFloat.White, Vector2.Zero));
            }
        }

        int stride = lonSegments + 1;
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                uint first = (uint)((lat * stride) + lon);
                uint second = first + (uint)stride;

                indices.Add(first);
                indices.Add(first + 1);
                indices.Add(second);

                indices.Add(second);
                indices.Add(first + 1);
                indices.Add(second + 1);
            }
        }

        return new Mesh([.. verts], [.. indices]);
    }

    private static bool TriangleOverlapsAabb(Vector3 a, Vector3 b, Vector3 c, BoundingBox box)
    {
        Vector3 tMin = Vector3.Min(a, Vector3.Min(b, c));
        Vector3 tMax = Vector3.Max(a, Vector3.Max(b, c));
        return tMax.X >= box.Min.X && tMin.X <= box.Max.X
            && tMax.Y >= box.Min.Y && tMin.Y <= box.Max.Y
            && tMax.Z >= box.Min.Z && tMin.Z <= box.Max.Z;
    }
}
