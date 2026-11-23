using AwesomeAssertions;
using System.Numerics;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers the Transform rows the Photino inspector draws: three Vector3s, each component labelled with its
/// axis, and rotation shown as Euler degrees rather than as a quaternion.
/// </summary>
public class TransformRowsTests
{
    private static readonly float HalfTurn = MathF.PI / 2f;

    [Fact]
    public void From_ReturnsPositionRotationAndScale_InThatOrder()
    {
        SceneNodeSnapshot node = Node(position: new Vector3(1, 2, 3), rotation: Quaternion.Identity, scale: new Vector3(4, 5, 6));

        IReadOnlyList<TransformRow> rows = TransformRows.From(node);

        rows.Should().HaveCount(3);
        rows.Select(row => row.Label).Should().Equal("Position", "Rotation", "Scale");
        rows[0].Value.Should().Be(new Vector3(1, 2, 3));
        rows[1].Value.Should().Be(Vector3.Zero);
        rows[2].Value.Should().Be(new Vector3(4, 5, 6));
    }

    [Fact]
    public void Rotation_IsShownAsEulerDegrees_YawAroundY()
    {
        // A quarter turn about Y: the quaternion is (0, sin45, 0, cos45), the inspector shows (0, 90, 0).
        SceneNodeSnapshot node = Node(rotation: Quaternion.CreateFromYawPitchRoll(HalfTurn, 0f, 0f));

        Vector3 euler = TransformRows.From(node)[1].Value;

        euler.X.Should().BeApproximately(0f, 0.0001f);
        euler.Y.Should().BeApproximately(90f, 0.0001f);
        euler.Z.Should().BeApproximately(0f, 0.0001f);
    }

    [Fact]
    public void Rotation_IsShownAsEulerDegrees_PitchAroundXAndRollAroundZ()
    {
        TransformRows.From(Node(rotation: Quaternion.CreateFromYawPitchRoll(0f, HalfTurn, 0f)))[1].Value
            .X.Should().BeApproximately(90f, 0.0001f);

        TransformRows.From(Node(rotation: Quaternion.CreateFromYawPitchRoll(0f, 0f, HalfTurn)))[1].Value
            .Z.Should().BeApproximately(90f, 0.0001f);
    }

    [Fact]
    public void EveryRow_IsAVector3LabelledXyz()
    {
        TransformRow.Axes.Should().Equal("X", "Y", "Z");

        foreach (TransformRow row in TransformRows.From(Node(rotation: Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f))))
        {
            row.Components.Should().HaveCount(3, "the inspector labels one field per axis and nothing more");
            row.Components.Should().Equal(row.Value.X, row.Value.Y, row.Value.Z);
        }
    }

    [Fact]
    public void From_RejectsANullNode()
    {
        Action act = () => TransformRows.From(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static SceneNodeSnapshot Node(
        Vector3? position = null,
        Quaternion? rotation = null,
        Vector3? scale = null)
    {
        return new SceneNodeSnapshot(
            Guid.NewGuid(),
            "Object",
            position ?? Vector3.Zero,
            rotation ?? Quaternion.Identity,
            scale ?? Vector3.One,
            ["Transform"]);
    }
}
