using System.Globalization;
using System.Numerics;

namespace OvercoreEditorSession;

/// <summary>Which part of a transform a row, and therefore an edit of that row, changes.</summary>
public enum TransformField
{
    /// <summary>World position.</summary>
    Position,

    /// <summary>World rotation, shown and written as Euler degrees.</summary>
    Rotation,

    /// <summary>Local scale.</summary>
    Scale
}

/// <summary>
/// One Vector3 row of the Transform component, as the inspector draws it: a label and three components.
/// </summary>
/// <param name="Label">Row label: Position, Rotation or Scale.</param>
/// <param name="Field">Which part of the transform an edit of this row changes.</param>
/// <param name="Value">The row's value, X first.</param>
/// <param name="Default">What the row is reset to, in the same units as <paramref name="Value"/>.</param>
/// <remarks>
/// <para>
/// Both front ends label a vector's components the same way — X red, Y green, Z blue — so the axis letters
/// live here rather than in the markup. The ImGui inspector draws the same letters in the same colors
/// (<c>TransformInspector.DrawVector3Control</c>).
/// </para>
/// <para>
/// The defaults are the ones <c>TransformInspector</c> resets to: position zero, rotation the identity — which
/// in Euler degrees is zero on all three axes — and scale one.
/// </para>
/// </remarks>
public sealed record TransformRow(
    string Label, TransformField Field, System.Numerics.Vector3 Value, System.Numerics.Vector3 Default)
{
    /// <summary>
    /// How far a component may drift from its default and still count as being there: a quaternion read back as
    /// Euler degrees lands a hair off the whole number.
    /// </summary>
    private const float Tolerance = 0.001f;

    /// <summary>Axis letters in component order, for the label of each field.</summary>
    public static IReadOnlyList<string> Axes { get; } = ["X", "Y", "Z"];

    /// <summary>The row's components, X, Y and Z.</summary>
    public IReadOnlyList<float> Components => [Value.X, Value.Y, Value.Z];

    /// <summary>True when the row is away from its default, which is when resetting it does something.</summary>
    public bool CanReset =>
        MathF.Abs(Value.X - Default.X) > Tolerance ||
        MathF.Abs(Value.Y - Default.Y) > Tolerance ||
        MathF.Abs(Value.Z - Default.Z) > Tolerance;

    /// <summary>The default as the row would show it, for the reset button's tooltip.</summary>
    public string DefaultText => Format(Default);

    /// <summary>The three components in the inspector's "0.###" style, X first.</summary>
    /// <param name="value">The value to write out.</param>
    public static string Format(System.Numerics.Vector3 value) => String.Join(
        ", ",
        value.X.ToString("0.###", CultureInfo.InvariantCulture),
        value.Y.ToString("0.###", CultureInfo.InvariantCulture),
        value.Z.ToString("0.###", CultureInfo.InvariantCulture));
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
            new TransformRow("Position", TransformField.Position, node.Position, Vector3.Zero),
            new TransformRow("Rotation", TransformField.Rotation, node.RotationDegrees, Vector3.Zero),
            new TransformRow("Scale", TransformField.Scale, node.Scale, Vector3.One)
        ];
    }
}
