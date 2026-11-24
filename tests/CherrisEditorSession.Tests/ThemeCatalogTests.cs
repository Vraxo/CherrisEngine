using AwesomeAssertions;
using Cherris.UI;
using Xunit;

namespace CherrisEditorSession.Tests;

/// <summary>
/// Covers the theme layer: that every theme is complete, that the two built-ins hold the values their sources
/// publish, that the shipped JSON files match the code, and that a theme dropped on disk is loaded, selectable
/// and remembered.
/// </summary>
public class ThemeCatalogTests
{
    [Fact]
    public void EveryTheme_FillsEveryRole()
    {
        foreach (EditorTheme theme in ThemeCatalog.BuiltIn)
        {
            theme.Colors.Keys.Should().BeEquivalentTo(ThemeRoles.All, $"'{theme.Id}' must theme every role");
            theme.Colors.Values.Should().OnlyContain(color => color.Hex.Length == 7 || color.Hex.Length == 9);
        }
    }

    [Fact]
    public void Ue5Theme_MatchesThePaletteAndTheType()
    {
        EditorTheme theme = Ue5StarshipTheme.Create();

        theme[ThemeRoles.Panel].Should().Be(Ue5Colors.Panel);
        theme[ThemeRoles.Background].Should().Be(Ue5Colors.Background);
        theme[ThemeRoles.Select].Should().Be(Ue5Colors.Select);
        theme[ThemeRoles.SelectionPressed].Should().Be(Ue5Colors.SelectionPressed);

        theme.Typography.Family.Should().Be("Roboto");
        theme.Typography.BodyPixels.Should().Be(Ue5Typography.NormalPixels);
        theme.Metrics.ControlHeight.Should().Be(22f);
        theme.Metrics.CornerRadius.Should().Be(4f);
    }

    [Fact]
    public void UnityTheme_UsesUnitysPublishedDarkValues()
    {
        EditorTheme theme = UnityDarkTheme.Create();

        // Each of these is the dark column of a --unity-colors-* variable on
        // foundations.unity.com/fundamentals/color-palette.
        theme[ThemeRoles.Panel].Hex.Should().Be("#383838", "window-background");
        theme[ThemeRoles.Background].Hex.Should().Be("#191919", "app_toolbar-background");
        theme[ThemeRoles.Recessed].Hex.Should().Be("#282828", "default-background");
        theme[ThemeRoles.Header].Hex.Should().Be("#3c3c3c", "headerbar-background");
        theme[ThemeRoles.Title].Hex.Should().Be("#3e3e3e", "inspector_titlebar-background");
        theme[ThemeRoles.Input].Hex.Should().Be("#2a2a2a", "input_field-background");
        theme[ThemeRoles.InputOutline].Hex.Should().Be("#212121", "input_field-border");
        theme[ThemeRoles.Primary].Hex.Should().Be("#3a79bb", "input_field-border-focus");
        theme[ThemeRoles.Dropdown].Hex.Should().Be("#515151", "dropdown-background");
        theme[ThemeRoles.Secondary].Hex.Should().Be("#585858", "button-background");
        theme[ThemeRoles.Hover].Hex.Should().Be("#676767", "button-background-hover");
        theme[ThemeRoles.Foreground].Hex.Should().Be("#d2d2d2", "default-text");
        theme[ThemeRoles.ForegroundInverted].Hex.Should().Be("#090909", "the light theme's text color");
        theme[ThemeRoles.Select].Hex.Should().Be("#2c5d87", "highlight-background");
        theme[ThemeRoles.SelectInactive].Hex.Should().Be("#4d4d4d", "highlight-background-inactive");
        theme[ThemeRoles.TabHover].Hex.Should().Be("#303030", "tab-background-hover");
        theme[ThemeRoles.WindowBorder].Hex.Should().Be("#242424", "window-border");
        theme[ThemeRoles.Error].Hex.Should().Be("#d32222", "error-text");
        theme[ThemeRoles.Warning].Hex.Should().Be("#f4bc02", "warning-text");

        // Its type comes from the same design system: Inter at 12px body, 18px single-line controls.
        theme.Typography.Family.Should().Be("Inter");
        theme.Typography.BodyPixels.Should().Be(12f);
        theme.Typography.BoldWeight.Should().Be(600);
        theme.Metrics.ControlHeight.Should().Be(18f);
    }

    [Fact]
    public void ShippedFiles_MatchTheBuiltInThemes()
    {
        string directory = Path.Combine(FindRepositoryRoot(), "src", "CherrisEditor", "EditorResources", "Themes");

        foreach (EditorTheme builtIn in ThemeCatalog.BuiltIn)
        {
            string file = Path.Combine(directory, builtIn.Id + ".json");

            File.Exists(file).Should().BeTrue($"'{builtIn.Id}.json' is how the theme is stored and distributed");

            EditorTheme? fromFile = ThemeJson.ReadFile(file, out string? error);

            error.Should().BeNull();
            fromFile.Should().NotBeNull();
            fromFile!.DisplayName.Should().Be(builtIn.DisplayName);
            fromFile.Typography.Should().Be(builtIn.Typography);
            fromFile.Metrics.Should().Be(builtIn.Metrics);

            foreach (string role in ThemeRoles.All)
            {
                fromFile[role].Should().Be(builtIn[role], $"'{builtIn.Id}.json' must match the code for '{role}'");
            }
        }
    }

