namespace CherrisEditorWeb;

/// <summary>
/// One Vector3 row of the Transform component, as the inspector draws it: a label and three components.
/// </summary>
/// <param name="Label">Row label: Position, Rotation or Scale.</param>
/// <param name="Value">The row's value, X first.</param>
/// <remarks>
/// Both front ends label a vector's components the same way — X red, Y green, Z blue — so the axis letters
/// live here rather than in the markup. The ImGui inspector draws the same letters in the same colors
/// (<c>TransformInspector.DrawVector3Control</c>).
/// </remarks>
public sealed record TransformRow(string Label, System.Numerics.Vector3 Value)
{
    /// <summary>Axis letters in component order, for the label of each field.</summary>
    public static IReadOnlyList<string> Axes { get; } = ["X", "Y", "Z"];

    /// <summary>The row's components, X, Y and Z.</summary>
    public IReadOnlyList<float> Components => [Value.X, Value.Y, Value.Z];
}

/// <summary>The Transform component's rows, in the order the inspector shows them.</summary>
public static class TransformRows
{
    /// <summary>
    /// Builds the three rows for an object: Position and Scale as they are, Rotation as Euler degrees — the
    /// same reading the ImGui inspector edits, so the two windows agree on what the numbers mean.
    /// </summary>
    /// <param name="node">The object to describe.</param>
    public static IReadOnlyList<TransformRow> From(SceneNodeSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return
        [
            new TransformRow("Position", node.Position),
            new TransformRow("Rotation", node.RotationDegrees),
            new TransformRow("Scale", node.Scale)
        ];
    }
}
