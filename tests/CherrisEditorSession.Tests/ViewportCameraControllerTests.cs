using AwesomeAssertions;
using Cherris;
using Cherris.Components;
using Cherris.Core;
using System.Numerics;
using Xunit;

namespace CherrisEditorSession.Tests;

/// <summary>
/// Covers the editor's fly camera: what a drag does, what the movement keys do, and what it refuses to do when the
/// numbers it is handed make no sense.
/// </summary>
public class ViewportCameraControllerTests
{
    private static readonly float Frame = 1f / 60f;

    [Fact]
    public void NoInput_LeavesTheCameraExactlyWhereItIs()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), ViewportCameraInput.None, Frame);

        owner.Transform.Position.Should().Be(Vector3.Zero);
        owner.Transform.Rotation.Should().Be(Quaternion.Identity);
    }

    [Fact]
    public void Look_TurnsTheCameraByTheDragAndNoMore()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        // Dragging right by 100 pixels turns the other way, the way a first-person camera does.
        controller.Update(owner.GetComponent<Camera>(), LookInput(100f, 0f), Frame);

        controller.Look.X.Should().BeApproximately(-100f * ViewportCameraController.MouseSensitivity, 1e-5f);
        controller.Look.Y.Should().BeApproximately(0f, 1e-5f);

        // Dragging right turns the view right: a camera that started looking down -Z now looks a little towards +X.
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, owner.Transform.Rotation);
        forward.X.Should().BeGreaterThan(0.01f);
        forward.Z.Should().BeLessThan(0f, "it is still looking forwards, not sideways");
    }

    [Fact]
    public void Look_StopsShortOfStraightUpAndDown()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), LookInput(0f, 100000f), Frame);
        controller.Look.Y.Should().BeApproximately(-ViewportCameraController.PitchLimit, 0.0001f);

        controller.Update(owner.GetComponent<Camera>(), LookInput(0f, -200000f), Frame);
        controller.Look.Y.Should().BeApproximately(ViewportCameraController.PitchLimit, 0.0001f);

        // Straight up and down is where the yaw would flip the view over, so it is never reached.
        controller.Look.Y.Should().BeLessThan(MathF.PI / 2f);
    }

    [Fact]
    public void Move_WalksForwardAndBackAlongTheView()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), MoveInput(forward: true), 1f);
        owner.Transform.Position.Z.Should().BeApproximately(-ViewportCameraController.MoveSpeed, 0.001f);
        owner.Transform.Position.X.Should().BeApproximately(0f, 0.001f);

        owner.Transform.Position = Vector3.Zero;

        controller.Update(owner.GetComponent<Camera>(), MoveInput(forward: false), 1f);
        owner.Transform.Position.Z.Should().BeApproximately(ViewportCameraController.MoveSpeed, 0.001f);
    }

    [Fact]
    public void Move_StrafeFollowsTheYawSoItStaysOnTheFloor()
    {
        ViewportCameraController controller = new();
        const float Yaw = 0.7f;

        GameObject owner = CameraAt(Vector3.Zero, Quaternion.CreateFromYawPitchRoll(Yaw, 0f, 0f));

        controller.SyncFrom(owner.Transform.Rotation);

        // Look most of the way down without turning at all, so the pitch is what a strafe has to ignore.
        controller.Update(owner.GetComponent<Camera>(), LookInput(0f, 500f), Frame);

        controller.Update(owner.GetComponent<Camera>(), new ViewportCameraInput(
            Vector2.Zero,
            new Vector2(1f, 0f),
            0f,
            0f,
            false), 1f);

        // Standing a little to the left of -Z, the camera's own right is a shade towards +X and, because the movement
        // follows the yaw and not the view, it is still entirely level: looking down must not turn a strafe into a dive.
        owner.Transform.Position.X.Should().BeApproximately(ViewportCameraController.MoveSpeed * MathF.Cos(Yaw), 0.001f);
        owner.Transform.Position.Z.Should().BeApproximately(-ViewportCameraController.MoveSpeed * MathF.Sin(Yaw), 0.001f);
        owner.Transform.Position.Y.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Move_UpAndDownAreWorldAxes()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), new ViewportCameraInput(Vector2.Zero, Vector2.Zero, 1f, 0f, false), 1f);
        owner.Transform.Position.Y.Should().BeApproximately(ViewportCameraController.MoveSpeed, 0.001f);

        owner.Transform.Position = Vector3.Zero;

        controller.Update(owner.GetComponent<Camera>(), new ViewportCameraInput(Vector2.Zero, Vector2.Zero, -1f, 0f, false), 1f);
        owner.Transform.Position.Y.Should().BeApproximately(-ViewportCameraController.MoveSpeed, 0.001f);
    }

    [Fact]
    public void Move_DiagonallyIsNoFasterThanStraightAhead()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), new ViewportCameraInput(
            Vector2.Zero,
            new Vector2(1f, -1f),
            0f,
            0f,
            false), 1f);

        owner.Transform.Position.Length().Should().BeApproximately(ViewportCameraController.MoveSpeed, 0.001f);
    }

    [Fact]
    public void Run_IsThreeTimesAsFast()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), new ViewportCameraInput(
            Vector2.Zero,
            new Vector2(0f, -1f),
            0f,
            0f,
            true), 1f);

        owner.Transform.Position.Z.Should()
            .BeApproximately(-ViewportCameraController.MoveSpeed * ViewportCameraController.RunMultiplier, 0.001f);
    }

    [Fact]
    public void Zoom_FliesAlongTheView()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(new Vector3(0f, 1f, 2f), Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), new ViewportCameraInput(Vector2.Zero, Vector2.Zero, 0f, 2f, false), Frame);

        owner.Transform.Position.Z.Should()
            .BeApproximately(2f - (2f * ViewportCameraController.ZoomSensitivity), 0.001f);
        owner.Transform.Position.Y.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void Time_ThatIsNotAPositiveNumberMovesNothing()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), MoveInput(forward: true), 0f);
        owner.Transform.Position.Should().Be(Vector3.Zero);

        controller.Update(owner.GetComponent<Camera>(), MoveInput(forward: true), float.NaN);
        owner.Transform.Position.Should().Be(Vector3.Zero);

        controller.Update(owner.GetComponent<Camera>(), MoveInput(forward: true), -1f);
        owner.Transform.Position.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void Input_ThatIsNotANumberIsIgnored()
    {
        ViewportCameraController controller = new();
        GameObject owner = CameraAt(Vector3.Zero, Quaternion.Identity);

        controller.Update(owner.GetComponent<Camera>(), new ViewportCameraInput(
            new Vector2(float.NaN, 0f), new Vector2(float.PositiveInfinity, 0f), float.NaN, float.NaN, false), 1f);

        owner.Transform.Position.Should().Be(Vector3.Zero);
        owner.Transform.Rotation.Should().Be(Quaternion.Identity);
    }

    [Fact]
    public void ACameraOnNoObjectIsIgnored()
    {
        ViewportCameraController controller = new();

        Action fly = () => controller.Update(new Camera(), MoveInput(forward: true), 1f);

        fly.Should().NotThrow();
        controller.Look.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void SyncFrom_StartsFromTheCamerasOwnRotation()
    {
        ViewportCameraController controller = new();

        // The angles are read back with the engine's own extraction, the one the desktop editor's controller uses, so
        // a camera it left pointing somewhere is adopted rather than snapped away from.
        controller.SyncFrom(Quaternion.CreateFromYawPitchRoll(0.6f, 0f, 0f));
        controller.Look.X.Should().BeApproximately(0.6f, 0.001f);
        controller.Look.Y.Should().BeApproximately(0f, 0.001f);

        controller.SyncFrom(Quaternion.CreateFromYawPitchRoll(0f, -0.3f, 0f));
        controller.Look.X.Should().BeApproximately(0f, 0.001f);
        controller.Look.Y.Should().BeApproximately(-0.3f, 0.001f);
    }

    private static ViewportCameraInput LookInput(float x, float y) =>
        new(new Vector2(x, y), Vector2.Zero, 0f, 0f, false);

    private static ViewportCameraInput MoveInput(bool forward) =>
        new(Vector2.Zero, new Vector2(0f, forward ? -1f : 1f), 0f, 0f, false);

    private static GameObject CameraAt(Vector3 position, Quaternion rotation)
    {
        GameObject gameObject = new("Camera");
        gameObject.Transform.Position = position;
        gameObject.Transform.Rotation = rotation;
        gameObject.AddComponent(new Camera());

        return gameObject;
    }
}
