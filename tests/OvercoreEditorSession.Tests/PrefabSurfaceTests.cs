using AwesomeAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// The prefab workflow's visible surface: the hierarchy's Create Prefab menu item, the link mark on a
/// prefab's rows, and the content browser's Instantiate action on a prefab's tile.
/// </summary>
public class PrefabSurfaceTests
{
    [Fact]
    public void TheContextMenu_OffersCreatingAPrefab()
    {
        string markup = Read("src", "OvercoreEditorPhotino", "Components", "HierarchyPanel.razor");

        markup.Should().Contain("<span>Create Prefab</span>", "the row menu saves the row as a prefab");
        markup.Should().Contain("Host.CreatePrefab(",
            "the item is a real step: it writes the asset and links the objects to it");
    }

    [Fact]
    public void PrefabRows_CarryTheLinkMark()
    {
        string markup = Read("src", "OvercoreEditorPhotino", "Components", "HierarchyPanel.razor");

        markup.Should().Contain("IsPrefabInstance", "the mark follows the snapshot's link flag");
        markup.Should().Contain("hier-prefab-mark");
        markup.Should().Contain("fa-cubes-stacked",
            "Unity's blue cube for a prefab's objects, in this theme's roles");

        string mark = Rule("hier-prefab-mark");
        mark.Should().Contain("color: var(--theme-primary);",
            "the mark is tinted by a theme role, not a raw colour");
    }

    [Fact]
    public void TheContentBrowser_InstantiatesAPrefab()
    {
        string markup = Read("src", "OvercoreEditorPhotino", "Components", "ContentBrowserPanel.razor");

        markup.Should().Contain("<span>Instantiate in Scene</span>");
        markup.Should().Contain("Host.InstantiatePrefab(", "the item adds one to the scene for real");
        markup.Should().Contain("IsPrefab(", "prefab tiles know what they are");
        markup.Should().Contain("\".prefab\" =>", "a prefab tile wears a cube of its own");
    }

    [Fact]
    public void Commands_ExistForBothDirections()
    {
        string commands = Read("src", "OvercoreEditorSession", "Host", "EditorCommands.cs");

        commands.Should().Contain("class CreatePrefabFromSelectionCommand(Guid objectId, string assetPath)");
        commands.Should().Contain("class InstantiatePrefabCommand(string assetPath)");
        commands.Should().Contain("PrefabSource", "the objects know what they are linked to");
    }

    [Fact]
    public void TheInspector_MarksOverriddenValuesInPlace()
    {
        string markup = Read("src", "OvercoreEditorPhotino", "Components", "InspectorPanel.razor");

        markup.Should().Contain("PrefabPaths.Name", "the name row knows what it is called in the diff");
        markup.Should().Contain("PrefabPaths.Transform(row.Field)", "so do the transform rows");
        markup.Should().Contain("PrefabPaths.Enabled(component.TypeName)", "so does the component header");
        markup.Should().Contain("PrefabPaths.Property(component.TypeName, property.Name)", "so does every property row");
        markup.Should().Contain("\"overridden\"", "the drifted ones wear the class");

        string mark = Rule("overridden");
        mark.Should().Contain("font-weight: 700;", "Unity marks an override by bolding the label; so is this");
    }

    [Fact]
    public void TheInspector_HasTheOverridesDropdown()
    {
        string markup = Read("src", "OvercoreEditorPhotino", "Components", "InspectorPanel.razor");

        markup.Should().Contain("class=\"imgui-button inspector-overrides\"", "the object row carries Unity's dropdown");
        markup.Should().Contain("node.Overrides.Count", "its count is what has drifted");
        markup.Should().Contain("Host.RevertOverride(", "each line reverts for real");
        markup.Should().Contain("Host.ApplyOverride(", "and applies for real");
        markup.Should().Contain("Host.RevertAllOverrides(", "the all-up steps sit beside them");
        markup.Should().Contain("Host.ApplyAllOverrides(");
        markup.Should().Contain("entry.SourceText", "the list shows the prefab's own value, not a guess");
    }

    [Fact]
    public void OverrideCommands_ExistWithHonestUndo()
    {
        string commands = Read("src", "OvercoreEditorSession", "Host", "PrefabOverrideCommands.cs");

        commands.Should().Contain("class RevertPrefabOverrideCommand(Guid objectId, string propertyPath)");
        commands.Should().Contain("class RevertAllPrefabOverridesCommand(Guid objectId, string name)");
        commands.Should().Contain("class ApplyPrefabOverrideCommand(Guid objectId, string propertyPath)");
        commands.Should().Contain("class ApplyAllPrefabOverridesCommand(Guid objectId, string name)");
        commands.Should().Contain("File.ReadAllBytes", "an apply remembers the asset's bytes to put back");
        commands.Should().Contain("File.WriteAllBytes", "and undo writes exactly those bytes back");
    }

    [Fact]
    public void TheOverridesList_UsesOnlyThemeRoles()
    {
        Rule("override-path").Should().Contain("var(--theme-label)", "the path reads as ordinary text");
        Rule("override-source").Should().Contain("var(--theme-accent-gray)", "the prefab's value sits quieter beside it");
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
