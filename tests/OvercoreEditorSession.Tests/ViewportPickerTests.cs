using AwesomeAssertions;
using Overcore;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering;
using System.Numerics;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Covers clicking in the viewport: the ray a pixel casts, and which object of a scene it lands on.
/// </summary>
public class ViewportPickerTests
{
    private static readonly Vector2 Viewport = new(800f, 600f);

    /// <summary>The middle of the viewport, which is where a camera with no rotation looks.</summary>
    private static Vector2 Centre => new(Viewport.X / 2f, Viewport.Y / 2f);

    [Fact]
    public void Pick_FindsTheObjectTheCameraIsPointingAt()
    {
        GameObject camera = CameraAt(new Vector3(0f, 0f, 5f));
        GameObject cube = CubeAt(Vector3.Zero, 1f);
        Scene scene = new(string.Empty, [camera, cube]);

        ViewportPicker.Pick(scene, Centre, Viewport).Should().BeSameAs(cube);
    }

    [Fact]
    public void Pick_ReturnsNothingWhenTheClickMisses()
    {
        GameObject camera = CameraAt(new Vector3(0f, 0f, 5f));
        GameObject cube = CubeAt(Vector3.Zero, 1f);
        Scene scene = new(string.Empty, [camera, cube]);

        ViewportPicker.Pick(scene, new Vector2(5f, 5f), Viewport).Should().BeNull();
    }

    [Fact]
    public void Pick_TakesTheNearestOfSeveralObjectsOnTheRay()
    {
        GameObject camera = CameraAt(new Vector3(0f, 0f, 10f));
        GameObject far = CubeAt(new Vector3(0f, 0f, 0f), 1f);
        GameObject near = CubeAt(new Vector3(0f, 0f, 4f), 1f);
        Scene scene = new(string.Empty, [camera, far, near]);

        ViewportPicker.Pick(scene, Centre, Viewport).Should().BeSameAs(near);
    }

    [Fact]
    public void Pick_IgnoresWhatIsBehindTheCamera()
    {
        GameObject camera = CameraAt(new Vector3(0f, 0f, 5f));
        GameObject behind = CubeAt(new Vector3(0f, 0f, 12f), 1f);
        Scene scene = new(string.Empty, [camera, behind]);

        ViewportPicker.Pick(scene, Centre, Viewport).Should().BeNull("nothing is in front of the camera");
    }

    [Fact]
    public void Pick_SelectsTheObjectThatIsOnScreenWhereTheClickWas()
    {
        GameObject cameraObject = CameraAt(new Vector3(0f, 0f, 10f));
        Camera camera = cameraObject.GetComponent<Camera>()!;
        GameObject left = CubeAt(new Vector3(-4f, 0f, 0f), 1f);
        GameObject right = CubeAt(new Vector3(4f, 0f, 0f), 1f);
        GameObject up = CubeAt(new Vector3(0f, 3f, 0f), 1f);
        Scene scene = new(string.Empty, [cameraObject, left, right, up]);

        // Where each object is drawn, clicked: left of the middle picks what is drawn on the left, and a click
        // above the middle picks what is drawn above it (screen Y runs down, world +Y runs up).
        ViewportPicker.Pick(scene, Project(camera, left.Transform.Position, Viewport), Viewport).Should().BeSameAs(left);
        ViewportPicker.Pick(scene, Project(camera, right.Transform.Position, Viewport), Viewport).Should().BeSameAs(right);

        Vector2 above = Project(camera, up.Transform.Position, Viewport);
        above.Y.Should().BeLessThan(Centre.Y);
        ViewportPicker.Pick(scene, above, Viewport).Should().BeSameAs(up);
    }

    [Fact]
    public void Pick_NeverSelectsTheCameraItself()
    {
        // The camera is drawn as a mesh here, so the click really would land on it: the ray starts inside its box.
        GameObject camera = CameraAt(Vector3.Zero);
        camera.AddComponent(new MeshRenderer { Mesh = Mesh.CreateCube(), MeshName = "Camera" });
        Scene scene = new(string.Empty, [camera, CubeAt(new Vector3(0f, 0f, -5f), 1f)]);

        ViewportPicker.Pick(scene, Centre, Viewport).Should().NotBeSameAs(camera);

        GameObject loneCamera = CameraAt(Vector3.Zero);
        loneCamera.AddComponent(new MeshRenderer { Mesh = Mesh.CreateCube(), MeshName = "Camera" });
        Scene cameraOnly = new(string.Empty, [loneCamera]);
        ViewportPicker.Pick(cameraOnly, Centre, Viewport).Should().BeNull("a camera is not something you click on");
    }