    [Fact]
    public void Json_RoundTrips()
    {
        EditorTheme original = UnityDarkTheme.Create();

        EditorTheme? copy = ThemeJson.Read(ThemeJson.Write(original), out string? error);

        error.Should().BeNull();
        copy.Should().NotBeNull();
        copy!.Id.Should().Be(original.Id);
        copy.DisplayName.Should().Be(original.DisplayName);
        copy.Typography.Should().Be(original.Typography);
        copy.Metrics.Should().Be(original.Metrics);
        copy.ToCssVariables().Should().BeEquivalentTo(original.ToCssVariables());
        copy.IsExternal.Should().BeTrue();
    }

    [Theory]
    [InlineData("{}", "id")]
    [InlineData("{\"id\":\"x\",\"displayName\":\"X\"}", "required")]
    [InlineData("not json", "valid JSON")]
    public void Json_RejectsBrokenDocuments(string json, string reason)
    {
        EditorTheme? theme = ThemeJson.Read(json, out string? error);

        theme.Should().BeNull();
        error.Should().NotBeNull();
        error!.Should().ContainEquivalentOf(reason);
    }

    [Fact]
    public void Json_RejectsMissingOrUnknownRoles()
    {
        EditorTheme source = UnityDarkTheme.Create();
        string json = ThemeJson.Write(source);

        // Drop one role.
        string missing = json.Replace("\"panel\": \"#383838\",", string.Empty, StringComparison.Ordinal);
        missing.Should().NotBe(json);
        ThemeJson.Read(missing, out string? error).Should().BeNull();
        error!.Should().Contain("panel");

        // Invent one.
        string unknown = json.Replace("\"panel\": \"#383838\",", "\"panel\": \"#383838\", \"pannel\": \"#000000\",", StringComparison.Ordinal);
        unknown.Should().NotBe(json);
        ThemeJson.Read(unknown, out error).Should().BeNull();
        error!.Should().Contain("pannel");

        // Spell a color badly.
        string badHex = json.Replace("\"panel\": \"#383838\"", "\"panel\": \"#38383g\"", StringComparison.Ordinal);
        badHex.Should().NotBe(json);
        ThemeJson.Read(badHex, out error).Should().BeNull();
        error!.Should().Contain("hex");
    }

    [Fact]
    public void Catalog_LoadsAThemeFromDisk()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cherris-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            // A user theme derived from Unity's, with one color changed and a new id.
            EditorTheme custom = UnityDarkTheme.Create() with
            {
                Id = "unity-midnight",
                DisplayName = "Unity (midnight)",
                Colors = UnityDarkTheme.Create().Colors
                    .Select(pair => pair.Key == ThemeRoles.Select
                        ? new KeyValuePair<string, ThemeColor>(pair.Key, ThemeColor.FromHex("#101820"))
                        : pair)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            };

            File.WriteAllText(Path.Combine(directory, custom.Id + ".json"), ThemeJson.Write(custom));

            ThemeCatalog catalog = ThemeCatalog.Create([directory]);

            catalog.Themes.Should().HaveCount(ThemeCatalog.BuiltIn.Count + 1);
            catalog.Errors.Should().BeEmpty();

            EditorTheme? found = catalog.Find("unity-midnight");
            found.Should().NotBeNull();
            EditorTheme loaded = found!;
            loaded.IsExternal.Should().BeTrue();
            loaded[ThemeRoles.Select].Hex.Should().Be("#101820");

            catalog.Select("unity-midnight").Should().BeTrue();
            catalog.Active.Id.Should().Be("unity-midnight");
            catalog.Select("no-such-theme").Should().BeFalse();
            catalog.Active.Id.Should().Be("unity-midnight", "a failed selection must not clear the active theme");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Catalog_KeepsBrokenFilesOutOfTheWay()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cherris-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{ \"id\": \"broken\" }");

            ThemeCatalog catalog = ThemeCatalog.Create([directory]);

            catalog.Themes.Should().HaveCount(ThemeCatalog.BuiltIn.Count);
            catalog.Errors.Should().ContainSingle().Which.Error.Should().Contain("required");
            catalog.Active.Id.Should().Be(ThemeCatalog.Default.Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Preferences_RoundTrip()
    {
        string file = Path.Combine(Path.GetTempPath(), "cherris-theme-" + Guid.NewGuid().ToString("N"), "theme.json");

        try
        {
            ThemePreferences.Load(file).Should().BeNull();

            ThemePreferences.Save("unity-dark", file).Should().BeTrue();
            ThemePreferences.Load(file).Should().Be("unity-dark");

            // A missing selection falls back to the default theme rather than throwing.
            ThemeCatalog catalog = ThemeCatalog.CreateBuiltIn();
            catalog.SelectOrDefault("does-not-exist").Id.Should().Be(ThemeCatalog.Default.Id);
            catalog.SelectOrDefault("unity-dark").Id.Should().Be("unity-dark");
        }
        finally
        {
            string? directory = Path.GetDirectoryName(file);

            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void CssVariables_CoverTypeAndMetrics()
    {
        IReadOnlyDictionary<string, string> variables = UnityDarkTheme.Create().ToCssVariables();

        variables["--theme-panel"].Should().Be("#383838");
        variables["--theme-font-ui"].Should().Contain("Inter");
        variables["--theme-font-size"].Should().Be("12px");
        variables["--theme-font-small"].Should().Be("10px");
        variables["--theme-radius"].Should().Be("3px");
        variables["--theme-control-height"].Should().Be("18px");
        variables["--theme-scrollbar-width"].Should().Be("12px");
        variables["--theme-font-weight-bold"].Should().Be("600");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "CherrisEditor")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found above '" + AppContext.BaseDirectory + "'.");
    }
}
