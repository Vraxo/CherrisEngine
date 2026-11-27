namespace Overcore.Projects;

/// <summary>Describes a project that can be copied into a user's workspace from the built-in template library.</summary>
public sealed record ProjectTemplate(string Id, string Name, string Description, bool IsBlank = false);

/// <summary>Stable identifiers and metadata for the built-in project templates.</summary>
public static class ProjectTemplateCatalog
{
    public const string BlankId = "blank";
    public const string ThreeDStarterId = "3d-starter";
    public const string UiShowcaseId = "ui-showcase";

    private static readonly ProjectTemplate[] s_templates =
    [
        new(BlankId, "Blank project", "A clean project with an empty start scene.", IsBlank: true),
        new(ThreeDStarterId, "3D starter", "A lit cube scene with a camera, plus notes on what to explore."),
        new(UiShowcaseId, "UI showcase", "A small interactive UI scene demonstrating layout, text, input, and RTL text.")
    ];

    /// <summary>Built-in templates in the order shown by launchers.</summary>
    public static IReadOnlyList<ProjectTemplate> Templates => s_templates;

    /// <summary>Looks up a built-in template by its stable identifier.</summary>
    public static ProjectTemplate? Find(string? id) =>
        s_templates.FirstOrDefault(template => String.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase));

    internal static string GetSourceDirectory(ProjectTemplate template)
    {
        if (template.IsBlank)
        {
            throw new ArgumentException("The blank project does not have template files.", nameof(template));
        }

        string source = Path.Combine(AppContext.BaseDirectory, "Resources", "ProjectTemplates", template.Id);
        if (Directory.Exists(source)) return source;

        throw new DirectoryNotFoundException(
            $"The built-in '{template.Name}' template is missing from the application files.");
    }
}
