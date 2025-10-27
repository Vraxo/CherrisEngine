using OpenTK.Graphics.OpenGL4;

namespace Cherris;

internal class OpenGLPostProcessor : IDisposable
{
    // Framebuffers and Textures
    private int _width, _height;
    private int _msaaFbo;
    private int _msaaColorTexture;
    private int _rboDepthStencil;

    private int _resolvedFbo;
    private int _resolvedColorTexture;

    private int _compositeFbo;
    private int _compositeTexture;
    public int FinalSceneTexture => _compositeTexture;

    private readonly int[] _bloomFbos = new int[2];
    private readonly int[] _bloomTextures = new int[2];

    // Quad for rendering post-processing effects
    private int _quadVao;
    private int _quadVbo;

    // Shaders
    private ShaderProgram _brightPassShader;
    private ShaderProgram _blurShader;
    private ShaderProgram _finalCompositeShader;

    public OpenGLPostProcessor()
    {
        SetupShaders();
        SetupQuad();
    }

    public void OnResize(int width, int height)
    {
        _width = width;
        _height = height;
        SetupFramebuffers(width, height);
    }

    public void BeginFrame()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
        GL.Viewport(0, 0, _width, _height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    public void ResolveMsaa()
    {
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolvedFbo);
        GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
    }

    public void RenderBloom()
    {
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
        const int amount = 10;
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
    }

    public void Composite(float exposure)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _compositeFbo);
        GL.Viewport(0, 0, _width, _height);
        GL.Clear(ClearBufferMask.ColorBufferBit);

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

        // After 10 blur iterations, the final result is in _bloomTextures[0].
        GL.BindTexture(TextureTarget.Texture2D, _bloomTextures[0]);
        GL.Uniform1(imageLoc, 0); // Sampler is still 0
        GL.Uniform1(isBloomLoc, 1); // Corresponds to 'true'
        RenderQuad();

        GL.Disable(EnableCap.Blend); // Reset blend state
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void RenderQuad()
    {
        GL.BindVertexArray(_quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
    }

    private void SetupShaders()
    {
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
        DisposeFramebuffers();

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

        // Final composite FBO for ImGui
        _compositeFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _compositeFbo);
        _compositeTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _compositeTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Srgb8Alpha8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _compositeTexture, 0);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) Console.WriteLine("ERROR::FRAMEBUFFER:: Composite Framebuffer is not complete!");

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void DisposeFramebuffers()
    {
        GL.DeleteFramebuffer(_msaaFbo);
        GL.DeleteTexture(_msaaColorTexture);
        GL.DeleteRenderbuffer(_rboDepthStencil);
        GL.DeleteFramebuffer(_resolvedFbo);
        GL.DeleteTexture(_resolvedColorTexture);
        GL.DeleteFramebuffer(_compositeFbo);
        GL.DeleteTexture(_compositeTexture);
        GL.DeleteFramebuffers(_bloomFbos.Length, _bloomFbos);
        GL.DeleteTextures(_bloomTextures.Length, _bloomTextures);
    }

    public void Dispose()
    {
        _brightPassShader?.Dispose();
        _blurShader?.Dispose();
        _finalCompositeShader?.Dispose();

        GL.DeleteVertexArray(_quadVao);
        GL.DeleteBuffer(_quadVbo);

        DisposeFramebuffers();
    }
}