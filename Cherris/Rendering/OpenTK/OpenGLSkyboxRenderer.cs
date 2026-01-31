using Cherris.Components;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Cherris.Rendering.OpenTK;

internal class OpenGLSkyboxRenderer : IDisposable
{
    private readonly ShaderProgram _skyboxShaderProgram;
    private readonly int _skyboxViewLocation;
    private readonly int _skyboxProjectionLocation;
    private readonly int _skyboxSamplerLocation;
    private readonly OpenGLMeshRendererData _skyboxCubeData;

    public OpenGLSkyboxRenderer()
    {
        _skyboxShaderProgram = ShaderProgram.FromFiles("Shaders/skybox.vert", "Shaders/skybox.frag");
        _skyboxViewLocation = _skyboxShaderProgram.GetUniformLocation("view");
        _skyboxProjectionLocation = _skyboxShaderProgram.GetUniformLocation("projection");
        _skyboxSamplerLocation = _skyboxShaderProgram.GetUniformLocation("skybox");

        _skyboxCubeData = new OpenGLMeshRendererData(Mesh.CreateCube());
    }

    [Obsolete]
    public void Render(Skybox skybox, Matrix4 view, Matrix4 projection)
    {
        GL.DepthFunc(DepthFunction.Lequal);
        GL.CullFace(CullFaceMode.Front);

        _skyboxShaderProgram.Use();

        var skyboxView = view;
        skyboxView.Row3 = new Vector4(0, 0, 0, 1);

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

    public void Dispose()
    {
        _skyboxShaderProgram?.Dispose();
        _skyboxCubeData?.Dispose();
    }
}