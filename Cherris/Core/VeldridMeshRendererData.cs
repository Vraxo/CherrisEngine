using Cherris.Components;
using System.Numerics;
using Veldrid;

namespace Cherris;

public partial class VeldridRenderer
{
    // Veldrid-specific data associated with a MeshRenderer component
    internal class VeldridMeshRendererData : IDisposable
    {
        public readonly DeviceBuffer VertexBuffer;
        public readonly DeviceBuffer IndexBuffer;
        public readonly uint IndexCount;
        public readonly ResourceSet TextureResourceSet;
        public readonly DeviceBuffer MaterialPropertiesBuffer;
        public readonly ResourceSet MaterialResourceSet;

        public VeldridMeshRendererData(GraphicsDevice gd, MeshRenderer meshRenderer, ResourceLayout textureLayout, ResourceLayout materialLayout, Sampler sampler)
        {
            var material = meshRenderer.Material;
            var texture = (Texture)material.Texture.GetBackendHandle();
            ResourceFactory factory = gd.ResourceFactory;

            VertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(Vertex.SizeInBytes * meshRenderer.Mesh.Vertices.Length), BufferUsage.VertexBuffer));
            gd.UpdateBuffer(VertexBuffer, 0, meshRenderer.Mesh.Vertices);

            IndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(sizeof(ushort) * meshRenderer.Mesh.Indices.Length), BufferUsage.IndexBuffer));
            gd.UpdateBuffer(IndexBuffer, 0, meshRenderer.Mesh.Indices);
            IndexCount = (uint)meshRenderer.Mesh.Indices.Length;

            TextureResourceSet = factory.CreateResourceSet(new ResourceSetDescription(textureLayout, texture.VeldridTextureView, sampler));

            MaterialPropertiesBuffer = factory.CreateBuffer(new BufferDescription(32, BufferUsage.UniformBuffer));
            var materialData = new Vector4[2];
            materialData[0] = new Vector4(material.TextureTiling.X, material.TextureTiling.Y, 0, 0);
            materialData[1] = new Vector4(material.EmissiveColor, 1.0f);
            gd.UpdateBuffer(MaterialPropertiesBuffer, 0, materialData);

            MaterialResourceSet = factory.CreateResourceSet(new ResourceSetDescription(materialLayout, MaterialPropertiesBuffer));
        }

        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
            TextureResourceSet.Dispose();
            MaterialPropertiesBuffer.Dispose();
            MaterialResourceSet.Dispose();
        }
    }
}