using AwesomeAssertions;
using Overcore.Core;
using Overcore.Rendering;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// The cutout rules that hold on the CPU side, and the shader text that holds the other half of them.
/// </summary>
/// <remarks>
/// <para>
/// A cutout is two discards on one threshold: the colour pass throws away the holes so the fence looks like a fence, and
/// the depth pass throws away the same holes so its shadow is a fence-shaped pattern rather than a solid rectangle.
/// The failure this file guards against is the two disagreeing — a fence that is see-through but casts a wall, which
/// reads as a bug in the shadows rather than as a missing setting.
/// </para>
/// <para>
/// Nothing here draws. What can be pinned without a GPU is that the value exists, that it defaults to off, that it
/// survives a save, and that all three fragment stages compare it the same way — the parts that failures actually
/// come from.
/// </para>
/// </remarks>
public class AlphaCutoutTests
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

    /// <summary>The shader with its comments removed, for assertions about code rather than about prose.</summary>
    private static string Code(string name) => string.Join(
        '\n',
        Read(name).Split('\n').Select(line =>
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);

            return comment >= 0 ? line[..comment] : line;
        }));

    // ---------------------------------------------------------------- the setting

    [Fact]
    public void AMaterialIsOpaqueUntilItSaysOtherwise()
    {
        // Zero disables the test, and it is the default because every material that existed before this setting must
        // keep drawing exactly as it did. A non-zero default would have made every existing texture with a stray alpha
        // channel start showing holes.
        new Material(new FakeTexture(), "White").AlphaCutoff.Should().Be(0f);
    }

    [Fact]
    public void TheCutoffIsAClosedUnitRange()
    {
        // It is compared against an alpha, so a value above 1 would discard every fragment including an opaque one —
        // a material that vanishes entirely — and a value below 0 would be the same as 0 with no way to tell.
        var range = typeof(Material)
            .GetProperty(nameof(Material.AlphaCutoff))!
            .GetCustomAttributes(typeof(Overcore.Attributes.RangeAttribute), false)
            .Cast<Overcore.Attributes.RangeAttribute>()
            .Single();

        range.Min.Should().Be(0f);
        range.Max.Should().Be(1f);
    }

    // ---------------------------------------------------------------- the shaders agree

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    [InlineData("shadow.frag")]
    public void EveryFragmentStageDiscardsBelowTheSameThreshold(string shader)
    {
        string source = Code(shader);

        source.Should().Contain("< uAlphaCutoff");
        source.Should().Contain("discard");
    }

    [Fact]
    public void TheColourPassWeighsTheVertexColourIntoTheCutout()
    {
        // The vertex colour's alpha is the other half of a surface's opacity, and the colour pass already multiplies
        // the output alpha by it. A test that ignored it would discard by texture alone and disagree with itself at the
        // output line — which is the kind of disagreement that shows up as a rim of half-drawn pixels.
        Code("scene.frag").Should().Contain("diffuse.a * fsin_Color.a < uAlphaCutoff");

        // The depth pass has no vertex colour to weigh — it declares locations 0 and 3 only — and that is a stated
        // gap: a caster whose opacity comes from vertex alpha rather than from its texture casts its full quad.
        Code("shadow.frag").Should().Contain("texture(uDiffuse, fsin_TexCoord).a < uAlphaCutoff");
    }

    [Theory]
    [InlineData("shadow.vert")]
    [InlineData("shadow_skinned.vert")]
    public void TheDepthVertexStageCarriesTheTexcoordTheCutoutNeeds(string shader)
    {
        // Without the varying the fragment stage samples the same texel for every fragment of the caster, which
        // discards either all of it or none of it — a fence whose shadow is a solid quad or no shadow at all.
        string source = Code(shader);

        source.Should().Contain("fsin_TexCoord = aTexCoord * uTiling;");
        source.Should().Contain("out vec2 fsin_TexCoord;");
    }

    [Fact]
    public void TheStaticDepthStageDeclaresTheTexcoordAttribute()
    {
        // It is the only attribute the static depth stage gained, and it is the one the varying is built from.
        Code("shadow.vert").Should().Contain("layout (location = 3) in vec2 aTexCoord;");
    }

    [Fact]
    public void TheDepthStagesStillWriteNoColour()
    {
        // The depth framebuffer has no colour attachment. A `discard` is not a write, so the cutout costs the pass
        // nothing it was not already allowed to do.
        foreach (string shader in new[] { "shadow.vert", "shadow_skinned.vert", "shadow.frag" })
        {
            Code(shader).Should().NotContain("out vec4");
        }
    }

    /// <summary>A texture with no backend, which is all these rules ever ask of one.</summary>
    private sealed class FakeTexture : ITexture
    {
        public object GetBackendHandle() => new object();

        public void Dispose() { }
    }
}
