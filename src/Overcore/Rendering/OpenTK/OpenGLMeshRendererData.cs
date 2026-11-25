using Overcore.Components;
using OpenTK.Graphics.OpenGL4;

namespace Overcore.Rendering.OpenTK;

internal class OpenGLMeshRendererData : IDisposable
{
    public readonly int VaoHandle;
    public readonly int VboHandle;
    public readonly int EboHandle;
    public readonly int IndexCount;
    private bool _disposed;

    public OpenGLMeshRendererData(Mesh mesh)
    {
        IndexCount = mesh.Indices.Length;

        VaoHandle = GL.GenVertexArray();
        GL.BindVertexArray(VaoHandle);

        VboHandle = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, VboHandle);
        GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(Vertex.SizeInBytes * mesh.Vertices.Length), mesh.Vertices, BufferUsageHint.StaticDraw);

        EboHandle = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboHandle);
        // 32-bit indices: a skinned character passes 65,535 vertices and a ushort index wraps there.
        GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(sizeof(uint) * mesh.Indices.Length), mesh.Indices, BufferUsageHint.StaticDraw);

        const int posBytes = 3 * 4;
        const int normalBytes = 3 * 4;
        const int colorBytes = 4 * 4;
        const int texCoordBytes = 2 * 4;
        const int boneIndicesBytes = 4 * 4;
        const int boneWeightsBytes = 4 * 4;

        // Position (location = 0)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, 0);

        // Normal (location = 1)
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, posBytes);

        // Color (location = 2)
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, posBytes + normalBytes);

        // TexCoord (location = 3)
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, posBytes + normalBytes + colorBytes);

        // Bone indices and weights (locations 4 and 5). The scene shader does not declare them, so they cost the
        // static pipeline nothing, and the skinning shader reads them where they are.
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(
            4, 4, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes,
            posBytes + normalBytes + colorBytes + texCoordBytes);

        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(
            5, 4, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes,
            posBytes + normalBytes + colorBytes + texCoordBytes + boneIndicesBytes);

        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            GL.DeleteBuffer(VboHandle);
            GL.DeleteBuffer(EboHandle);
            GL.DeleteVertexArray(VaoHandle);
            _disposed = true;
        }
    }
}