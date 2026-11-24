using Cherris.Components;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Cherris.Rendering.OpenTK;

internal class OpenGLSkyboxRenderer : IDisposable
{
    private readonly ShaderProgram _shader;
    private readonly int _viewLoc;
    private readonly int _projLoc;
    private readonly int _samplerLoc;
    private readonly int _hasSkyboxLoc;
    private readonly int _skyZenithLoc;
    private readonly int _skyHorizonLoc;
    private readonly int _skyGroundLoc;
    private readonly OpenGLMeshRendererData _cubeData;

    /// <summary>The sky above the horizon when a scene has no skybox of its own.</summary>
    public static Vector3 SkyZenith { get; } = new(0.070f, 0.090f, 0.140f);

    /// <summary>The band the horizon sits in when a scene has no skybox of its own.</summary>
    public static Vector3 SkyHorizon { get; } = new(0.200f, 0.205f, 0.220f);

    /// <summary>The sky below the horizon when a scene has no skybox of its own.</summary>
    public static Vector3 SkyGround { get; } = new(0.050f, 0.050f, 0.055f);

    public OpenGLSkyboxRenderer()
    {
        _shader = ShaderProgram.FromFiles("Shaders/skybox.vert", "Shaders/skybox.frag")
                 ?? throw new InvalidOperationException("Failed to load skybox shaders.");

        _viewLoc = _shader.GetUniformLocation("view");
        _projLoc = _shader.GetUniformLocation("projection");
        _samplerLoc = _shader.GetUniformLocation("skybox");
        _hasSkyboxLoc = _shader.GetUniformLocation("uHasSkybox");
        _skyZenithLoc = _shader.GetUniformLocation("uSkyZenith");
        _skyHorizonLoc = _shader.GetUniformLocation("uSkyHorizon");
        _skyGroundLoc = _shader.GetUniformLocation("uSkyGround");

        _cubeData = new OpenGLMeshRendererData(Mesh.CreateCube());
    }

    /// <summary>
    /// Draws the background a scene is seen against: its skybox when it has one, the engine's own gradient when it
    /// does not, so the picture behind the scene is never just whatever the framebuffer was cleared to.
    /// </summary>
    [Obsolete]
    public void Render(Skybox? skybox, Matrix4 view, Matrix4 projection)
    {
        GL.DepthFunc(DepthFunction.Lequal);
        GL.CullFace(CullFaceMode.Front);

        _shader.Use();

        // Remove translation from view matrix (skybox stays at origin)
        var skyboxView = view;
        skyboxView.Row3 = new Vector4(0, 0, 0, 1);

        GL.UniformMatrix4(_viewLoc, false, ref skyboxView);
        GL.UniformMatrix4(_projLoc, false, ref projection);

        // A cube map that only some backends can bind is not a sky either: the gradient is drawn unless the texture
        // is one this renderer can actually sample.
        ITexture? cubeMap = skybox?.CubeMapTexture;

        bool hasSkybox = cubeMap is OpenTKTexture;

        if (hasSkybox)
        {
            ((OpenTKTexture)cubeMap!).Bind(TextureUnit.Texture0);
            GL.Uniform1(_samplerLoc, 0);
        }

        GL.Uniform1(_hasSkyboxLoc, hasSkybox ? 1 : 0);
        GL.Uniform3(_skyZenithLoc, SkyZenith);
        GL.Uniform3(_skyHorizonLoc, SkyHorizon);
        GL.Uniform3(_skyGroundLoc, SkyGround);

        GL.BindVertexArray(_cubeData.VaoHandle);
        GL.DrawElements(PrimitiveType.Triangles, _cubeData.IndexCount, DrawElementsType.UnsignedShort, 0);

        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.TextureCubeMap, 0);

        // Restore default state
        GL.CullFace(CullFaceMode.Back);
        GL.DepthFunc(DepthFunction.Less);
    }

    public void Dispose()
    {
        _shader?.Dispose();
        _cubeData?.Dispose();
    }
}