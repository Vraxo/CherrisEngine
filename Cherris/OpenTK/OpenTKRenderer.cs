// OpenTKRenderer.cs
using Cherris.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Vector3 = System.Numerics.Vector3;

namespace Cherris
{
    public class OpenTKRenderer : IRenderer, IDisposable
    {
        private ShaderProgram _shaderProgram;
        private int _mvpLocation;
        private int _textureLocation;
        private int _tilingLocation;
        private int _emissiveLocation;

        #region Shader Program Helper
        private class ShaderProgram : IDisposable
        {
            public readonly int Handle;

            public ShaderProgram(string vertexSource, string fragmentSource)
            {
                var vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
                var fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

                Handle = GL.CreateProgram();
                GL.AttachShader(Handle, vertexShader);
                GL.AttachShader(Handle, fragmentShader);
                GL.LinkProgram(Handle);

                // <-- modern OpenTK: use GetProgram with GetProgramParameterName
                GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int linkStatus);
                if (linkStatus == 0)
                {
                    var info = GL.GetProgramInfoLog(Handle);
                    throw new InvalidOperationException($"Failed to link shader program: {info}");
                }

                GL.DetachShader(Handle, vertexShader);
                GL.DetachShader(Handle, fragmentShader);
                GL.DeleteShader(vertexShader);
                GL.DeleteShader(fragmentShader);
            }

            private static int CompileShader(ShaderType type, string source)
            {
                var shader = GL.CreateShader(type);
                GL.ShaderSource(shader, source);
                GL.CompileShader(shader);

                // <-- modern OpenTK: use GetShader with ShaderParameter
                GL.GetShader(shader, ShaderParameter.CompileStatus, out int compileStatus);
                if (compileStatus == 0)
                {
                    var info = GL.GetShaderInfoLog(shader);
                    throw new InvalidOperationException($"Failed to compile {type}: {info}");
                }
                return shader;
            }

            public void Use() => GL.UseProgram(Handle);
            public int GetUniformLocation(string name) => GL.GetUniformLocation(Handle, name);

            public void Dispose()
            {
                if (Handle != 0) GL.DeleteProgram(Handle);
            }
        }
        #endregion

        #region Mesh Data Helper
        internal class OpenGLMeshRendererData : IDisposable
        {
            public readonly int VaoHandle;
            public readonly int VboHandle;
            public readonly int EboHandle;
            public readonly int IndexCount;

            public OpenGLMeshRendererData(Mesh mesh)
            {
                IndexCount = mesh.Indices.Length;

                VaoHandle = GL.GenVertexArray();
                GL.BindVertexArray(VaoHandle);

                VboHandle = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, VboHandle);

                // <-- BufferUsageHint (modern name)
                GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(Vertex.SizeInBytes * mesh.Vertices.Length), mesh.Vertices, BufferUsageHint.StaticDraw);

