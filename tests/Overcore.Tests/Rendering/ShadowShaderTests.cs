using AwesomeAssertions;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// The depth shaders against the ones they were copied from, and against the C# that feeds them.
/// </summary>
/// <remarks>
/// <para>
/// <c>shadow_skinned.vert</c> restates <c>skin.vert</c>'s palette arithmetic because GLSL 330 has no include. A copy is
/// the right trade — a shader assembled by string substitution is unreadable in the repository — but it is only right
/// as long as it stays a copy, and nothing about editing one file tells you the other exists.
/// </para>
/// <para>
/// Read from the repository rather than from the build output: the test project does not carry the editor's content
/// files, and a comparison against a copied-out shader would pass on a stale copy.
/// </para>
/// </remarks>
public class ShadowShaderTests
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

    // ---------------------------------------------------------------- the palette constants

    [Theory]
    [InlineData("skin.vert")]
    [InlineData("shadow_skinned.vert")]
    public void ThePaletteIsThreeTexelsPerBone(string shader)
    {
        // Three texels a bone is three RGBA32F columns of an affine matrix. If one stage read a different stride its
        // character would still light correctly and its shadow would trail behind it.
        Read(shader).Should().Contain("bone * 3");
    }

    [Theory]
    [InlineData("skin.vert")]
    [InlineData("shadow_skinned.vert")]
    public void AnUnweightedVertexTakesTheIdentity(string shader)
    {
        // A zero matrix annihilates the vertex, and a vertex that vanishes in the depth pass does so silently: the
        // whole mesh reads as unlit rather than as broken.
        Read(shader).Should().Contain("total <= 0.0");
        Read(shader).Should().Contain("mat4(1.0)");
    }

    [Theory]
    [InlineData("skin.vert")]
    [InlineData("shadow_skinned.vert")]
    public void TheFourthColumnIsReconstructed(string shader)
    {
        Read(shader).Should().Contain("vec4(0.0, 0.0, 0.0, 1.0)");
    }

    [Fact]
    public void BothSkinnedStagesNormaliseTheWeights()
    {
        Read("skin.vert").Should().Contain("skin / total");
        Read("shadow_skinned.vert").Should().Contain("skin / total");
    }

    [Fact]
    public void BothSkinnedStagesApplyTheMatrixTheSameWay()
    {
        // The palette is uploaded for a row-vector multiply, so the matrix is on the right of the position. Written
        // the other way round the transform is the transpose, which on a T-posed character looks plausible and comes
        // apart the moment it turns.
        Read("skin.vert").Should().Contain("vec4(aPosition, 1.0) * skin");
        Read("shadow_skinned.vert").Should().Contain("vec4(aPosition, 1.0) * SkinMatrix()");
    }

    // ---------------------------------------------------------------- the depth stages

    [Fact]
    public void TheStaticDepthStageComposesTheLightMatrixWithTheModel()
    {
        // lightViewProjection * model, in that order: the model takes the vertex into the world and the light matrix
        // is defined over world space.
        Read("shadow.vert").Should().Contain("lightViewProjection * model * vec4(aPosition, 1.0)");
    }

    [Fact]
    public void TheStaticDepthStageDeclaresNoSkinningInputs()
    {
        // Deliberately absent: a static mesh pays nothing for a layout it never reads, which is the same reason the
        // colour pass has two programs rather than a branch.
        string shader = Read("shadow.vert");

        shader.Should().NotContain("layout (location = 4)");
        shader.Should().NotContain("uBoneTexture");
    }

    [Fact]
    public void TheDepthStagesWriteNoColour()
    {
        // The framebuffer has no colour attachment, so an output here would be a shader that links but writes nowhere.
        foreach (string shader in new[] { "shadow.vert", "shadow_skinned.vert" })
        {
            Read(shader).Should().NotContain("out vec4");
        }
    }

    [Fact]
    public void TheFragmentStageIsEmpty()
    {
        string shader = Read("shadow.frag");

        shader.Should().Contain("void main()");
        shader.Should().NotContain("gl_FragDepth", "the rasteriser's depth is the one that is wanted");
    }

    // ---------------------------------------------------------------- the receiver

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheReceiverSamplesAnArrayRatherThanAnArrayOfSamplers(string shader)
    {
        // GLSL 330 indexes an array of samplers only by a constant expression, so a run-time cascade index can only be
        // a layer of one texture. This is the constraint the whole storage choice is downstream of.
        string source = Read(shader);

        source.Should().Contain("uniform sampler2DArray uShadowMap;");
        source.Should().NotContain("sampler2DArrayShadow");
    }

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheReceiverShadowsTheDirectionalLightAndNothingElse(string shader)
    {
        // The ambient is deliberately untouched: a shadow that dimmed the ambient would make a corner go black rather
        // than shaded. Only CalcDirLight is multiplied by the shadow term.
        string source = Read(shader);

        source.Should().Contain("CalcDirLight(uDirLight, norm, viewDir, albedo) * shadow;");
        source.Should().NotContain("CalcPointLight(uPointLights[i], norm, FragPos, viewDir, albedo) * shadow");
        source.Should().NotContain("CalcSpotLight(uSpotLights[i], norm, FragPos, viewDir, albedo) * shadow");
    }

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheReceiverRemapsTheDepthIntoTheSpaceTheTextureHolds(string shader)
    {
        // The one bug in this layer that is invisible: the fit's projection emits NDC depth in [0, 1] and OpenGL stores
        // (ndc + 1) / 2, so a receiver comparing raw NDC against the texture finds every fragment lit. Shadowed and lit
        // are the only two answers, so there is nothing half-working to notice.
        string source = Read(shader);

        source.Should().Contain("float storedDepth = (projected.z + 1.0) * 0.5;");
        source.Should().Contain("step(storedDepth - bias");
        source.Should().NotContain("step(projected.z - bias");
    }

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheReceiverTakesTheCascadeFromTheViewDepthRatherThanTheClipDepth(string shader)
    {
        // gl_FragCoord.z is non-linear in a perspective projection, so comparing it against an orthographic map's
        // linear depth is meaningless. The view depth is a dot product against the eye's forward axis.
        Read(shader).Should().Contain("dot(worldPos - uViewPos, uCameraForward)");
    }

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheTwoReceiversAgreeEverywhereButTheirVaryings(string shader)
    {
        // The shadow block is copied into both stages for the same reason the palette is: two programs, one lighting
        // model. Copying is only safe while the copies match, so a change to one is a failure of this assertion rather
        // than a difference nobody notices until a character's shadow is filtered differently from a wall's.
        string body = Read(shader);

        int start = body.IndexOf("float CascadeVisibility", StringComparison.Ordinal);
        int end = body.IndexOf("void main()", StringComparison.Ordinal);

        start.Should().BeGreaterThan(0);
        end.Should().BeGreaterThan(start);

        string receiver = body[start..end];

        // Everything the function needs, asserted by name so a whole-block comparison is not defeated by whitespace.
        foreach (string token in new[]
        {
            "uShadowMatrices[cascade]",
            "uShadowSoftness",
            "uShadowNormalBias",
            "uShadowBias",
            "uCascadeSplits[cascade]",
            "uShadowBlendBand",
            "uShadowStrength",
            "textureSize(uShadowMap, 0)"
        })
        {
            receiver.Should().Contain(token);
        }
    }
}
