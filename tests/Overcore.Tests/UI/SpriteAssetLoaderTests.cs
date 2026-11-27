using AwesomeAssertions;
using Overcore.Rendering;
using System.Text;
using Xunit;

namespace Overcore.Tests.UI;

public class SpriteAssetLoaderTests
{
    [Fact]
    public void Parse_ConvertsPixelRectAndBordersToNormalizedUvAndSliceMetadata()
    {
        const string json = """
            {
              "texture": "Assets/UI/atlas.png",
              "textureSize": [256, 128],
              "rect": [32, 16, 64, 32],
              "pivot": [0.25, 0.75],
              "pixelsPerUnit": 100,
              "border": [8, 4, 16, 12]
            }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        SpriteMetadata sprite = SpriteAssetLoader.Parse(stream)!;

        sprite.TextureName.Should().Be("Assets/UI/atlas.png");
        sprite.UVMin.X.Should().BeApproximately(0.125f, 0.0001f);
        sprite.UVMin.Y.Should().BeApproximately(0.625f, 0.0001f);
        sprite.UVMax.X.Should().BeApproximately(0.375f, 0.0001f);
        sprite.UVMax.Y.Should().BeApproximately(0.875f, 0.0001f);
        sprite.Pivot.X.Should().Be(0.25f);
        sprite.Pivot.Y.Should().Be(0.75f);
        sprite.PixelsPerUnit.Should().Be(100f);
        sprite.SliceLeft.Should().Be(0.125f);
        sprite.SliceTop.Should().Be(0.125f);
        sprite.SliceRight.Should().Be(0.25f);
        sprite.SliceBottom.Should().Be(0.375f);
    }

    [Fact]
    public void Resolve_UsesDirectImageFieldsWhenSpriteReferenceIsBlank()
    {
        Overcore.Components.Image image = new()
        {
            TextureName = "Assets/UI/direct.png",
            UVMin = new System.Numerics.Vector2(0.2f, 0.3f),
            UVMax = new System.Numerics.Vector2(0.7f, 0.9f),
            SliceLeft = 0.1f
        };

        ResolvedSprite resolved = SpriteAssetLoader.Resolve(image);

        resolved.TextureName.Should().Be(image.TextureName);
        resolved.UVMin.Should().Be(image.UVMin);
        resolved.UVMax.Should().Be(image.UVMax);
        resolved.SliceLeft.Should().Be(0.1f);
    }

    [Fact]
    public void Parse_RejectsOutOfTextureRectsAndInvalidBorders()
    {
        const string json = """
            { "texture": "atlas.png", "textureSize": [16, 16], "rect": [8, 8, 12, 12], "border": [10, 0, 10, 0] }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        SpriteAssetLoader.Parse(stream).Should().BeNull();
    }
}
