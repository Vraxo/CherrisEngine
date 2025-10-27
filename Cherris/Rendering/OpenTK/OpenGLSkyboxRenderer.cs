using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Cherris;

internal class OpenGLSkyboxRenderer : IDisposable
{
    private readonly ShaderProgram _skyboxShaderProgram;
    private readonly int _skyboxViewLocation;
    private readonly int _skyboxProjectionLocation;
    private readonly int _skyboxSamplerLocation;
    private readonly OpenGLMeshRendererData _skyboxCubeData;

    public OpenGLSkyboxRenderer()
    {
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

        _skyboxCubeData = new OpenGLMeshRendererData(Mesh.CreateCube());
    }

    public void Render(Skybox skybox, Matrix4 view, Matrix4 projection)
    {
        GL.DepthFunc(DepthFunction.Lequal);
        GL.CullFace(CullFaceMode.Front);

        _skyboxShaderProgram.Use();

        var skyboxView = view;
        skyboxView.Row3 = new Vector4(0, 0, 0, 1); // Remove translation

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