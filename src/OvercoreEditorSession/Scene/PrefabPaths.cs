namespace OvercoreEditorSession;

/// <summary>
/// How an override names what it is about: the same names the editor's commands use, so a path means one
/// thing from the inspector's bold rows to the revert command to the scene's diff.
/// </summary>
public static class PrefabPaths
{
    /// <summary>The object's name.</summary>
    public const string Name = "Name";

    /// <summary>One Transform row: <c>Transform.Position</c>, <c>Transform.Rotation</c>, <c>Transform.Scale</c>.</summary>
    /// <param name="field">The row.</param>
    public static string Transform(TransformField field) => "Transform." + field;

    /// <summary>A component's on/off switch: <c>Enabled:Light</c>.</summary>
    /// <param name="typeName">The component's type name.</param>
    public static string Enabled(string typeName) => "Enabled:" + typeName;

    /// <summary>One component property: <c>Light.Intensity</c>.</summary>
    /// <param name="typeName">The component's type name.</param>
    /// <param name="propertyName">The property's name.</param>
    public static string Property(string typeName, string propertyName) => typeName + "." + propertyName;
}
