using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// The hierarchy is Unity's scene list: the create button and search over the visibility and scene-picking
/// columns, the scene's own name heading the tree as a bold header row, one icon per object, and zebra rows
/// that keep striping past the last one. The look is sampled from Unity's dark hierarchy (#282828/#323232
/// rows, #232323 scene row and search well, #282828 column strip with #383838 hairlines) and remapped onto
/// theme roles, so this pins the anatomy in the markup and the roles in the stylesheet.
/// </summary>
public class HierarchySurfaceTests
{
    [Fact]
    public void TheTree_IsNotABoxInsideThePanel()
    {
        string tree = Rule("tree");

        tree.Should().NotContain("border:", "the panel already draws the only frame the rows need");
        tree.Should().NotContain("border-radius:", "a rounded inset would read as a box inside the panel");
    }

    [Fact]
    public void TheTree_StripesLikeUnitysList()
    {
        string body = Rule("hier-body");

        body.Should().Contain("color-mix(in srgb, var(--theme-recessed) 50%, var(--theme-panel))",
            "Unity's light stripe is the Recessed well lifted halfway to Panel (#323232 sampled)");
        body.Should().Contain("--hier-row-height: var(--theme-control-height)",
            "one stripe per row, at the density the theme asks for");

        string tree = Rule("hier-tree");

        tree.Should().Contain("repeating-linear-gradient",
            "Unity's stripes run past the last row, so they cannot be the rows' own backgrounds");
        tree.Should().Contain("var(--hier-stripe-dark)", "the dark stripe is the Recessed well (#282828 sampled)");
        tree.Should().Contain("background-attachment: local",
            "a stripe rides with its row when the list scrolls");

        Rule("tree-row").Should().Contain("height: var(--hier-row-height)",
            "rows and stripes share one height, so they cannot drift apart");
    }

    [Fact]
    public void TheSceneRow_SitsInTheSearchWell()
    {
        Rule("tree-scene").Should().Contain("background: var(--theme-input);",
            "the reference puts the same #232323 under the scene row and the search field");
        Rule("hier-scene-name").Should().Contain("font-weight: var(--theme-font-weight-bold);",
            "Unity titles the scene row");
    }

    [Fact]
    public void TheColumnStrip_KeepsUnitysHeaderSurface()
    {
        string columns = Rule("hier-columns");

        columns.Should().Contain("background: var(--hier-stripe-dark);",
            "the header strip is the dark stripe's surface (#282828 sampled)");
        columns.Should().Contain("color: var(--theme-foreground-header);");

        Rule("hier-col-pick").Should().Contain("border-right: var(--theme-border-width) solid var(--theme-panel);",
            "Unity hairlines the columns apart with a Panel-coloured line (#383838 sampled)");
    }

    [Fact]
    public void TheRows_CarryUnitysToggleColumns()
    {
        string markup = ReadHierarchy();

        int eye = markup.IndexOf("class=\"hier-cell hier-cell-eye\"", StringComparison.Ordinal);
        int pick = markup.IndexOf("class=\"hier-cell hier-cell-pick\"", StringComparison.Ordinal);
        int name = markup.IndexOf("class=\"hier-name\"", StringComparison.Ordinal);

        eye.Should().BeGreaterThan(-1, "every row reserves Unity's visibility cell");
        pick.Should().BeGreaterThan(-1, "and its scene-picking cell");
        name.Should().BeGreaterThan(-1, "the name column takes what is left");
        eye.Should().BeLessThan(pick, "the toggle columns come first, as Unity draws them");
        pick.Should().BeLessThan(name);
    }

    [Fact]
    public void TheEye_SwitchesTheRowRenderer()
    {
        string markup = ReadHierarchy();

        markup.Should().Contain("@if (row.VisibilityType is string visibilityType)",
            "only rows that draw get the toggle; the rest keep Unity's empty eye cell");
        markup.Should().Contain("Host.SetComponentEnabled(",
            "the eye is a real toggle: it switches the renderer that draws the row");
        markup.Should().Contain("fa-eye-slash", "a hidden row reads dim behind the slashed eye");
    }

    [Fact]
    public void TheToolbar_HasUnitysCreateButtonAndSearch()
    {
        string markup = ReadHierarchy();

        markup.Should().Contain("hier-create-button");
        markup.Should().Contain("fa-plus");
        markup.Should().Contain("Create Empty GameObject");
        markup.Should().Contain("class=\"search-field hier-search\"");
    }

    [Fact]
    public void TheRows_ShowOneComponentIcon()
    {
        string markup = ReadHierarchy();

        markup.Should().Contain("ComponentGlyphs.For(",
            "the row icon comes from the mapping the inspector shows, not a copy of it");
        markup.Should().Contain("\"fa-cube\", \"hier-icon-plain\"",
            "an empty object gets Unity's plain cube");
    }

    [Fact]
    public void TheContextMenu_DoesWhatUnitysMenuDoes()
    {
        string markup = ReadHierarchy();

        markup.Should().Contain("@oncontextmenu=\"(MouseEventArgs args) => OpenRowMenu(args, row)\"",
            "right-clicking a row opens the menu on it");
        markup.Should().Contain("OpenTreeMenu(args)",
            "right-clicking empty space or the scene row offers creating, as Unity does");
        markup.Should().Contain("class=\"menu-popup cb-menu\"",
            "the menu is the same inline menu the content browser gets");
        markup.Should().Contain("<span>Create Empty GameObject</span>");
        markup.Should().Contain("<span>Rename</span>");
        markup.Should().Contain("<span>Delete</span>");
        markup.Should().Contain("Host.RenameObject(",
            "Rename turns the row's label into a field and renames for real");
        markup.Should().Contain("Host.DeleteGameObject(",
            "Delete is a real step: the row and its subtree leave the scene, and Undo brings them back");
    }

    [Fact]
    public void TheRenameField_TakesTheLabelsPlace()
    {
        string rename = Rule("hier-rename");

        rename.Should().Contain("height: calc(var(--hier-row-height) - 4px)",
            "the field is the row's own height, not a bigger box");
    }

    private static string Rule(string selector)
    {
        // Comments are stripped first: they would otherwise attach to the selector printed in front of them.
        string bare = Regex.Replace(Stylesheet(), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        List<string> bodies = [];

        foreach (Match match in Regex.Matches(bare, @"([^{}]*)\{([^{}]*)\}"))
        {
            bool names = match.Groups[1].Value
                .Split(',')
                .Any(part => string.Equals(part.Trim(), "." + selector, StringComparison.Ordinal));

            if (names)
            {
                bodies.Add(match.Groups[2].Value);
            }
        }

        return bodies.Count > 0
            ? string.Join("\n", bodies)
            : throw new InvalidOperationException($"No .{selector} rule in app.css.");
    }

    private static string ReadHierarchy() =>
        Read("src", "OvercoreEditorPhotino", "Components", "HierarchyPanel.razor");

    private static string Stylesheet() =>
        Read("src", "OvercoreEditorPhotino", "wwwroot", "css", "app.css");

    private static string Read(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find {Path.Combine(parts)} above {AppContext.BaseDirectory}.");
    }
}
