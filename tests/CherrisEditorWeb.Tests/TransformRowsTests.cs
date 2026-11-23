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
    private const float HalfTurn = MathF.PI / 2f;

    // A float quaternion round-trip cannot land on a whole degree: yaw 90° decomposes to 89.9802°, because the
    // stored q.Y is sin(45°) and asin() of its square lands just short of 1. A tenth of a degree is the honest
    // bound, and it still catches a wrong axis or a radians/degrees mix-up.
    private const float HalfDegree = 0.1f;

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

        euler.X.Should().BeApproximately(0f, HalfDegree);
        euler.Y.Should().BeApproximately(90f, HalfDegree);
        euler.Z.Should().BeApproximately(0f, HalfDegree);
    }

    [Fact]
    public void Rotation_IsShownAsEulerDegrees_PitchAroundXAndRollAroundZ()
    {
        TransformRows.From(Node(rotation: Quaternion.CreateFromYawPitchRoll(0f, HalfTurn, 0f)))[1].Value
            .X.Should().BeApproximately(90f, HalfDegree);

        TransformRows.From(Node(rotation: Quaternion.CreateFromYawPitchRoll(0f, 0f, HalfTurn)))[1].Value
            .Z.Should().BeApproximately(90f, HalfDegree);
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
    public void EveryRow_KnowsWhatItIsResetTo()
    {
        IReadOnlyList<TransformRow> rows = TransformRows.From(Node(
            position: new Vector3(0f, 1f, 0f),
            rotation: Quaternion.Identity,
            scale: new Vector3(2f, 2f, 2f)));

        rows[0].Default.Should().Be(Vector3.Zero, "a new object sits at the origin");
        rows[1].Default.Should().Be(Vector3.Zero, "a new object is unrotated, which is zero in Euler degrees");
        rows[2].Default.Should().Be(Vector3.One, "a new object is unscaled");

        rows[0].CanReset.Should().BeTrue("the object sits at y = 1");
        rows[1].CanReset.Should().BeFalse("it is already at the identity rotation");
        rows[2].CanReset.Should().BeTrue();

        rows[0].DefaultText.Should().Be("0, 0, 0");
        rows[2].DefaultText.Should().Be("1, 1, 1");
    }

    [Fact]
    public void CanReset_IgnoresTheNoiseOfAQuaternionReadBack()
    {
        // A rotation that is a hair off the identity — a quaternion round trip leaves such dust — must still read
        // as being at the default, or the reset arrow would light up on rows nobody touched.
        TransformRow nearlyZero = TransformRows.From(
            Node(rotation: Quaternion.CreateFromYawPitchRoll(0.0000001f, 0f, 0f)))[1];

        nearlyZero.Value.X.Should().BeApproximately(0f, 0.001f);
        nearlyZero.CanReset.Should().BeFalse();
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
            [new ComponentSnapshot("Transform", [])]);
    }
}
