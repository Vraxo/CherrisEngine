using Veldrid;

namespace VeldridCube
{
    public class MeshRenderer : Component
    {
        private readonly DeviceBuffer _vertexBuffer;
        private readonly DeviceBuffer _indexBuffer;
        private readonly uint _indexCount;

        public MeshRenderer(Mesh mesh, GraphicsDevice gd)
        {
            ResourceFactory factory = gd.ResourceFactory;

            _vertexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(VertexPositionColor.SizeInBytes * mesh.Vertices.Length),
                BufferUsage.VertexBuffer));
            gd.UpdateBuffer(_vertexBuffer, 0, mesh.Vertices);

            _indexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(sizeof(ushort) * mesh.Indices.Length),
                BufferUsage.IndexBuffer));
            gd.UpdateBuffer(_indexBuffer, 0, mesh.Indices);

            _indexCount = (uint)mesh.Indices.Length;
        }

        public void Render(CommandList cl, Pipeline pipeline, ResourceSet mvpResourceSet)
        {
            cl.SetVertexBuffer(0, _vertexBuffer);
            cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            cl.SetPipeline(pipeline);
            cl.SetGraphicsResourceSet(0, mvpResourceSet);

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
        }
    }
}