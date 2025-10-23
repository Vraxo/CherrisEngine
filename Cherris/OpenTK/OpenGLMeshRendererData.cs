using Cherris;
using OpenTK.Graphics.OpenGL;
using System;

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
        GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(sizeof(ushort) * mesh.Indices.Length), mesh.Indices, BufferUsageHint.StaticDraw);

        const int posBytes = 3 * 4;
        const int colorBytes = 4 * 4;
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, 0);

        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, posBytes);

        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, posBytes + colorBytes);

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