using OpenTK.Graphics.OpenGL4;

namespace Cherris.Rendering.OpenTK;

internal partial class OpenGLPostProcessor
{
    public class OpenGLPostProcessResources : IDisposable
    {
        public Framebuffer? Msaa { get; set; }
        public Framebuffer? Resolved { get; set; }
        public Framebuffer[] Bloom { get; set; } = new Framebuffer[2];
        public Framebuffer? Composite { get; set; }

        public ShaderProgram BrightPassShader { get; }
        public ShaderProgram BlurShader { get; }
        public ShaderProgram FinalCompositeShader { get; }

        private readonly int _quadVao;
        private readonly int _quadVbo;

        public OpenGLPostProcessResources()
        {
            BrightPassShader = ShaderProgram.FromFiles("Shaders/post_quad.vert", "Shaders/post_brightpass.frag");
            BlurShader = ShaderProgram.FromFiles("Shaders/post_quad.vert", "Shaders/post_blur.frag");
            FinalCompositeShader = ShaderProgram.FromFiles("Shaders/post_quad.vert", "Shaders/post_composite.frag");

            if (BrightPassShader == null || BlurShader == null || FinalCompositeShader == null)
            {
                throw new InvalidOperationException("Failed to load post-processing shaders");
            }

            (_quadVao, _quadVbo) = CreateFullscreenQuad();
        }

        public void RenderQuad()
        {
            if (_quadVao == 0)
            {
                return;
            }

            GL.BindVertexArray(_quadVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);
        }

        private static (int vao, int vbo) CreateFullscreenQuad()
        {
            float[] quadVertices = {
                -1.0f,  1.0f,  0.0f, 1.0f,
                -1.0f, -1.0f,  0.0f, 0.0f,
                 1.0f, -1.0f,  1.0f, 0.0f,
                -1.0f,  1.0f,  0.0f, 1.0f,
                 1.0f, -1.0f,  1.0f, 0.0f,
                 1.0f,  1.0f,  1.0f, 1.0f
            };

            int vao = GL.GenVertexArray();
            int vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * quadVertices.Length, quadVertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.BindVertexArray(0);
            return (vao, vbo);
        }

        public void Dispose()
        {
            BrightPassShader?.Dispose();
            BlurShader?.Dispose();
            FinalCompositeShader?.Dispose();
            GL.DeleteVertexArray(_quadVao);
            GL.DeleteBuffer(_quadVbo);
        }
    }
}