    [Fact]
    public void Pick_NeverSelectsTheSkybox()
    {
        // A real skybox fills the view, so without the rule the click would always land on it.
        GameObject camera = CameraAt(new Vector3(0f, 0f, 10f));
        GameObject skybox = new("Sky");
        skybox.Transform.Position = new Vector3(0f, 0f, 5f);
        skybox.Transform.Scale = new Vector3(4f);
        skybox.AddComponent(new MeshRenderer { Mesh = Mesh.CreateCube(), MeshName = "Sky" });
        skybox.AddComponent(new Skybox(new StubTexture(), "Sky"));
        Scene scene = new(string.Empty, [camera, skybox]);

        ViewportPicker.Pick(scene, Centre, Viewport).Should().BeNull();
    }

    [Fact]
    public void Pick_ReturnsNothingWithoutACameraOrAViewport()
    {
        ViewportPicker.Pick(null, Centre, Viewport).Should().BeNull();
        ViewportPicker.Pick(new Scene(string.Empty, [CubeAt(Vector3.Zero, 1f)]), Centre, Viewport)
            .Should().BeNull("a scene without a camera cannot be looked through");

        Scene scene = new(string.Empty, [CameraAt(new Vector3(0f, 0f, 5f)), CubeAt(Vector3.Zero, 1f)]);

        ViewportPicker.Pick(scene, Centre, Vector2.Zero).Should().BeNull();
        ViewportPicker.Pick(scene, new Vector2(-1f, 10f), Viewport).Should().BeNull();
        ViewportPicker.Pick(scene, new Vector2(Viewport.X + 1f, 10f), Viewport).Should().BeNull();
    }

    [Fact]
    public void Pick_WorksWithANarrowViewport()
    {
        // A different aspect ratio must not shift what the middle of the screen looks at.
        GameObject camera = CameraAt(new Vector3(0f, 0f, 5f));
        GameObject cube = CubeAt(Vector3.Zero, 1f);
        Scene scene = new(string.Empty, [camera, cube]);

        ViewportPicker.Pick(scene, new Vector2(200f, 300f), new Vector2(400f, 600f)).Should().BeSameAs(cube);
    }

    [Fact]
    public void TryCreateRay_PointsWhereThePixelLooks()
    {
        Camera camera = CameraAt(new Vector3(0f, 0f, 5f)).GetComponent<Camera>()!;

        ViewportPicker.TryCreateRay(camera, Centre, Viewport, out Ray centre).Should().BeTrue();
        centre.Origin.Should().Be(new Vector3(0f, 0f, 5f), "the ray starts at the camera");
        centre.Direction.Z.Should().BeApproximately(-1f, 0.001f, "an unrotated camera looks down -Z");
        centre.Direction.X.Should().BeApproximately(0f, 0.001f);
        centre.Direction.Y.Should().BeApproximately(0f, 0.001f);

        ViewportPicker.TryCreateRay(camera, new Vector2(790f, Centre.Y), Viewport, out Ray right).Should().BeTrue();
        right.Direction.X.Should().BeGreaterThan(0f, "the right of the viewport looks to the camera's right");

        ViewportPicker.TryCreateRay(camera, new Vector2(Centre.X, 10f), Viewport, out Ray top).Should().BeTrue();
        top.Direction.Y.Should().BeGreaterThan(0f, "the top of the viewport looks up");
    }

    [Fact]
    public void TryCreateRay_RefusesPixelsOutsideTheViewport()
    {
        Camera camera = CameraAt(new Vector3(0f, 0f, 5f)).GetComponent<Camera>()!;

        ViewportPicker.TryCreateRay(camera, new Vector2(-0.5f, 10f), Viewport, out _).Should().BeFalse();
        ViewportPicker.TryCreateRay(camera, Centre, new Vector2(0f, 600f), out _).Should().BeFalse();
        ViewportPicker.TryCreateRay(new Camera(), Centre, Viewport, out _)
            .Should().BeFalse("a camera that is not on an object has no position to cast from");
    }

    private static GameObject CameraAt(Vector3 position)
    {
        GameObject gameObject = new("Camera");
        gameObject.Transform.Position = position;
        gameObject.AddComponent(new Camera());

        return gameObject;
    }

    private static GameObject CubeAt(Vector3 position, float scale)
    {
        GameObject gameObject = new("Cube");
        gameObject.Transform.Position = position;
        gameObject.Transform.Scale = new Vector3(scale);
        gameObject.AddComponent(new MeshRenderer { Mesh = Mesh.CreateCube(), MeshName = "Cube" });

        return gameObject;
    }

    /// <summary>Where a world position is drawn, in viewport pixels — the projection the picker has to invert.</summary>
    private static Vector2 Project(Camera camera, Vector3 world, Vector2 viewport)
    {
        Matrix4x4 viewProjection = camera.GetViewMatrix() * camera.GetProjectionMatrix(viewport.X / viewport.Y);
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        Vector3 screen = new(clip.X / clip.W, clip.Y / clip.W, 0f);

        return new Vector2(
            (screen.X + 1f) * 0.5f * viewport.X,
            (1f - screen.Y) * 0.5f * viewport.Y);
    }

    private sealed class StubTexture : ITexture
    {
        public object GetBackendHandle() => new();

        public void Dispose()
        {
        }
    }
}
