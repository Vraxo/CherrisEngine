using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Cherris;

// It's good practice to define a struct that matches the shader's uniform buffer layout.
[StructLayout(LayoutKind.Sequential)]
public struct MaterialProperties
{
    public Vector2 TextureTiling;
    private float _padding1; // Veldrid requires uniform buffer members to be aligned to 16 bytes.
    private float _padding2;
    public Vector3 EmissiveColor;
    public float EmissiveIntensity;
}

public class MeshRenderer : Component
{
    private DeviceBuffer _vertexBuffer;
    private DeviceBuffer _indexBuffer;
    private uint _indexCount;
    private ResourceSet _textureResourceSet;
    private DeviceBuffer _materialPropertiesBuffer;
    private ResourceSet _materialResourceSet;

    public Mesh Mesh => _mesh;
    public DeviceBuffer VertexBuffer => _vertexBuffer;
    public DeviceBuffer IndexBuffer => _indexBuffer;
    public uint IndexCount => _indexCount;

    private readonly Mesh _mesh;
    private readonly Texture _texture;
    private readonly MaterialProperties _materialProperties;
    private readonly GraphicsDevice _gd;

    public MeshRenderer(Mesh mesh, GraphicsDevice gd, ResourceLayout textureLayout, ResourceLayout materialLayout, Sampler sampler, Texture texture, MaterialProperties materialProperties)
    {
        _mesh = mesh;
        _gd = gd;
        _texture = texture;
        _materialProperties = materialProperties;

        CreateDeviceBuffers();
        CreateResourceSets(textureLayout, materialLayout, sampler);
    }

    private void CreateDeviceBuffers()
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
    }

    private void CreateResourceSets(ResourceLayout textureLayout, ResourceLayout materialLayout, Sampler sampler)
    {
        ResourceFactory factory = _gd.ResourceFactory;

        _textureResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            textureLayout,
            _texture.VeldridTextureView,
            sampler));

        _materialPropertiesBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<MaterialProperties>(), BufferUsage.UniformBuffer));
        _gd.UpdateBuffer(_materialPropertiesBuffer, 0, _materialProperties);

        _materialResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            materialLayout,
            _materialPropertiesBuffer));
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