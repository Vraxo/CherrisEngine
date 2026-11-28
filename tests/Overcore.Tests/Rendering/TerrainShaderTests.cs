using AwesomeAssertions;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// The terrain splat blend in the lit shaders: four layers and an alphamap, in both fragment programs —
/// which are one shader written twice, and stay that way.
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
    public void TheFragmentShaderBlendsFourTerrainLayersByTheAlphamap(string shader)
    {
        string source = Code(shader);

        source.Should().Contain("uniform int uTerrainEnabled;");
        source.Should().Contain("uniform sampler2D uTerrainAlphamap;");
        source.Should().Contain("uniform sampler2D uTerrainLayer0;");
        source.Should().Contain("uniform sampler2D uTerrainLayer3;");
        source.Should().Contain("uniform vec3 uTerrainTints[4];");
        source.Should().Contain("uniform vec2 uTerrainRepeats[4];");
        source.Should().Contain("vec3 TerrainAlbedo(vec2 uv)");
        source.Should().Contain("albedo = TerrainAlbedo(fsin_TexCoord);");
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
