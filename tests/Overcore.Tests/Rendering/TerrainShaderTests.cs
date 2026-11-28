using AwesomeAssertions;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// The terrain splat blend in the lit shaders: eight layers and two alphamaps, in both fragment
/// programs — which are one shader written twice, and stay that way.
/// </summary>
/// <remarks>
/// Read from the repository rather than the build output, for the reason <see cref="ShadowShaderTests"/>
/// gives: a comparison against a copied-out shader would pass on a stale copy.
/// </remarks>
public class TerrainShaderTests
{
    private static string ShaderDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src",
                "OvercoreEditor",
                "EditorResources",
                "Shaders");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not find src/OvercoreEditor/EditorResources/Shaders from the test binary.");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(ShaderDirectory(), name));

    private static string Code(string name) =>
        string.Join('\n', Read(name)
            .Split('\n')
            .Select(line =>
            {
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                return comment >= 0 ? line[..comment] : line;
            }));

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheFragmentShaderBlendsEightTerrainLayersByTheAlphamaps(string shader)
    {
        string source = Code(shader);

        source.Should().Contain("uniform int uTerrainEnabled;");
        source.Should().Contain("uniform sampler2D uTerrainAlphamap;");
        source.Should().Contain("uniform sampler2D uTerrainAlphamap2;");
        source.Should().Contain("uniform sampler2D uTerrainLayer0;");
        source.Should().Contain("uniform sampler2D uTerrainLayer3;");
        source.Should().Contain("uniform sampler2D uTerrainLayer7;");
        source.Should().Contain("uniform vec3 uTerrainTints[8];");
        source.Should().Contain("uniform vec2 uTerrainRepeats[8];");
        source.Should().Contain("uTerrainLayer7").And.Contain("weights2.a",
            "the second alphamap carries layers four to seven, its alpha channel the eighth layer");
        source.Should().Contain("vec3 TerrainAlbedo(vec2 uv)");
        source.Should().Contain("albedo = TerrainAlbedo(fsin_TexCoord);");
    }

    [Fact]
    public void TheVeldridFragmentShaderBlendsTheSameEightLayers()
    {
        // The Veldrid pipeline's fragment shader is an inline string in SceneRenderer, not a file; the
        // terrain branch is the same blend in that backend's dress, and parity is a test, not a hope.
        string source = ReadSceneRenderer();

        source.Should().Contain("uniform TerrainProperties");
        source.Should().Contain("uniform texture2D TerrainAlphamap;");
        source.Should().Contain("uniform texture2D TerrainAlphamap2;");
        source.Should().Contain("uniform texture2D TerrainLayer7;");
        source.Should().Contain("vec3 TerrainAlbedo(vec2 uv)");
        source.Should().Contain("baseColor = vec4(TerrainAlbedo(fsin_TexCoord), 1.0);");
        source.Should().Contain("PackTerrainProperties",
            "the C# packer and the GLSL block are one layout stated twice, kept side by side");
    }

    private static string ReadSceneRenderer()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Overcore", "Rendering", "SceneRenderer.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new DirectoryNotFoundException("Could not find src/Overcore/Rendering/SceneRenderer.cs from the test binary.");
    }

    [Fact]
    public void TheVertexShadersBendDetailInTheWind()
    {
        // The OpenGL static vertex shader and the Veldrid one bend the same way: by the square of the
        // height up the card, and only when the draw names a wind strength.
        string gl = Read("scene.vert");
        gl.Should().Contain("uniform float uWindStrength;");
        gl.Should().Contain("aTexCoord.y * aTexCoord.y");

        string veldrid = ReadSceneRenderer();
        veldrid.Should().Contain("uniform TimeBuffer { float Time; float WindStrength; }");
        veldrid.Should().Contain("TexCoord.y * TexCoord.y");
    }

    [Fact]
    public void TheTwoFragmentShadersStayOneShaderWrittenTwice()
    {
        // The skinning program's fragment stage is the scene's, copied because GLSL 330 has no include.
        // The terrain branch is part of that copy, so it must land in both or a skinned draw and a static
        // draw of the same ground would disagree.
        Read("scene.frag").Should().Be(Read("skin.frag"));
    }
}
