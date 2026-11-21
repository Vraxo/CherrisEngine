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
    private readonly OpenGLMeshRendererData _cubeData;

    public OpenGLSkyboxRenderer()
    {
        _shader = ShaderProgram.FromFiles("Shaders/skybox.vert", "Shaders/skybox.frag")
                 ?? throw new InvalidOperationException("Failed to load skybox shaders.");

        _viewLoc = _shader.GetUniformLocation("view");
        _projLoc = _shader.GetUniformLocation("projection");
        _samplerLoc = _shader.GetUniformLocation("skybox");

        _cubeData = new OpenGLMeshRendererData(Mesh.CreateCube());
    }

    [Obsolete]
    public void Render(Skybox skybox, Matrix4 view, Matrix4 projection)
    {
        GL.DepthFunc(DepthFunction.Lequal);
        GL.CullFace(CullFaceMode.Front);

        _shader.Use();

        // Remove translation from view matrix (skybox stays at origin)
        var skyboxView = view;
        skyboxView.Row3 = new Vector4(0, 0, 0, 1);

        GL.UniformMatrix4(_viewLoc, false, ref skyboxView);
        GL.UniformMatrix4(_projLoc, false, ref projection);

        if (skybox.CubeMapTexture is OpenTKTexture glSkyboxTexture)
        {
            glSkyboxTexture.Bind(TextureUnit.Texture0);
            GL.Uniform1(_samplerLoc, 0);
        }

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