using AwesomeAssertions;
using Cherris.UI;
using System.Text.RegularExpressions;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Keeps the two front ends on the engine's palette: ImGui reads <see cref="Ue5Colors"/> directly, the
/// Blazor UI reads <c>wwwroot/css/app.css</c>, and this test fails if those two ever disagree.
/// </summary>
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
        // White25 is the engine's translucent separator/menu-heading color.
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
        // The engine paints a hovered tab with Panel at 80% over Background: 0.8 * 0x24 + 0.2 * 0x15 = 0x21.
        Ue5Colors.TabHover.Hex.Should().Be("#212121");
    }

    [Fact]
    public void ToVector4_ConvertsToImGuiRange()
    {
        Ue5Colors.Panel.ToVector4().Should().Be(new System.Numerics.Vector4(0x24 / 255f, 0x24 / 255f, 0x24 / 255f, 1f));
        Ue5Colors.White25.WithAlpha(0.5f).A.Should().Be(128);
    }

    [Fact]
    public void Stylesheet_MatchesThePalette()
    {
        string stylesheet = File.ReadAllText(FindStylesheet());

        Dictionary<string, Ue5Color> expected = new()
        {
            ["--ue-black"] = Ue5Colors.Black,
            ["--ue-title"] = Ue5Colors.Title,
            ["--ue-background"] = Ue5Colors.Background,
            ["--ue-window-border"] = Ue5Colors.WindowBorder,
            ["--ue-foldout"] = Ue5Colors.Foldout,
            ["--ue-input"] = Ue5Colors.Input,
            ["--ue-input-outline"] = Ue5Colors.InputOutline,
            ["--ue-recessed"] = Ue5Colors.Recessed,
            ["--ue-panel"] = Ue5Colors.Panel,
            ["--ue-header"] = Ue5Colors.Header,
            ["--ue-dropdown"] = Ue5Colors.Dropdown,
            ["--ue-dropdown-outline"] = Ue5Colors.DropdownOutline,
            ["--ue-hover"] = Ue5Colors.Hover,
            ["--ue-hover-2"] = Ue5Colors.Hover2,
            ["--ue-white"] = Ue5Colors.White,
            ["--ue-white-25"] = Ue5Colors.White25,
            ["--ue-highlight"] = Ue5Colors.Highlight,
            ["--ue-primary"] = Ue5Colors.Primary,
            ["--ue-primary-hover"] = Ue5Colors.PrimaryHover,
            ["--ue-primary-press"] = Ue5Colors.PrimaryPress,
            ["--ue-secondary"] = Ue5Colors.Secondary,
            ["--ue-foreground"] = Ue5Colors.Foreground,
            ["--ue-foreground-hover"] = Ue5Colors.ForegroundHover,
            ["--ue-foreground-inverted"] = Ue5Colors.ForegroundInverted,
            ["--ue-foreground-header"] = Ue5Colors.ForegroundHeader,
            ["--ue-select"] = Ue5Colors.Select,
            ["--ue-select-inactive"] = Ue5Colors.SelectInactive,
            ["--ue-select-parent"] = Ue5Colors.SelectParent,
            ["--ue-select-hover"] = Ue5Colors.SelectHover,
            ["--ue-notifications"] = Ue5Colors.Notifications,
            ["--ue-accent-blue"] = Ue5Colors.AccentBlue,
            ["--ue-accent-purple"] = Ue5Colors.AccentPurple,
            ["--ue-accent-pink"] = Ue5Colors.AccentPink,
            ["--ue-accent-red"] = Ue5Colors.AccentRed,
            ["--ue-accent-orange"] = Ue5Colors.AccentOrange,
            ["--ue-accent-yellow"] = Ue5Colors.AccentYellow,
            ["--ue-accent-green"] = Ue5Colors.AccentGreen,
            ["--ue-accent-brown"] = Ue5Colors.AccentBrown,
            ["--ue-accent-black"] = Ue5Colors.AccentBlack,
            ["--ue-accent-gray"] = Ue5Colors.AccentGray,
            ["--ue-accent-white"] = Ue5Colors.AccentWhite,
            ["--ue-accent-folder"] = Ue5Colors.AccentFolder,
            ["--ue-warning"] = Ue5Colors.Warning,
            ["--ue-error"] = Ue5Colors.Error,
            ["--ue-success"] = Ue5Colors.Success,
            ["--ue-selection"] = Ue5Colors.Selection,
            ["--ue-tab-hover"] = Ue5Colors.TabHover
        };

        Dictionary<string, string> declared = Regex
            .Matches(stylesheet, @"(?m)^\s*(--ue-[a-z0-9-]+)\s*:\s*([^;]+);")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Trim());

        declared.Keys.Should().BeEquivalentTo(expected.Keys,
            "every engine color the stylesheet declares must have a counterpart in Ue5Colors");

        foreach ((string token, Ue5Color color) in expected)
        {
            declared[token].Should().Be(color.Hex, $"'{token}' must be the engine's exact value");
        }
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

        throw new FileNotFoundException(
            "wwwroot/css/app.css was not found above '" + AppContext.BaseDirectory + "'.");
    }
}
