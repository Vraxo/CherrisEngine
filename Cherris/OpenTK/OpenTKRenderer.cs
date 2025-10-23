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
using Vector2 = System.Numerics.Vector2;
using SkiaSharp;
using DirectUI;
using DirectUI.Core;
using DirectUI.Backends.SkiaSharp;

namespace Cherris;

public class OpenTKRenderer : Cherris.Rendering.IRenderer, IDisposable
{
    // Scene rendering
    private ShaderProgram _shaderProgram;
    private int _mvpLocation;
    private int _textureLocation;
    private int _tilingLocation;
    private int _emissiveLocation;

    // Skybox rendering
    private ShaderProgram _skyboxShaderProgram;
    private int _skyboxViewLocation;
    private int _skyboxProjectionLocation;
    private int _skyboxSamplerLocation;
    private OpenGLMeshRendererData _skyboxCubeData;

    // Post-processing
    private int _quadVao, _quadVbo;
    private ShaderProgram _brightPassShader;
    private ShaderProgram _blurShader;
    private ShaderProgram _finalCompositeShader;

    // Framebuffers and Textures
    private int _width, _height;
    private int _msaaFbo;
    private int _msaaColorTexture;
    private int _rboDepthStencil;

    private int _resolvedFbo;
    private int _resolvedColorTexture;

    private int[] _bloomFbos = new int[2];
    private int[] _bloomTextures = new int[2];

    // --- DirectUI/Skia Integration ---
    private readonly AppEngine _appEngine;
    private GRContext _grContext;
    private SKSurface _skSurface;
    private GRBackendRenderTarget _skRenderTarget;
    private SilkNetRenderer _uiRenderer; // Reusing SilkNetRenderer as it's Skia-based
    private SilkNetTextService _uiTextService;
    public DirectUI.Core.IRenderer UiRenderer => _uiRenderer;
    public ITextService UiTextService => _uiTextService;


    #region Shader Program Helper
    private class ShaderProgram : IDisposable
    {
        public readonly int Handle;
        private bool _disposed;

        public ShaderProgram(string vertexSource, string fragmentSource)
        {
            var vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
            var fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vertexShader);
            GL.AttachShader(Handle, fragmentShader);
            GL.LinkProgram(Handle);

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
            if (!_disposed)
            {
                GL.DeleteProgram(Handle);
                _disposed = true;
            }
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
    #endregion

    public OpenTKRenderer(AppEngine appEngine)
    {
        _appEngine = appEngine;
        SetupShaders();
        SetupQuad();

        _skyboxCubeData = new OpenGLMeshRendererData(Mesh.CreateCube());

        GL.ClearColor(0.1f, 0.1f, 0.2f, 1.0f);
        GL.FrontFace(FrontFaceDirection.Cw);
        GL.Enable(EnableCap.FramebufferSrgb); // Enable automatic linear->sRGB conversion

        // --- Skia Init ---
        var glInterface = GRGlInterface.Create();
        _grContext = GRContext.CreateGl(glInterface);
        _uiTextService = new SilkNetTextService();
        _uiRenderer = new SilkNetRenderer(_uiTextService);

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
        if ((int)windowWidth != _width || (int)windowHeight != _height)
        {
            OnWindowResized((int)windowWidth, (int)windowHeight);
        }
        if (mainCamera is null) return;

        // --- 1. Scene Pass: Render scene to offscreen MSAA framebuffer ---
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
        GL.Viewport(0, 0, _width, _height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
        GL.Disable(EnableCap.Blend); // Ensure blend is disabled for 3D pass

        var view = ToOpenTKMatrix(mainCamera.GetViewMatrix());
        var projection = Matrix4.CreatePerspectiveFieldOfView(
            mainCamera.FieldOfView * (float)Math.PI / 180.0f,
            windowWidth / windowHeight,
            mainCamera.NearClipPlane,
            mainCamera.FarClipPlane);

        // Draw Skybox
        if (skybox?.CubeMapTexture != null)
        {
            DrawSkybox(skybox, view, projection);
        }

        // Draw Scene Objects
        _shaderProgram.Use();
        GL.Uniform1(_textureLocation, 0);
        foreach (var go in gameObjects)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr?.Mesh is null || go.GetComponent<Skybox>() != null) continue;
            var data = GetOrCreateBackendData(mr);
            DrawObject(go, mr, data, view, projection);
        }

