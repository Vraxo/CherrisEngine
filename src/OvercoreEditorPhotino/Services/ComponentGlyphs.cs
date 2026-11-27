using Overcore.Components;

namespace OvercoreEditorPhotino.Services;

/// <summary>
/// One Font Awesome glyph and tint per component family: the inspector's section headers, its Add Component
/// menu and the hierarchy's rows all show the same face for the same component, so the mapping lives here once
/// and no panel can drift from another.
/// </summary>
/// <remarks>
/// The colours are CSS classes backed by theme roles, so an icon still recolours when the editor changes
/// theme; a script or an unknown component receives a generic component glyph.
/// </remarks>
public static class ComponentGlyphs
{
    private static readonly Dictionary<string, (string Glyph, string Color)> Icons =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Transform"] = ("fa-arrows-up-down-left-right", "component-icon-blue"),
            // Font Awesome's movie/video camera (fa-video, also named fa-video-camera), not a still camera.
            ["Camera"] = ("fa-video", "component-icon-light-blue"),
            ["Light"] = ("fa-lightbulb", "component-icon-yellow"),
            ["AudioSource"] = ("fa-volume-high", "component-icon-green"),
            ["AudioListener"] = ("fa-ear-listen", "component-icon-purple"),
            ["MeshRenderer"] = ("fa-cube", "component-icon-green"),
            ["RigidBody"] = ("fa-weight-hanging", "component-icon-orange"),
            ["Skybox"] = ("fa-mountain-sun", "component-icon-pink"),
            ["ParticleSystem"] = ("fa-wand-magic-sparkles", "component-icon-pink"),
            ["NavMeshSurface"] = ("fa-map", "component-icon-light-blue"),
            ["NavMeshAgent"] = ("fa-person-walking", "component-icon-green"),
            ["NavMeshObstacle"] = ("fa-road-barrier", "component-icon-orange"),
            ["Script"] = ("fa-code", "component-icon-purple"),
            ["LanguageScript"] = ("fa-scroll", "component-icon-purple")
        };

    /// <summary>The glyph for a component named the way the scene snapshot names it.</summary>
    public static (string Glyph, string Color) For(string typeName)
    {
        if (Icons.TryGetValue(typeName, out var icon))
        {
            return icon;
        }

        return typeName.Contains("Script", StringComparison.OrdinalIgnoreCase)
            ? ("fa-code", "component-icon-purple")
            : ("fa-puzzle-piece", "component-icon-purple");
    }

    /// <summary>Compiled C# scripts get the script glyph even though their class names are project-specific.</summary>
    public static (string Glyph, string Color) For(Type componentType)
    {
        if (Icons.TryGetValue(componentType.Name, out var icon))
        {
            return icon;
        }

        return typeof(Script).IsAssignableFrom(componentType)
            ? ("fa-code", "component-icon-purple")
            : ("fa-puzzle-piece", "component-icon-purple");
    }
}
