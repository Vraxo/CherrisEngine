using AwesomeAssertions;
using Overcore.Animation;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// The contract between the palette the CPU packs and the texture the vertex shader samples. Both halves are written
/// by hand in different languages, so the constants that tie them together are asserted here rather than left to a
/// reviewer's memory — a mismatch is a character that skin-twists into garbage with no error anywhere.
/// </summary>
public class SkinningShaderTests
{
    // ---------------------------------------------------------------- the palette constants

    [Theory]
    [InlineData("skin.vert")]
    [InlineData("scene.vert")]
    public void TheBoneTextureIsThreeTexelsPerBone_AsThePalettePacks(string shader)
    {
        // The shader computes `bone * 3`. If either side changed that stride, every bone after the first would read
        // another bone's matrix.
        Shader(shader).Should().Contain("bone * 3");
    }

    [Fact]
    public void TheSkinningShaderReconstructsTheFourthColumnAsTheAffineConstant()
    {
        // The palette drops the fourth column because an affine matrix always has (0, 0, 0, 1) there. If the shader
        // did not put it back, every vertex would be scaled to the origin by the zero.
        Shader("skin.vert").Should().Contain("vec4(0.0, 0.0, 0.0, 1.0)");
    }

    [Fact]
    public void TheSkinningShaderReadsTheBoneTextureByName()
    {
        // Matched against what the renderer looks up; a rename on either side binds nothing and samples unit 0, which
        // is the material's diffuse texture.
        Shader("skin.vert").Should().Contain("uniform sampler2D uBoneTexture");
    }

    [Fact]
    public void TheSkinningShaderBindsTheBoneAttributesTheVertexLayoutDeclares()
    {
        // The vertex layout puts bones at location 4 and weights at 5. A shader reading either from another location
        // would fetch a colour or a texcoord and skin by it.
        string shader = Shader("skin.vert");

        shader.Should().Contain("layout (location = 4) in vec4 aBones");
        shader.Should().Contain("layout (location = 5) in vec4 aWeights");
    }

    [Fact]
    public void TheStaticShaderDeclaresNoBoneAttributes()
    {
        // The static pipeline must not pay for a bone fetch it never uses, which is why the two programs are separate.
        Shader("scene.vert").Should().NotContain("aBones");
        Shader("scene.vert").Should().NotContain("uBoneTexture");
    }

    [Fact]
    public void AVerticesFourInfluencesAreBlendedByWeight()
    {
        string shader = Shader("skin.vert");

        shader.Should().Contain("aWeights.x");
        shader.Should().Contain("aWeights.y");
        shader.Should().Contain("aWeights.z");
        shader.Should().Contain("aWeights.w");
    }

    [Fact]
    public void AnUnweightedVertexTakesTheIdentityRatherThanAZeroMatrix()
    {
        // Without this a mesh with no bone data is annihilated by a zero matrix — the "imported character disappears
        // to a point" failure, moved from the importer to the shader.
        Shader("skin.vert").Should().Contain("mat4(1.0)");
    }

    // ---------------------------------------------------------------- the layout's stride

    [Fact]
    public void TheVertexStrideCoversTheFourAttributesAndTwoBoneVectors()
    {
        // The GL attribute pointers offset into the vertex by these sizes. If the struct grew and this constant did
        // not, the bone attributes would read the wrong bytes.
        Vertex.SizeInBytes.Should().Be((12 + 12 + 16 + 8) + (16 + 16));
    }

    [Fact]
    public void FourInfluencesIsWhatTheVertexCarries()
    {
        Vertex.MaxBoneInfluences.Should().Be(4);
    }

    private static string Shader(string fileName) => File.ReadAllText(Find($"src/OvercoreEditor/EditorResources/Shaders/{fileName}"));

    /// <summary>Walks up from the test binary until the repository root, the way the stylesheet tests do.</summary>
    private static string Find(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"'{relativePath}' not found above '{AppContext.BaseDirectory}'.");
    }
}