        // --- 2. Resolve MSAA Framebuffer ---
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolvedFbo);
        GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        // --- 3. Bloom / Post-Processing ---
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        // A. Bright pass
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFbos[0]);
        GL.Viewport(0, 0, _width / 2, _height / 2);
        _brightPassShader.Use();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _resolvedColorTexture);
        RenderQuad();

        // B. Blur pass
        bool horizontal = true;
        bool firstIteration = true;
        int amount = 10;
        _blurShader.Use();
        GL.Uniform1(_blurShader.GetUniformLocation("image"), 0);
        for (int i = 0; i < amount; i++)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFbos[horizontal ? 1 : 0]);
            GL.Uniform1(_blurShader.GetUniformLocation("horizontal"), horizontal ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _bloomTextures[firstIteration ? 0 : (horizontal ? 0 : 1)]);
            RenderQuad();
            horizontal = !horizontal;
            if (firstIteration) firstIteration = false;
        }

        // --- 4. Final Composite Pass: Render to screen ---
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, _width, _height);

        _finalCompositeShader.Use();
        var imageLoc = _finalCompositeShader.GetUniformLocation("image");
        var exposureLoc = _finalCompositeShader.GetUniformLocation("exposure");
        var isBloomLoc = _finalCompositeShader.GetUniformLocation("isBloomPass");

        // Pass 1: Draw tonemapped scene
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _resolvedColorTexture);
        GL.Uniform1(imageLoc, 0);
        GL.Uniform1(exposureLoc, exposure);
        GL.Uniform1(isBloomLoc, 0); // Corresponds to 'false'
        RenderQuad();

        // Pass 2: Additively blend bloom on top
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        GL.ActiveTexture(TextureUnit.Texture0);
        // The final blurred image is in the texture that was the *target* in the last blur pass
        GL.BindTexture(TextureTarget.Texture2D, _bloomTextures[horizontal ? 0 : 1]);
        GL.Uniform1(imageLoc, 0); // Sampler is still 0
        GL.Uniform1(isBloomLoc, 1); // Corresponds to 'true'
        RenderQuad();

        GL.Disable(EnableCap.Blend); // Reset blend state

        // --- 5. DirectUI Pass ---
        if (_appEngine != null && _skSurface != null)
        {
            // GL state for 2D UI
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _uiRenderer.SetCanvas(_skSurface.Canvas, new Vector2(_width, _height));
            _appEngine.UpdateAndRender(_uiRenderer, _uiTextService);
            _skSurface.Canvas.Flush();
        }
    }

    private void DrawSkybox(Skybox skybox, Matrix4 view, Matrix4 projection)
    {
        GL.DepthFunc(DepthFunction.Lequal);
        GL.CullFace(CullFaceMode.Front);

        _skyboxShaderProgram.Use();

        var skyboxView = view;
        skyboxView.Row3 = new OpenTK.Mathematics.Vector4(0, 0, 0, 1); // Remove translation

        GL.UniformMatrix4(_skyboxViewLocation, false, ref skyboxView);
        GL.UniformMatrix4(_skyboxProjectionLocation, false, ref projection);

        if (skybox.CubeMapTexture is OpenTKTexture glSkyboxTexture)
        {
            glSkyboxTexture.Bind(TextureUnit.Texture0);
            GL.Uniform1(_skyboxSamplerLocation, 0);
        }

        GL.BindVertexArray(_skyboxCubeData.VaoHandle);
        GL.DrawElements(PrimitiveType.Triangles, _skyboxCubeData.IndexCount, DrawElementsType.UnsignedShort, 0);

        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.TextureCubeMap, 0);

        GL.CullFace(CullFaceMode.Back);
        GL.DepthFunc(DepthFunction.Less);
    }

    private void DrawObject(GameObject go, MeshRenderer meshRenderer, OpenGLMeshRendererData data, Matrix4 view, Matrix4 projection)
    {
        if (meshRenderer.Texture is OpenTKTexture glTexture)
        {
            glTexture.Bind(TextureUnit.Texture0);
        }
        else
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        var model = ToOpenTKMatrix(go.Transform.GetModelMatrix());
        var mvp = model * view * projection;

        GL.UniformMatrix4(_mvpLocation, false, ref mvp);
        GL.Uniform2(_tilingLocation, meshRenderer.TextureTiling.X, meshRenderer.TextureTiling.Y);
        GL.Uniform3(_emissiveLocation, meshRenderer.EmissiveColor.X, meshRenderer.EmissiveColor.Y, meshRenderer.EmissiveColor.Z);

        GL.BindVertexArray(data.VaoHandle);
        GL.DrawElements(PrimitiveType.Triangles, data.IndexCount, DrawElementsType.UnsignedShort, 0);
        GL.BindVertexArray(0);

        CheckGLError($"Draw '{go.Name}'");
    }

    private void RenderQuad()
    {
        GL.BindVertexArray(_quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
    }

    private void SetupShaders()
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
        _tilingLocation = _shaderProgram.GetUniformLocation("uTiling");
        _emissiveLocation = _shaderProgram.GetUniformLocation("uEmissive");

        const string skyboxVert = @"
#version 330 core
layout (location = 0) in vec3 aPosition;

out vec3 TexCoords;

uniform mat4 view;
uniform mat4 projection;

void main()
{
    TexCoords = aPosition;
    vec4 pos = projection * view * vec4(aPosition, 1.0);
    gl_Position = pos.xyww;
}";
        const string skyboxFrag = @"
#version 330 core
out vec4 FragColor;
in vec3 TexCoords;
uniform samplerCube skybox;

void main()
{    
    FragColor = texture(skybox, TexCoords);
}";
        _skyboxShaderProgram = new ShaderProgram(skyboxVert, skyboxFrag);
        _skyboxViewLocation = _skyboxShaderProgram.GetUniformLocation("view");
        _skyboxProjectionLocation = _skyboxShaderProgram.GetUniformLocation("projection");
        _skyboxSamplerLocation = _skyboxShaderProgram.GetUniformLocation("skybox");

        const string quadVert = @"
#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoords;
out vec2 TexCoords;
void main()
{
    TexCoords = aTexCoords;
    gl_Position = vec4(aPosition, 0.0, 1.0);
}";

        const string brightPassFrag = @"
#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
uniform sampler2D image;
const float threshold = 1.1;
void main()
{
    vec3 color = texture(image, TexCoords).rgb;
    vec3 finalColor = max(vec3(0.0), color - threshold);
    FragColor = vec4(finalColor, 1.0);
}";
        _brightPassShader = new ShaderProgram(quadVert, brightPassFrag);

        const string blurFrag = @"
#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
uniform sampler2D image;
uniform bool horizontal;

// 5-tap Gaussian blur (Veldrid equivalent)
const float weights[3] = float[](0.227027, 0.316216, 0.070270);
const float offsets[3] = float[](0.0, 1.384615, 3.230769);

void main()
{
    vec2 texelSize = 1.0 / textureSize(image, 0);
    vec3 result = texture(image, TexCoords).rgb * weights[0];
    vec2 dir = horizontal ? vec2(texelSize.x, 0.0) : vec2(0.0, texelSize.y);

    for (int i = 1; i < 3; i++) {
        result += texture(image, TexCoords + offsets[i] * dir).rgb * weights[i];
        result += texture(image, TexCoords - offsets[i] * dir).rgb * weights[i];
    }
    FragColor = vec4(result, 1.0);
}";
        _blurShader = new ShaderProgram(quadVert, blurFrag);

        const string finalCompositeFrag = @"
#version 330 core
out vec4 FragColor;
in vec2 TexCoords;
uniform sampler2D image;
uniform float exposure;
uniform bool isBloomPass;

vec3 tonemap_reinhard(vec3 color) {
    return color / (color + vec3(1.0));
}

void main()
{
    vec3 color = texture(image, TexCoords).rgb;
    if (!isBloomPass) { // This is the main scene pass
        color *= exposure;
        color = tonemap_reinhard(color);
    }
    // else, this is the bloom pass, so we output the raw color for additive blending.
    
    FragColor = vec4(color, 1.0);
}";
        _finalCompositeShader = new ShaderProgram(quadVert, finalCompositeFrag);
    }

    private void SetupQuad()
    {
        float[] quadVertices = {
            // positions   // texCoords
            -1.0f,  1.0f,  0.0f, 1.0f,
            -1.0f, -1.0f,  0.0f, 0.0f,
             1.0f, -1.0f,  1.0f, 0.0f,
             -1.0f,  1.0f,  0.0f, 1.0f,
             1.0f, -1.0f,  1.0f, 0.0f,
             1.0f,  1.0f,  1.0f, 1.0f
        };

        _quadVao = GL.GenVertexArray();
        _quadVbo = GL.GenBuffer();
        GL.BindVertexArray(_quadVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * quadVertices.Length, quadVertices, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (2 * sizeof(float)));
        GL.BindVertexArray(0);
    }

    private void SetupFramebuffers(int width, int height)
    {
        DisposeFramebuffers(); // Clean up old resources

        _width = width;
        _height = height;

        // MSAA FBO for scene rendering
        _msaaFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
        _msaaColorTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2DMultisample, _msaaColorTexture);
        GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, 4, PixelInternalFormat.Rgba16f, width, height, true);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2DMultisample, _msaaColorTexture, 0);
        _rboDepthStencil = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _rboDepthStencil);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, 4, RenderbufferStorage.Depth24Stencil8, width, height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _rboDepthStencil);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) Console.WriteLine("ERROR::FRAMEBUFFER:: MSAA Framebuffer is not complete!");

        // Resolved FBO for post-processing source
        _resolvedFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _resolvedFbo);
        _resolvedColorTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _resolvedColorTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, width, height, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _resolvedColorTexture, 0);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) Console.WriteLine("ERROR::FRAMEBUFFER:: Resolved Framebuffer is not complete!");

        // Ping-pong FBOs for bloom blur
        GL.GenFramebuffers(_bloomFbos.Length, _bloomFbos);
        GL.GenTextures(_bloomTextures.Length, _bloomTextures);
        for (int i = 0; i < 2; i++)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFbos[i]);
            GL.BindTexture(TextureTarget.Texture2D, _bloomTextures[i]);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, width / 2, height / 2, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _bloomTextures[i], 0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) Console.WriteLine($"ERROR::FRAMEBUFFER:: Bloom Framebuffer {i} is not complete!");
        }

        // --- Skia Surface for UI ---
        _skRenderTarget?.Dispose();
        _skSurface?.Dispose();

        // The default framebuffer is 0. We'll wrap it for Skia.
        // Samples and Stencil are for the main backbuffer.
        GL.GetInteger(GetPName.Samples, out int samples);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.Stencil, FramebufferParameterName.FramebufferAttachmentStencilSize, out int stencil);

        var framebufferInfo = new GRGlFramebufferInfo(0, (uint)PixelInternalFormat.Rgba8);
        _skRenderTarget = new GRBackendRenderTarget(width, height, samples, stencil, framebufferInfo);
        _skSurface = SKSurface.Create(_grContext, _skRenderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
        _uiTextService?.Cleanup();

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void OnWindowResized() { } // This is called by Engine, but we need width/height
    private void OnWindowResized(int width, int height)
    {
        _width = width;
        _height = height;
        GL.Viewport(0, 0, width, height);
        SetupFramebuffers(width, height);
    }

    public void RequestSnapshot(string path) { /* Not implemented for OpenTK */ }
    public void ProcessSnapshot() { /* Not implemented for OpenTK */ }

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

    private void DisposeFramebuffers()
    {
        GL.DeleteFramebuffer(_msaaFbo);
        GL.DeleteTexture(_msaaColorTexture);
        GL.DeleteRenderbuffer(_rboDepthStencil);
        GL.DeleteFramebuffer(_resolvedFbo);
        GL.DeleteTexture(_resolvedColorTexture);
        GL.DeleteFramebuffers(_bloomFbos.Length, _bloomFbos);
        GL.DeleteTextures(_bloomTextures.Length, _bloomTextures);
    }

    public void Dispose()
    {
        _shaderProgram?.Dispose();
        _skyboxShaderProgram?.Dispose();
        _skyboxCubeData?.Dispose();
        _brightPassShader?.Dispose();
        _blurShader?.Dispose();
        _finalCompositeShader?.Dispose();

        GL.DeleteVertexArray(_quadVao);
        GL.DeleteBuffer(_quadVbo);

        DisposeFramebuffers();

        _skSurface?.Dispose();
        _skRenderTarget?.Dispose();
        _grContext?.Dispose();
        _uiRenderer?.Cleanup();
        _uiTextService?.Cleanup();
    }
}