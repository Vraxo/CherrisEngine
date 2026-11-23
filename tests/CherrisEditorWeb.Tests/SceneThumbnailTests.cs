using AwesomeAssertions;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers the scene preview: what the content browser can learn from a scene file without loading it, and the
/// geometry that turns that into a picture.
/// </summary>
public class SceneThumbnailTests
{
    // The shape SceneSerializer writes: a GameObjects sequence of objects, each with a Transform and the
    // components it carries.
    private const string Scene =
        """
        GameObjects:
        - Id: 6f8f4e1c-0f1a-4c1e-9a2b-1f4d3c2b1a09
          Name: Cube
          Components:
            Transform:
              Position: [0, 0.75, 0]
              Rotation: [0, 45, 0]
              Scale: [1.5, 1.5, 1.5]
            MeshRenderer:
              Mesh: Cube
              Material:
                Texture: White
        - Id: 2c1b0a99-8d7e-4f6a-b5c4-d3e2f1a0b9c8
          Name: Sun
          Components:
            Transform:
              Position: [2, 3, -4]
              Rotation: [0, 0, 0]
              Scale: [1, 1, 1]
            Light:
              Type: Directional
              Intensity: 1.1
        - Id: 5a4b3c2d-1e0f-4a9b-8c7d-6e5f4a3b2c1d
          Name: Camera
          Components:
            Transform:
              Position: [-6, 2.2, 8]
              Rotation: [0, 90, 0]
              Scale: [1, 1, 1]
            Camera:
              FieldOfView: 60
        """;

    [Fact]
    public void Parse_ReadsNamesPositionsScaleAndWhatEachObjectIs()
    {
        SceneThumbnail? thumbnail = SceneThumbnail.Parse(Scene);

        thumbnail.Should().NotBeNull();
        thumbnail!.Objects.Should().HaveCount(3);

        ThumbnailObject cube = thumbnail.Objects[0];
        cube.Name.Should().Be("Cube");
        cube.Kind.Should().Be(ThumbnailObjectKind.Mesh);
        cube.X.Should().Be(0f);
        cube.Z.Should().Be(0f);
        cube.Width.Should().Be(1.5f, "a mesh footprint is the ground its scale describes");
        cube.Depth.Should().Be(1.5f);
        cube.YawRadians.Should().BeApproximately(MathF.PI / 4f, 0.001f, "the file stores Euler degrees");

        ThumbnailObject sun = thumbnail.Objects[1];
        sun.Kind.Should().Be(ThumbnailObjectKind.Light);
        sun.X.Should().Be(2f);
        sun.Z.Should().Be(-4f);
        sun.Width.Should().Be(0f, "a marker has no footprint of its own");

        ThumbnailObject camera = thumbnail.Objects[2];
        camera.Kind.Should().Be(ThumbnailObjectKind.Camera);
        camera.X.Should().Be(-6f);
        camera.Z.Should().Be(8f);
    }

    [Fact]
    public void Parse_TreatsAnObjectWithoutAVisibleComponentAsOther()
    {
        SceneThumbnail? thumbnail = SceneThumbnail.Parse(
            """
            GameObjects:
            - Name: Controller
              Components:
                Transform:
                  Position: [1, 0, 2]
                  Rotation: [0, 0, 0]
                  Scale: [1, 1, 1]
                Script:
                  Path: Player.lua
            """);

        thumbnail.Should().NotBeNull();
        ThumbnailObject item = thumbnail!.Objects.Should().ContainSingle().Which;
        item.Kind.Should().Be(ThumbnailObjectKind.Other);
        item.X.Should().Be(1f);
        item.Z.Should().Be(2f);
    }

    [Fact]
    public void Parse_ReadsAVectorWrittenOutOverSeveralLines()
    {
        SceneThumbnail? thumbnail = SceneThumbnail.Parse(
            """
            GameObjects:
            - Name: Floor
              Components:
                Transform:
                  Position:
                  - 0
                  - 0
                  - 0
                  Rotation:
                  - 0
                  - 0
                  - 0
                  Scale:
                  - 20
                  - 1
                  - 20
                MeshRenderer:
                  Mesh: Plane
            """);

        ThumbnailObject floor = thumbnail!.Objects.Should().ContainSingle().Which;
        floor.Kind.Should().Be(ThumbnailObjectKind.Mesh);
        floor.Width.Should().Be(20f);
        floor.Depth.Should().Be(20f);
    }

