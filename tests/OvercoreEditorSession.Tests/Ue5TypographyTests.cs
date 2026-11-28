using AwesomeAssertions;
using Overcore.UI;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Guards the UE5 typography: the point-to-pixel sizes the engine implies, the fonts that are shipped, and
/// the stylesheet that has to use them.
/// </summary>
public class Ue5TypographyTests
{
    [Fact]
    public void PointSizes_AreTheEngines()
    {
        // FStarshipCoreStyle::FStyleFonts
        Ue5Typography.NormalPointSize.Should().Be(10f);
        Ue5Typography.SmallPointSize.Should().Be(8f);
        Ue5Typography.LargePointSize.Should().Be(14f);
        Ue5Typography.HeadingExtraSmallPointSize.Should().Be(15f);
        Ue5Typography.HeadingSmallPointSize.Should().Be(21f);
        Ue5Typography.HeadingMediumPointSize.Should().Be(33f);
    }

    [Fact]
    public void ToPixels_ConvertsAtTheReferenceDpi()
    {
        Ue5Typography.ToPixels(10f).Should().BeApproximately(13.33f, 0.01f);
        Ue5Typography.ToPixels(8f).Should().BeApproximately(10.67f, 0.01f);
        Ue5Typography.ToPixels(14f).Should().BeApproximately(18.67f, 0.01f);

        // The pixel sizes the UI actually uses are those rounded to whole pixels.
        Ue5Typography.NormalPixels.Should().Be(13f);
        Ue5Typography.SmallPixels.Should().Be(11f);
        Ue5Typography.LargePixels.Should().Be(19f);
    }

    [Fact]
    public void FontFiles_AreShipped()
    {
        string repositoryRoot = FindRepositoryRoot();

        string[] required =
        [
            Path.Combine(repositoryRoot, "src", "OvercoreEditor", "EditorResources", "Fonts", "Roboto-Regular.TTF"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditor", "EditorResources", "Fonts", "Inter-Regular.TTF"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditorPhotino", "wwwroot", "fonts", "inter-latin-400-normal.woff2"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditorPhotino", "wwwroot", "fonts", "Inter-OFL.txt"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditor", "EditorResources", "Fonts", "Inter-LICENSE.txt"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditorPhotino", "wwwroot", "fonts", "roboto-latin-400-normal.woff2"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditorPhotino", "wwwroot", "fonts", "roboto-latin-700-normal.woff2"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditorPhotino", "wwwroot", "fonts", "roboto-mono-latin-400-normal.woff2"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditorPhotino", "wwwroot", "fonts", "Roboto-OFL.txt"),
            Path.Combine(repositoryRoot, "src", "OvercoreEditor", "EditorResources", "Fonts", "Roboto-LICENSE.txt")
        ];

        foreach (string path in required)
        {
            File.Exists(path).Should().BeTrue($"the UE5 theme needs '{Path.GetFileName(path)}' in the repository");
        }

        // A TTF starts with 0x00010000 (or "true"); make sure the shipped file is a real font and not a stub.
        byte[] header = new byte[4];
        using (FileStream stream = File.OpenRead(required[0]))
        {
            stream.ReadExactly(header);
        }

        header.Should().Equal(0x00, 0x01, 0x00, 0x00);
    }

    [Fact]
    public void Stylesheet_LoadsRobotoAndUsesTheEngineSizes()
    {
        string stylesheet = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "src", "OvercoreEditorPhotino", "wwwroot", "css", "theme.css"));

        // One @font-face per shipped file: four Roboto subsets at 400, latin at 700, mono, and the same
        // five for Inter, which the Unity theme asks for.
        stylesheet.Split("@font-face").Length.Should().Be(12);
        stylesheet.Should().Contain("url(\"../fonts/roboto-latin-400-normal.woff2\")");
        stylesheet.Should().Contain("url(\"../fonts/roboto-mono-latin-400-normal.woff2\")");
        stylesheet.Should().Contain("url(\"../fonts/inter-latin-400-normal.woff2\")");
        stylesheet.Should().Contain("url(\"../fonts/inter-latin-600-normal.woff2\")");

        // The type is a theme variable, defaulting to Roboto at the engine's 10pt (13px).
        stylesheet.Should().Contain($"--theme-font-ui: \"{Ue5Typography.Family}\", \"Segoe UI\", system-ui, sans-serif;");
        stylesheet.Should().Contain($"--theme-font-size: {Ue5Typography.NormalPixels:0}px;");
        stylesheet.Should().Contain($"--theme-font-mono: \"{Ue5Typography.MonospaceFamily}\", \"Cascadia Mono\", ui-monospace, monospace;");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "OvercoreEditorPhotino")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found above '" + AppContext.BaseDirectory + "'.");
    }
}
