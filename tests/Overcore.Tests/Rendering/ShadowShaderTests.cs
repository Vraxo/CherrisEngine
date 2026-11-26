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

    /// <summary>
    /// The shader with its <c>//</c> comments removed, for the assertions that claim something is <em>absent</em>.
    /// </summary>
    /// <remarks>
    /// A negative assertion against the raw text is an assertion about the prose too: the receiver's comment explains
    /// that it does not use a <c>sampler2DArrayShadow</c>, and that sentence alone made the check fail on a shader that
    /// was correct. Comments are where the reasoning lives, so they must not be able to fail a test that is about code.
    /// </remarks>
    private static string Code(string name)
    {
        IEnumerable<string> lines = Read(name)
            .Split('\n')
            .Select(line =>
            {
                int comment = line.IndexOf("//", StringComparison.Ordinal);

                return comment >= 0 ? line[..comment] : line;
            });

        return string.Join('\n', lines);
    }

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
        string shader = Code("shadow.vert");

        shader.Should().NotContain("layout (location = 4)");
        shader.Should().NotContain("uBoneTexture");
    }

    [Fact]
    public void TheDepthStagesWriteNoColour()
    {
        // The framebuffer has no colour attachment, so an output here would be a shader that links but writes nowhere.
        foreach (string shader in new[] { "shadow.vert", "shadow_skinned.vert" })
        {
            Code(shader).Should().NotContain("out vec4");
        }
    }

    [Fact]
    public void TheFragmentStageIsEmpty()
    {
        string shader = Code("shadow.frag");

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
        string source = Code(shader);

        source.Should().Contain("uniform sampler2DArray uShadowMap;");
        source.Should().NotContain("sampler2DArrayShadow", "a comparison sampler's mode is fixed at compile time, so Hard and Soft could not share a program");
    }

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheReceiverShadowsTheDirectionalLightAndNothingElse(string shader)
    {
        // The ambient is deliberately untouched: a shadow that dimmed the ambient would make a corner go black rather
        // than shaded. Only CalcDirLight is multiplied by the shadow term.
        string source = Code(shader);

        source.Should().Contain("CalcDirLight(uDirLight, norm, viewDir, albedo) * shadow;");
        source.Should().NotContain("CalcPointLight(uPointLights[i], norm, FragPos, viewDir, albedo) * shadow");
        source.Should().NotContain("CalcSpotLight(uSpotLights[i], norm, FragPos, viewDir, albedo) * shadow");
    }

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheNormalOffsetUsesTheFragmentsOwnCascadeTexel(string shader)
    {
        // Uploading the tan of four texel sizes and then reading only `.x` would make the normal offset 20× too small
        // in the outermost cascade — 0.013 m against 0.26 m at the defaults — and the outer cascade is the one big
        // enough to show acne. A constant index is legal on a vec4, and it is also legal on a sampler array, so the
        // absence of `uShadowTexelSize.x` is the assertion.
        string source = Code(shader);

        source.Should().Contain("uShadowTexelSize[cascade]");
        source.Should().NotContain("uShadowTexelSize.x");
    }

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheReceiverHonoursTheLightsNearPlane(string shader)
    {
        // Light.ShadowNearPlane is a per-light setting, declared and serialized. It was uploaded from the first
        // cascade's *range* instead, which made the setting inert — the kind of inert that looks like a feature.
        Code(shader).Should().Contain("depth < uShadowNearPlane");
    }

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheReceiverRemapsTheDepthIntoTheSpaceTheTextureHolds(string shader)
    {
        // The one bug in this layer that is invisible: the fit's projection emits NDC depth in [0, 1] and OpenGL stores
        // (ndc + 1) / 2, so a receiver comparing raw NDC against the texture finds every fragment lit. Shadowed and lit
        // are the only two answers, so there is nothing half-working to notice.
        string source = Code(shader);

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

    // ---------------------------------------------------------------- the cascade, and the order it is found in

    [Theory]
    [InlineData("scene.frag")]
    [InlineData("skin.frag")]
    public void TheCascadeIsChosenBeforeItIsIndexed(string shader)
    {
        // GLSL needs a declaration above the statement that reads it, and `uShadowTexelSize[cascade]` was written
        // above `int cascade = 0;`. Every C# test in this file passed, the build passed, and the shader did not
        // compile — the editor loaded it, failed, and drew nothing. A whole-program compile is the only check that
        // catches this, which is what CI's *Validate shaders* step is for; this test exists so the reason survives in
        // the file somebody will edit next.
        string source = Code(shader);

        int declared = source.IndexOf("int cascade = 0;", StringComparison.Ordinal);
        int texel = source.IndexOf("uShadowTexelSize[cascade]", StringComparison.Ordinal);

        declared.Should().BeGreaterThan(0);
        texel.Should().BeGreaterThan(0);
        declared.Should().BeLessThan(texel, "the index has to exist before the array is subscripted with it");
    }

    [Fact]
    public void TheShadersTheRendererLinksAreTheShadersCiCompiles()
    {
        // The *Validate shaders* step lists the programs by name, because a pair is what a renderer asks for and a
        // directory full of stages says nothing about which two belong together. That list is a second copy of the
        // truth, and a second copy drifts: the check is only worth running if it covers the pair somebody just added.
        //
        // Both sides are read as text rather than reflected over, since the renderers' file names are string literals
        // and the workflow is a YAML file this project has no reason to parse.
        string workflow = ReadRepositoryFile(".github", "workflows", "ci.yml");

        // The list is the body of a `while read … done <<'PAIRS'` heredoc, so it is bounded by the opening tag and a
        // line that holds nothing but the closing one. Anchoring on the tag and not on the bare word matters: the
        // closing delimiter is the bare word, and taking the first match swallowed the shell below it.
        const string opening = "<<'PAIRS'";
        const string closing = "PAIRS";

        int tag = workflow.IndexOf(opening, StringComparison.Ordinal);

        tag.Should().BeGreaterThan(0, "the validate step reads its pair list from a heredoc tagged PAIRS");

        string[] lines = workflow[(tag + opening.Length)..].Split('\n');

        int close = Array.FindIndex(lines, line => line.Trim() == closing);

        close.Should().BeGreaterThan(0);

        HashSet<string> compiled = [.. lines[..close]
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.Replace(" ", "", StringComparison.Ordinal))];

        HashSet<string> linked = [.. RendererSource()
            .SelectMany(source => ShaderNames(source))
            .Chunk(2)
            .Where(pair => pair.Length == 2)
            .Select(pair => pair[0] + pair[1])];

        linked.Should().NotBeEmpty("the renderers are where the pairs come from");
        compiled.Should().BeEquivalentTo(linked);
    }

    /// <summary>
    /// The vertex/fragment file names each renderer asks for, in the order it asks for them: <c>FromFiles(vert, frag)</c>
    /// for the ones that build a program from a pair, and the constructor's first two arguments for
    /// <c>OpenGLSceneRenderer</c>'s <c>Program</c>.
    /// </summary>
    private static IEnumerable<string> ShaderNames(string source)
    {
        // The shadow renderer loads `shadow.frag` twice against two different vertex stages, which is two programs and
        // has to stay two entries; so this walks the calls rather than collecting a set of names.
        foreach (string line in source.Split('\n'))
        {
            if (!line.Contains("Shaders/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string name in line.Split('"'))
            {
                if (name.StartsWith("Shaders/", StringComparison.Ordinal))
                {
                    yield return name["Shaders/".Length..];
                }
            }
        }
    }

    private static IEnumerable<string> RendererSource()
    {
        // Shaders/ -> EditorResources -> OvercoreEditor -> src, then down into the engine's OpenTK backend.
        string directory = Path.GetFullPath(Path.Combine(
            ShaderDirectory(),
            "..", "..", "..",
            "Overcore", "Rendering", "OpenTK"));

        Directory.Exists(directory).Should().BeTrue($"the OpenTK backend should be at {directory}");
        Directory.EnumerateFiles(directory, "*.cs").Should().NotBeEmpty();

        return Directory
            .EnumerateFiles(directory, "*.cs")
            .Where(file => File.ReadAllText(file).Contains("Shaders/", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();
    }

    /// <summary>A file from the repository root, found the same way the shader directory is.</summary>
    private static string ReadRepositoryFile(params string[] parts)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = directory.FullName;

            foreach (string part in parts)
            {
                candidate = Path.Combine(candidate, part);
            }

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(parts)} from the test binary.");
    }
}
