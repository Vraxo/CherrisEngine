using System.Numerics;
using Veldrid;

namespace Cherris;

public class MeshRenderer : Component
{
    private DeviceBuffer _vertexBuffer;
    private DeviceBuffer _indexBuffer;
    private uint _indexCount;
    private ResourceSet _textureResourceSet;
    private DeviceBuffer _materialPropertiesBuffer;
    private ResourceSet _materialResourceSet;

    private readonly Mesh _mesh;
    private readonly Texture _texture;
    private readonly Vector2 _textureTiling;
    private readonly GraphicsDevice _gd;

    public MeshRenderer(Mesh mesh, GraphicsDevice gd, ResourceLayout textureLayout, ResourceLayout materialLayout, Sampler sampler, Texture texture, Vector2 textureTiling)
    {
        _mesh = mesh;
        _gd = gd;
        _texture = texture;
        _textureTiling = textureTiling;

        CreateResources(textureLayout, materialLayout, sampler);
    }

    private void CreateResources(ResourceLayout textureLayout, ResourceLayout materialLayout, Sampler sampler)
    {
        ResourceFactory factory = _gd.ResourceFactory;

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(Vertex.SizeInBytes * _mesh.Vertices.Length),
            BufferUsage.VertexBuffer));
        _gd.UpdateBuffer(_vertexBuffer, 0, _mesh.Vertices);

        _indexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(sizeof(ushort) * _mesh.Indices.Length),
            BufferUsage.IndexBuffer));
        _gd.UpdateBuffer(_indexBuffer, 0, _mesh.Indices);

        _indexCount = (uint)_mesh.Indices.Length;

        _textureResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            textureLayout,
            _texture.VeldridTextureView,
            sampler));

        _materialPropertiesBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer)); // Vector4 is 16 bytes
        var materialData = new Vector4(_textureTiling.X, _textureTiling.Y, 0, 0);
        _gd.UpdateBuffer(_materialPropertiesBuffer, 0, materialData);

        _materialResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            materialLayout,
            _materialPropertiesBuffer));
    }

    public void RecreateResources(ResourceLayout textureLayout, ResourceLayout materialLayout, Sampler sampler)
    {
        _textureResourceSet?.Dispose();
        _materialResourceSet?.Dispose();
        _materialPropertiesBuffer?.Dispose();

        CreateResources(textureLayout, materialLayout, sampler);
    }

    public void Render(CommandList cl, Pipeline pipeline, ResourceSet mvpResourceSet)
    {
        cl.SetVertexBuffer(0, _vertexBuffer);
        cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
        cl.SetPipeline(pipeline);
        cl.SetGraphicsResourceSet(0, mvpResourceSet);
        cl.SetGraphicsResourceSet(1, _textureResourceSet);
        cl.SetGraphicsResourceSet(2, _materialResourceSet);

        cl.DrawIndexed(
            indexCount: _indexCount,
            instanceCount: 1,
            indexStart: 0,
            vertexOffset: 0,
            instanceStart: 0);
    }

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _textureResourceSet?.Dispose();
        _materialPropertiesBuffer?.Dispose();
        _materialResourceSet?.Dispose();
    }
}