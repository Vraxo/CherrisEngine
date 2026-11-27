using AwesomeAssertions;
using Overcore.Core;
using Overcore.Rendering.UI;
using Overcore.Utils;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.UI;

public class UIClipGeometryTests
{
    [Fact]
    public void RoundedRectClip_RejectsTexturePathsOutsideProjectAssets()
    {
        RoundedRectClip clip = new(new RectF(0f, 0f, 100f, 20f), 0f,
            maskTextureName: "../outside.png");

        clip.MaskTextureName.Should().BeEmpty();
        clip.Contains(new Vector2(50f, 10f)).Should().BeTrue();
    }

    [Fact]
    public void RoundedRectClip_UsesAlphaTextureForHitTesting()
    {
        string oldRoot = ProjectFiles.ProjectRoot ?? String.Empty;
        string projectRoot = Path.Combine(Path.GetTempPath(), $"overcore-alpha-mask-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        try
        {
            ProjectFiles.ProjectRoot = projectRoot;
            byte[] tga = new byte[18 + 8];
            tga[2] = 2;
            tga[12] = 2;
            tga[14] = 1;
            tga[16] = 32;
            tga[17] = 0x28;
            tga[18] = tga[19] = tga[20] = 255;
            tga[21] = 0;
            tga[22] = tga[23] = tga[24] = tga[25] = 255;
            File.WriteAllBytes(Path.Combine(projectRoot, "alpha.tga"), tga);

            RoundedRectClip clip = new(new RectF(0f, 0f, 100f, 20f), 0f,
                maskTextureName: "alpha.tga", maskThreshold: 0.5f);

            clip.Contains(new Vector2(10f, 10f)).Should().BeFalse();
            clip.Contains(new Vector2(49.9f, 10f)).Should().BeFalse();
            clip.Contains(new Vector2(50.1f, 10f)).Should().BeTrue();
            clip.Contains(new Vector2(90f, 10f)).Should().BeTrue();
        }
        finally
        {
            RoundedRectClip.ClearAlphaMaskCache();
            ProjectFiles.ProjectRoot = String.IsNullOrEmpty(oldRoot) ? null : oldRoot;
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public void ClipTriangles_IntersectsNestedRoundedMasks()
    {
        RoundedRectClip[] masks =
        [
            new(new RectF(0f, 0f, 100f, 100f), 20f),
            new(new RectF(20f, 10f, 60f, 80f), 12f)
        ];
        List<Vector2> vertices =
        [
            new(-100f, -100f), new(200f, -100f), new(200f, 200f),
            new(-100f, -100f), new(200f, 200f), new(-100f, 200f)
        ];

        UIClipGeometry.ClipTriangles(vertices, masks, point => point, Vector2.Lerp);

        vertices.Should().NotBeEmpty();
        vertices.Should().OnlyContain(point => masks.All(mask => mask.Contains(point)));
    }

    [Fact]
    public void ClipTriangles_ConstrainsImageGeometryToRoundedMask()
    {
        RoundedRectClip mask = new(new RectF(0f, 0f, 100f, 100f), 24f);
        List<Vector2> vertices =
        [
            new(-100f, -100f), new(200f, -100f), new(200f, 200f),
            new(-100f, -100f), new(200f, 200f), new(-100f, 200f)
        ];

        UIClipGeometry.ClipTriangles(vertices, [mask], point => point, Vector2.Lerp);

        vertices.Should().NotBeEmpty();
        vertices.Should().OnlyContain(point => mask.Contains(point));
        vertices.Count.Should().BeGreaterThan(6);
        vertices.Count.Should().Be(vertices.Count / 3 * 3);
    }
}
