using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using Cherris.Rendering;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Vector3 = System.Numerics.Vector3;

namespace Cherris;

public class OpenTKRenderer : IRenderer
{
    private ShaderProgram _shaderProgram;
    private int _mvpLocation;
    private int _textureLocation;

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

            int success;
            GL.GetProgrami(Handle, ProgramProperty.LinkStatus, out success);
            if (success == 0)
            {
                GL.GetProgramInfoLog(Handle, out string info);
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

            int success;
            GL.GetShaderi(shader, ShaderParameterName.CompileStatus, out success);
            if (success == 0)
            {
                GL.GetShaderInfoLog(shader, out string info);
                throw new InvalidOperationException($"Failed to compile {type}: {info}");
            }
            return shader;
        }

        public void Use() => GL.UseProgram(Handle);
        public int GetUniformLocation(string name) => GL.GetUniformLocation(Handle, name);

        public void Dispose()
        {
            GL.DeleteProgram(Handle);
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

            VboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, VboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(Vertex.SizeInBytes * mesh.Vertices.Length), mesh.Vertices, BufferUsage.StaticDraw);

            EboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboHandle);
            GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(sizeof(ushort) * mesh.Indices.Length), mesh.Indices, BufferUsage.StaticDraw);

            VaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(VaoHandle);

            GL.BindBuffer(BufferTarget.ArrayBuffer, VboHandle);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, EboHandle);

            // Position
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, 0);

            // Color
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, 12);

            // TexCoord
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (int)Vertex.SizeInBytes, 12 + 16);

            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            GL.DeleteBuffer(VboHandle);
            GL.DeleteBuffer(EboHandle);
            GL.DeleteVertexArray(VaoHandle);
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
        _mvpLocation = _shaderProgram.GetUniformLocation("mvp");
        _textureLocation = _shaderProgram.GetUniformLocation("uTexture");

        GL.ClearColor(0.1f, 0.1f, 0.2f, 1.0f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.FrontFace(FrontFaceDirection.Cw); // Match Veldrid's winding order
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        CheckGLError("Setup");
    }

    private OpenGLMeshRendererData GetOrCreateBackendData(MeshRenderer mr)
    {
        if (mr.BackendData is OpenGLMeshRendererData data)
        {
            return data;
        }
        var newData = new OpenGLMeshRendererData(mr.Mesh);
        mr.BackendData = newData;
        return newData;
    }

    public void OnWindowResized() { }

    private static Matrix4d ToOpenTKMatrixd(System.Numerics.Matrix4x4 m)
    {
        return new Matrix4d(
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

        _shaderProgram.Use();

        var view = ToOpenTKMatrixd(mainCamera.GetViewMatrix());
        var projection = ToOpenTKMatrixd(mainCamera.GetProjectionMatrix(windowWidth / windowHeight));

        GL.Uniform1i(_textureLocation, 0);

        foreach (var go in gameObjects)
        {
            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer?.Mesh is null) continue;

            var data = GetOrCreateBackendData(meshRenderer);

            if (meshRenderer.Texture is OpenTKTexture glTexture)
            {
                glTexture.Bind(TextureUnit.Texture0);
            }

            var model = ToOpenTKMatrixd(go.Transform.GetModelMatrix());
            var mvp = model * view * projection;
            GL.UniformMatrix4d(_mvpLocation, 1, true, ref mvp);

            GL.Uniform2f(_shaderProgram.GetUniformLocation("uTiling"),
                meshRenderer.TextureTiling.X, meshRenderer.TextureTiling.Y);

            GL.Uniform3f(_shaderProgram.GetUniformLocation("uEmissive"),
                meshRenderer.EmissiveColor.X, meshRenderer.EmissiveColor.Y, meshRenderer.EmissiveColor.Z);

            GL.BindVertexArray(data.VaoHandle);
            GL.DrawElements(PrimitiveType.Triangles, data.IndexCount, DrawElementsType.UnsignedShort, 0);
            GL.BindVertexArray(0); // Unbind after use
            CheckGLError($"Draw '{go.Name}'");
        }
    }

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