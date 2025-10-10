using Veldrid;

namespace Cherris;

public class MeshRenderer : Component
{
    private readonly DeviceBuffer _vertexBuffer;
    private readonly DeviceBuffer _indexBuffer;
    private readonly uint _indexCount;
    private readonly ResourceSet _textureResourceSet;

    public MeshRenderer(Mesh mesh, GraphicsDevice gd, ResourceLayout textureLayout, Sampler sampler, Texture texture)
    {
        ResourceFactory factory = gd.ResourceFactory;

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(Vertex.SizeInBytes * mesh.Vertices.Length),
            BufferUsage.VertexBuffer));
        gd.UpdateBuffer(_vertexBuffer, 0, mesh.Vertices);

        _indexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(sizeof(ushort) * mesh.Indices.Length),
            BufferUsage.IndexBuffer));
        gd.UpdateBuffer(_indexBuffer, 0, mesh.Indices);

        _indexCount = (uint)mesh.Indices.Length;

        _textureResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            textureLayout,
            texture.VeldridTextureView,
            sampler));
    }

    public void Render(CommandList cl, Pipeline pipeline, ResourceSet mvpResourceSet)
    {
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        cl.SetPipeline(pipeline);
        cl.SetGraphicsResourceSet(0, mvpResourceSet);
        cl.SetGraphicsResourceSet(1, _textureResourceSet);

        cl.DrawIndexed(
            indexCount: _indexCount,
            instanceCount: 1,
            indexStart: 0,
            vertexOffset: 0,
            instanceStart: 0);
    }

    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _textureResourceSet.Dispose();
    }
}