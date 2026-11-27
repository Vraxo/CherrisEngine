using Overcore.Components;
using Overcore.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;

namespace Overcore.Rendering.OpenTK;

/// <summary>
/// Instanced OpenGL 3.3+ renderer for <see cref="ParticleSystem"/> components.
/// Draws into the HDR MSAA framebuffer after opaque geometry with depth writes disabled,
/// supporting Billboard, StretchedBillboard, and HorizontalBillboard modes plus Alpha and Additive blending.
/// </summary>
internal sealed class OpenGLParticleRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct QuadVertex
    {
        public float CornerX;
        public float CornerY;
        public float U;
        public float V;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleInstanceData
    {
        public System.Numerics.Vector3 Position;
        public System.Numerics.Vector4 Color;
        public System.Numerics.Vector3 Right;
        public System.Numerics.Vector3 Up;
    }

    private struct SortEntry
    {
        public int Index;
        public float DistanceSq;
    }

    private readonly ShaderProgram _shader;
    private readonly int _viewLocation;
    private readonly int _projectionLocation;
    private readonly int _textureLocation;
    private readonly int _hasTextureLocation;

    private readonly int _vao;
    private readonly int _quadVbo;
    private readonly int _quadEbo;
    private readonly int _instanceVbo;

    private ParticleInstanceData[] _instanceScratch = new ParticleInstanceData[256];
    private SortEntry[] _sortScratch = new SortEntry[256];
    private int _gpuInstanceCapacity = 256;
    private bool _disposed;

    public OpenGLParticleRenderer()
    {
        _shader = ShaderProgram.FromFiles("Shaders/particle.vert", "Shaders/particle.frag")
            ?? throw new InvalidOperationException("Failed to load particle shaders.");

        _viewLocation = _shader.GetUniformLocation("view");
        _projectionLocation = _shader.GetUniformLocation("projection");
        _textureLocation = _shader.GetUniformLocation("uTexture");
        _hasTextureLocation = _shader.GetUniformLocation("uHasTexture");

        QuadVertex[] quadVertices =
        [
            new QuadVertex { CornerX = -0.5f, CornerY = -0.5f, U = 0f, V = 0f },
            new QuadVertex { CornerX =  0.5f, CornerY = -0.5f, U = 1f, V = 0f },
            new QuadVertex { CornerX =  0.5f, CornerY =  0.5f, U = 1f, V = 1f },
            new QuadVertex { CornerX = -0.5f, CornerY =  0.5f, U = 0f, V = 1f }
        ];

        uint[] quadIndices = [0u, 1u, 2u, 2u, 3u, 0u];

        _vao = GL.GenVertexArray();
        _quadVbo = GL.GenBuffer();
        _quadEbo = GL.GenBuffer();
        _instanceVbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * Marshal.SizeOf<QuadVertex>(), quadVertices, BufferUsageHint.StaticDraw);

        int quadStride = Marshal.SizeOf<QuadVertex>();
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, quadStride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, quadStride, 2 * sizeof(float));

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _quadEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, quadIndices.Length * sizeof(uint), quadIndices, BufferUsageHint.StaticDraw);

        int instanceStride = Marshal.SizeOf<ParticleInstanceData>();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _gpuInstanceCapacity * instanceStride, IntPtr.Zero, BufferUsageHint.StreamDraw);

        // location 2: iPosition (vec3)
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, instanceStride, 0);
        GL.VertexAttribDivisor(2, 1);

        // location 3: iColor (vec4)
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, instanceStride, 3 * sizeof(float));
        GL.VertexAttribDivisor(3, 1);

        // location 4: iRight (vec3)
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, instanceStride, 7 * sizeof(float));
        GL.VertexAttribDivisor(4, 1);

        // location 5: iUp (vec3)
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, instanceStride, 10 * sizeof(float));
        GL.VertexAttribDivisor(5, 1);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    public void Render(IReadOnlyList<GameObject> gameObjects, Matrix4 view, Matrix4 projection, IResourceManager? resourceManager = null)
    {
        if (gameObjects.Count == 0)
        {
            return;
        }

        // Extract camera world position and basis vectors from the OpenTK view matrix.
        Matrix4 invView = view.Inverted();
        System.Numerics.Vector3 cameraPos = new(invView.Row3.X, invView.Row3.Y, invView.Row3.Z);
        System.Numerics.Vector3 camRight = System.Numerics.Vector3.Normalize(new(view.M11, view.M21, view.M31));
        System.Numerics.Vector3 camUp = System.Numerics.Vector3.Normalize(new(view.M12, view.M22, view.M32));
        System.Numerics.Vector3 camForward = -System.Numerics.Vector3.Normalize(new(view.M13, view.M23, view.M33));

        bool anyDrawn = false;

        foreach (GameObject go in gameObjects)
        {
            if (!go.ActiveInHierarchy)
            {
                continue;
            }

            foreach (ParticleSystem ps in go.GetComponents<ParticleSystem>())
            {
                if (!ps.Enabled || ps.ParticleCount == 0)
                {
                    continue;
                }

                if (!anyDrawn)
                {
                    BeginPass(ref view, ref projection);
                    anyDrawn = true;
                }

                DrawSystem(ps, go, cameraPos, camRight, camUp, camForward, resourceManager);
            }
        }

        if (anyDrawn)
        {
            EndPass();
        }
    }

    private void BeginPass(ref Matrix4 view, ref Matrix4 projection)
    {
        _shader.Use();
        GL.UniformMatrix4(_viewLocation, false, ref view);
        GL.UniformMatrix4(_projectionLocation, false, ref projection);
        GL.Uniform1(_textureLocation, 0);

        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Blend);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindVertexArray(_vao);
    }

    private static void EndPass()
    {
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);
    }

    private void DrawSystem(
        ParticleSystem ps,
        GameObject owner,
        System.Numerics.Vector3 cameraPos,
        System.Numerics.Vector3 camRight,
        System.Numerics.Vector3 camUp,
        System.Numerics.Vector3 camForward,
        IResourceManager? resourceManager)
    {
        ReadOnlySpan<Particle> particles = ps.Particles;
        int count = particles.Length;
        if (count == 0)
        {
            return;
        }

        EnsureScratchCapacity(count);

        // Resolve texture lazily when TextureName was assigned via YAML or inspector text field.
        if (ps.Texture is null && !string.IsNullOrWhiteSpace(ps.TextureName) && resourceManager is not null)
        {
            ps.Texture = resourceManager.GetTexture(ps.TextureName);
        }

        if (ps.Texture is OpenTKTexture glTexture)
        {
            glTexture.Bind(TextureUnit.Texture0);
            GL.Uniform1(_hasTextureLocation, 1);
        }
        else
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Uniform1(_hasTextureLocation, 0);
        }

        if (ps.BlendMode == ParticleBlendMode.Additive)
        {
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        }
        else
        {
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        System.Numerics.Matrix4x4 emitterWorld = owner.Transform.GetModelMatrix();
        bool localSpace = ps.SimulationSpace == ParticleSimulationSpace.Local;

        bool doSort = ps.SortByDepth && ps.BlendMode == ParticleBlendMode.Alpha && count > 1;
        if (doSort)
        {
            for (int i = 0; i < count; i++)
            {
                System.Numerics.Vector3 worldPos = localSpace
                    ? System.Numerics.Vector3.Transform(particles[i].Position, emitterWorld)
                    : particles[i].Position;

                _sortScratch[i] = new SortEntry
                {
                    Index = i,
                    DistanceSq = System.Numerics.Vector3.DistanceSquared(worldPos, cameraPos)
                };
            }

            Array.Sort(_sortScratch, 0, count, BackToFrontComparer.Instance);
        }

        int instanceCount = 0;
        for (int s = 0; s < count; s++)
        {
            int idx = doSort ? _sortScratch[s].Index : s;
            ref readonly Particle p = ref particles[idx];

            float size = ps.EvaluateParticleSize(in p);
            if (size <= 0.0001f)
            {
                continue;
            }

            System.Numerics.Vector4 color = ps.EvaluateParticleColor(in p);
            if (color.W <= 0.001f)
            {
                continue;
            }

            System.Numerics.Vector3 worldPos = localSpace
                ? System.Numerics.Vector3.Transform(p.Position, emitterWorld)
                : p.Position;

            System.Numerics.Vector3 right;
            System.Numerics.Vector3 up;

            switch (ps.RenderMode)
            {
                case ParticleRenderMode.HorizontalBillboard:
                {
                    float cos = MathF.Cos(p.Rotation);
                    float sin = MathF.Sin(p.Rotation);
                    right = new System.Numerics.Vector3(cos, 0f, sin) * size;
                    up = new System.Numerics.Vector3(-sin, 0f, cos) * size;
                    break;
                }

                case ParticleRenderMode.StretchedBillboard:
                {
                    System.Numerics.Vector3 worldVel = localSpace
                        ? System.Numerics.Vector3.TransformNormal(p.Velocity, emitterWorld)
                        : p.Velocity;

                    float speed = worldVel.Length();
                    System.Numerics.Vector3 dir = speed > 1e-4f ? worldVel / speed : camUp;
                    System.Numerics.Vector3 side = System.Numerics.Vector3.Cross(camForward, dir);
                    float sideLen = side.Length();
                    side = sideLen > 1e-4f ? side / sideLen : camRight;

                    float stretchedLength = size * MathF.Max(0.01f, ps.LengthScale + (speed * ps.VelocityScale));
                    right = side * size;
                    up = dir * stretchedLength;
                    break;
                }

                case ParticleRenderMode.Billboard:
                default:
                {
                    float cos = MathF.Cos(p.Rotation);
                    float sin = MathF.Sin(p.Rotation);
                    right = ((camRight * cos) + (camUp * sin)) * size;
                    up = ((-camRight * sin) + (camUp * cos)) * size;
                    break;
                }
            }

            _instanceScratch[instanceCount++] = new ParticleInstanceData
            {
                Position = worldPos,
                Color = color,
                Right = right,
                Up = up
            };
        }

        if (instanceCount == 0)
        {
            return;
        }

        int instanceStride = Marshal.SizeOf<ParticleInstanceData>();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);

        if (instanceCount > _gpuInstanceCapacity)
        {
            while (_gpuInstanceCapacity < instanceCount)
            {
                _gpuInstanceCapacity *= 2;
            }

            GL.BufferData(BufferTarget.ArrayBuffer, _gpuInstanceCapacity * instanceStride, IntPtr.Zero, BufferUsageHint.StreamDraw);
        }

        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, instanceCount * instanceStride, _instanceScratch);
        GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, IntPtr.Zero, instanceCount);
    }

    private void EnsureScratchCapacity(int count)
    {
        if (count <= _instanceScratch.Length)
        {
            return;
        }

        int newCap = _instanceScratch.Length;
        while (newCap < count)
        {
            newCap *= 2;
        }

        _instanceScratch = new ParticleInstanceData[newCap];
        _sortScratch = new SortEntry[newCap];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_quadVbo);
        GL.DeleteBuffer(_quadEbo);
        GL.DeleteBuffer(_instanceVbo);
        _shader.Dispose();
    }

    private sealed class BackToFrontComparer : IComparer<SortEntry>
    {
        public static readonly BackToFrontComparer Instance = new();

        public int Compare(SortEntry x, SortEntry y) => y.DistanceSq.CompareTo(x.DistanceSq);
    }
}