    [Fact]
    public void Parse_AcceptsAnEmptyScene()
    {
        SceneThumbnail? thumbnail = SceneThumbnail.Parse("GameObjects: []");

        thumbnail.Should().NotBeNull("a new project's start scene is an empty scene, not a broken file");
        thumbnail!.IsEmpty.Should().BeTrue();
        thumbnail.Objects.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReturnsNullForAnythingThatIsNotAScene()
    {
        SceneThumbnail.Parse("Name: MyGame\nStartScene: Main.yaml\nPackAssets: false").Should()
            .BeNull("project.yaml is the project descriptor, not a scene");
        SceneThumbnail.Parse("# just a comment").Should().BeNull();
        SceneThumbnail.Parse(": : :").Should().BeNull("broken YAML keeps the file's ordinary icon");
        SceneThumbnail.Parse(string.Empty).Should().BeNull();
        SceneThumbnail.Parse(null).Should().BeNull();
    }

    [Fact]
    public void Parse_KeepsGoingWhenAnObjectHasNoTransform()
    {
        SceneThumbnail? thumbnail = SceneThumbnail.Parse(
            """
            GameObjects:
            - Name: Loose
              Components:
                MeshRenderer:
                  Mesh: Cube
            """);

        ThumbnailObject loose = thumbnail!.Objects.Should().ContainSingle().Which;
        loose.X.Should().Be(0f);
        loose.Z.Should().Be(0f);
        loose.Width.Should().Be(1f, "without a scale the footprint is a unit square");
    }

    [Fact]
    public void Layout_PutsEveryShapeInsideTheBox()
    {
        const float width = 76f;
        const float height = 46f;
        IReadOnlyList<ThumbnailShape> shapes = SceneThumbnailLayout.Fit(SceneThumbnail.Parse(Scene)!, width, height);

        shapes.Should().HaveCount(3);

        foreach (ThumbnailShape shape in shapes)
        {
            (shape.X - (shape.Width / 2f)).Should().BeGreaterThanOrEqualTo(0f);
            (shape.Y - (shape.Height / 2f)).Should().BeGreaterThanOrEqualTo(0f);
            (shape.X + (shape.Width / 2f)).Should().BeLessThanOrEqualTo(width);
            (shape.Y + (shape.Height / 2f)).Should().BeLessThanOrEqualTo(height);
        }
    }

    [Fact]
    public void Layout_UsesOneScaleForBothAxes()
    {
        // Two objects a known distance apart, in X and in Z: what fits the picture has to keep the ratio.
        SceneThumbnail thumbnail = SceneThumbnail.Parse(
            """
            GameObjects:
            - Name: Left
              Components:
                Transform:
                  Position: [0, 0, 0]
                  Scale: [2, 1, 2]
                MeshRenderer:
                  Mesh: Cube
            - Name: Right
              Components:
                Transform:
                  Position: [10, 0, 4]
                  Scale: [2, 1, 2]
                MeshRenderer:
                  Mesh: Cube
            """)!;

        IReadOnlyList<ThumbnailShape> shapes = SceneThumbnailLayout.Fit(thumbnail, 76f, 46f);

        float screenX = shapes[1].X - shapes[0].X;
        float screenY = shapes[1].Y - shapes[0].Y;

        (screenX / 10f).Should().BeApproximately(screenY / 4f, 0.001f, "a square on the floor stays square");
        screenX.Should().BeGreaterThan(0f);
        screenY.Should().BeGreaterThan(0f, "world +Z runs down the picture");
    }

    [Fact]
    public void Layout_ScalesFootprintsWithTheScene()
    {
        SceneThumbnail thumbnail = SceneThumbnail.Parse(
            """
            GameObjects:
            - Name: Small
              Components:
                Transform:
                  Position: [0, 0, 0]
                  Scale: [1, 1, 1]
                MeshRenderer:
                  Mesh: Cube
            - Name: Large
              Components:
                Transform:
                  Position: [4, 0, 0]
                  Scale: [2, 1, 2]
                MeshRenderer:
                  Mesh: Cube
            """)!;

        IReadOnlyList<ThumbnailShape> shapes = SceneThumbnailLayout.Fit(thumbnail, 76f, 46f);

        (shapes[1].Width / shapes[0].Width).Should().BeApproximately(2f, 0.001f);
    }

    [Fact]
    public void Layout_CentresASingleObject()
    {
        SceneThumbnail thumbnail = SceneThumbnail.Parse(
            """
            GameObjects:
            - Name: Only
              Components:
                Transform:
                  Position: [3, 0, -7]
                  Scale: [1, 1, 1]
                MeshRenderer:
                  Mesh: Cube
            """)!;

        ThumbnailShape shape = SceneThumbnailLayout.Fit(thumbnail, 76f, 46f).Should().ContainSingle().Which;

        shape.X.Should().BeApproximately(38f, 0.001f);
        shape.Y.Should().BeApproximately(23f, 0.001f);
    }

    [Fact]
    public void Layout_PointsACameraMarkerWhereTheCameraLooks()
    {
        SceneThumbnail facing = SceneThumbnail.Parse(
            """
            GameObjects:
            - Name: Camera
              Components:
                Transform:
                  Position: [0, 0, 0]
                  Rotation: [0, 0, 0]
                Camera:
                  FieldOfView: 60
            """)!;

        // Rotation 0 looks down -Z, which is up the picture.
        SceneThumbnailLayout.Fit(facing, 76f, 46f).Should().ContainSingle().Which.AngleDegrees
            .Should().BeApproximately(-90f, 0.001f);

        SceneThumbnail turned = SceneThumbnail.Parse(
            """
            GameObjects:
            - Name: Camera
              Components:
                Transform:
                  Position: [0, 0, 0]
                  Rotation: [0, 90, 0]
                Camera:
                  FieldOfView: 60
            """)!;

        // A quarter turn puts it looking down -X, i.e. to the left.
        SceneThumbnailLayout.Fit(turned, 76f, 46f).Should().ContainSingle().Which.AngleDegrees
            .Should().BeApproximately(180f, 0.001f);
    }

    [Fact]
    public void Layout_HasNothingToDrawForAnEmptyScene()
    {
        SceneThumbnailLayout.Fit(SceneThumbnail.Empty, 76f, 46f).Should().BeEmpty();
        SceneThumbnailLayout.Fit(SceneThumbnail.Empty, 0f, 46f).Should().BeEmpty();
    }
}