                EboHandle = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboHandle);
                GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(sizeof(ushort) * mesh.Indices.Length), mesh.Indices, BufferUsageHint.StaticDraw);

                // Vertex attribute layout: position (0), color (1), texcoord (2)
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
                if (VboHandle != 0) GL.DeleteBuffer(VboHandle);
                if (EboHandle != 0) GL.DeleteBuffer(EboHandle);
                if (VaoHandle != 0) GL.DeleteVertexArray(VaoHandle);
            }
        }
        #endregion

        public OpenTKRenderer()
        {
            const string vertSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec4 aColor;
layout (location = 2) in vec2 aTexCoord;

uniform mat4 mvp;
uniform vec2 uTiling;

out vec4 fsin_Color;
out vec2 fsin_TexCoord;

void main()
{
    gl_Position = mvp * vec4(aPosition, 1.0);
    fsin_Color = aColor;
    fsin_TexCoord = aTexCoord * uTiling;
}";

            const string fragSource = @"
#version 330 core
in vec4 fsin_Color;
in vec2 fsin_TexCoord;

uniform sampler2D uTexture;
uniform vec3 uEmissive;

out vec4 FragColor;

void main()
{
    vec4 texColor = texture(uTexture, fsin_TexCoord);
    vec3 finalColor = (texColor.rgb * fsin_Color.rgb) + uEmissive;
    FragColor = vec4(finalColor, texColor.a * fsin_Color.a);
}";

            _shaderProgram = new ShaderProgram(vertSource, fragSource);
            _shaderProgram.Use();

            _mvpLocation = _shaderProgram.GetUniformLocation("mvp");
            _textureLocation = _shaderProgram.GetUniformLocation("uTexture");
            _tilingLocation = _shaderProgram.GetUniformLocation("uTiling");
            _emissiveLocation = _shaderProgram.GetUniformLocation("uEmissive");

            GL.ClearColor(0.1f, 0.1f, 0.2f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);
            GL.Enable(EnableCap.CullFace);
            GL.FrontFace(FrontFaceDirection.Cw);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            CheckGLError("Setup");
        }

        private OpenGLMeshRendererData GetOrCreateBackendData(MeshRenderer mr)
        {
            if (mr.BackendData is OpenGLMeshRendererData d) return d;
            var nd = new OpenGLMeshRendererData(mr.Mesh);
            mr.BackendData = nd;
            return nd;
        }

        private static Matrix4 ToOpenTKMatrix(System.Numerics.Matrix4x4 m)
        {
            return new Matrix4(
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44
            );
        }

        public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            CheckGLError("Frame Start");
            if (mainCamera is null) return;

            var view = ToOpenTKMatrix(mainCamera.GetViewMatrix());

            // The projection matrix from the shared Camera component is for Veldrid (LH, 0-1 depth).
            // We must create a new one that is compatible with OpenGL (RH, -1 to 1 depth).
            var projection = Matrix4.CreatePerspectiveFieldOfView(
                mainCamera.FieldOfView * (float)Math.PI / 180.0f,
                windowWidth / windowHeight,
                mainCamera.NearClipPlane,
                mainCamera.FarClipPlane);

            _shaderProgram.Use();

            // Set texture unit 0 (modern GL.Uniform1 overload)
            GL.Uniform1(_textureLocation, 0);

            // Render all objects as opaque. This removes the faulty transparency sort.
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            foreach (var go in gameObjects)
            {
                var mr = go.GetComponent<MeshRenderer>();
                if (mr?.Mesh is null) continue;
                var data = GetOrCreateBackendData(mr);
                DrawObject(go, mr, data, view, projection);
            }

            CheckGLError("Frame End");
        }

        private void DrawObject(GameObject go, MeshRenderer meshRenderer, OpenGLMeshRendererData data, Matrix4 view, Matrix4 projection)
        {
            _shaderProgram.Use();

            if (meshRenderer.Texture is OpenTKTexture glTexture)
            {
                glTexture.Bind(TextureUnit.Texture0);
            }
            else
            {
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }

            var model = ToOpenTKMatrix(go.Transform.GetModelMatrix());

            // The correct multiplication order for row-major matrices is Model -> View -> Projection.
            var mvp = model * view * projection;

            // Upload matrix. OpenTK's Matrix4 is row-major. 
            // `transpose: false` is correct here because the in-memory layout of a row-major
            // matrix is identical to the in-memory layout of a transposed column-major matrix.
            // GLSL expects column-major, so this effectively uploads the transpose of our matrix.
            GL.UniformMatrix4(_mvpLocation, false, ref mvp);

            GL.Uniform2(_tilingLocation, meshRenderer.TextureTiling.X, meshRenderer.TextureTiling.Y);
            GL.Uniform3(_emissiveLocation, meshRenderer.EmissiveColor.X, meshRenderer.EmissiveColor.Y, meshRenderer.EmissiveColor.Z);

            GL.BindVertexArray(data.VaoHandle);
            GL.DrawElements(PrimitiveType.Triangles, data.IndexCount, DrawElementsType.UnsignedShort, 0);
            GL.BindVertexArray(0);

            CheckGLError($"Draw '{go.Name}'");
        }

        public void OnWindowResized() { }
        public void RequestSnapshot(string path) { }
        public void ProcessSnapshot() { }

        [Conditional("DEBUG")]
        private static void CheckGLError(string context)
        {
            var error = GL.GetError();
            while (error != ErrorCode.NoError)
            {
                Console.WriteLine($"[OpenGL Error] After {context}: {error}");
                error = GL.GetError();
            }
        }

        public void Dispose()
        {
            _shaderProgram?.Dispose();
        }
    }
}