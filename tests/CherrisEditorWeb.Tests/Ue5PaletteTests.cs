using AwesomeAssertions;
using Cherris.UI;
using System.Text.RegularExpressions;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Keeps the stylesheet's fallback theme in step with the C# theme it mirrors, and keeps the two front ends
/// reading the same role names.
/// </summary>
/// <remarks>
/// The stylesheet's <c>:root</c> block is the default theme's values, so the UI is styled before Blazor and
/// without JavaScript; the editor then overrides those properties at runtime from whichever
/// <see cref="EditorTheme"/> is selected. This test pins the fallback.
/// </remarks>
public class Ue5PaletteTests
{
    [Fact]
    public void Palette_HoldsTheEngineColors()
    {
        Ue5Colors.Black.Hex.Should().Be("#000000");
        Ue5Colors.Title.Hex.Should().Be("#151515");
        Ue5Colors.Background.Hex.Should().Be("#151515");
        Ue5Colors.WindowBorder.Hex.Should().Be("#0f0f0f");
        Ue5Colors.Input.Hex.Should().Be("#0f0f0f");
        Ue5Colors.InputOutline.Hex.Should().Be("#383838");
        Ue5Colors.Recessed.Hex.Should().Be("#1a1a1a");
        Ue5Colors.Panel.Hex.Should().Be("#242424");
        Ue5Colors.Header.Hex.Should().Be("#2f2f2f");
        Ue5Colors.Dropdown.Hex.Should().Be("#383838");
        Ue5Colors.DropdownOutline.Hex.Should().Be("#4c4c4c");
        Ue5Colors.Hover.Hex.Should().Be("#575757");
        Ue5Colors.Hover2.Hex.Should().Be("#808080");
        Ue5Colors.White25.Hex.Should().Be("#ffffff40");
        Ue5Colors.Primary.Hex.Should().Be("#0070e0");
        Ue5Colors.PrimaryHover.Hex.Should().Be("#0e86ff");
        Ue5Colors.PrimaryPress.Hex.Should().Be("#0050a0");
        Ue5Colors.Secondary.Hex.Should().Be("#383838");
        Ue5Colors.Foreground.Hex.Should().Be("#c0c0c0");
        Ue5Colors.ForegroundHover.Hex.Should().Be("#ffffff");
        Ue5Colors.ForegroundHeader.Hex.Should().Be("#c8c8c8");
        Ue5Colors.Select.Hex.Should().Be("#0070e0");
        Ue5Colors.SelectInactive.Hex.Should().Be("#40576f");
        Ue5Colors.SelectParent.Hex.Should().Be("#2c323a");
        Ue5Colors.SelectHover.Hex.Should().Be("#242424");
        Ue5Colors.Warning.Hex.Should().Be("#ffb800");
        Ue5Colors.Error.Hex.Should().Be("#ef3535");
        Ue5Colors.Success.Hex.Should().Be("#1fe44b");
        Ue5Colors.Selection.Hex.Should().Be("#18a0fb");
        Ue5Colors.SelectionPressed.Hex.Should().Be("#da820a");
        Ue5Colors.TabHover.Hex.Should().Be("#212121");
    }

    [Fact]
    public void ToVector4_ConvertsToImGuiRange()
    {
        Ue5Colors.Panel.ToVector4().Should().Be(new System.Numerics.Vector4(0x24 / 255f, 0x24 / 255f, 0x24 / 255f, 1f));
        Ue5Colors.White25.WithAlpha(0.5f).A.Should().Be(128);
    }

    [Fact]
    public void Stylesheet_MatchesTheDefaultTheme()
    {
        string stylesheet = File.ReadAllText(FindStylesheet());
        EditorTheme theme = Ue5StarshipTheme.Create();
        IReadOnlyDictionary<string, string> expected = theme.ToCssVariables();

        Dictionary<string, string> declared = Regex
            .Matches(stylesheet, @"(?m)^\s*(--theme-[a-z0-9-]+)\s*:\s*([^;]+);")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Trim());

        foreach ((string token, string value) in expected)
        {
            declared.Should().ContainKey(token, "the stylesheet has to declare every theme variable as a fallback");
            declared[token].Should().Be(value, $"'{token}' must be the default theme's value");
        }
    }

    [Fact]
    public void Stylesheet_ReadsRolesRatherThanOneEditorsNumbers()
    {
        string stylesheet = File.ReadAllText(FindStylesheet());

        // No rule may hardcode a palette value: everything goes through a --theme-* variable, which is what
        // lets a theme swap at runtime.
        stylesheet.Should().NotContain("--ue-");
        stylesheet.Should().Contain("font-family: var(--theme-font-ui)");
        stylesheet.Should().Contain("font-size: var(--theme-font-size)");
    }

    private static string FindStylesheet()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "CherrisEditorPhotino", "wwwroot", "css", "app.css");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("app.css not found above '" + AppContext.BaseDirectory + "'.");
    }
}
