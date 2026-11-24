using AwesomeAssertions;
using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Serialization;
using System.Numerics;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers what a new scene is: the objects it starts with, the file it is written as, and whether that file can be
/// read back into the same scene.
/// </summary>
public class SceneTemplateTests
{
    [Fact]
    public void CreateObjects_GivesTheSceneACameraAndASun()
    {
        List<GameObject> objects = SceneTemplate.CreateObjects();

        objects.Should().HaveCount(2);

        GameObject camera = objects[0];
        camera.Name.Should().Be("Camera");
        camera.GetComponent<Camera>().Should().NotBeNull();
        camera.Transform.Position.Should().Be(SceneTemplate.DefaultCameraPosition);
        camera.Transform.Rotation.Should().Be(SceneTemplate.DefaultCameraRotation);

        GameObject sun = objects[1];
        sun.GetComponent<Light>().Should().NotBeNull("a scene with no light renders black");
        sun.GetComponent<Light>()!.Type.Should().Be(LightType.Directional);
    }

    [Fact]
    public void Write_PutsAReadableSceneOnDisk()
    {
        using TemporaryFolder folder = new();

        string path = SceneTemplate.Write(folder.Path, "Level 1");

        path.Should().Be(Path.Combine(folder.Path, "Level 1.yaml"));
        File.Exists(path).Should().BeTrue();

        // The objects carry generated ids, so two serialisations of "the same scene" legitimately differ; what has to
        // hold is that the file describes the two objects, read back through the preview the content browser uses.
        string text = File.ReadAllText(path);

        text.Should().Contain("Camera").And.Contain("Sun");

        SceneThumbnail? preview = SceneThumbnail.Parse(text);

        preview.Should().NotBeNull();
        preview!.Objects.Should().HaveCount(2);
    }

    [Fact]
    public void Write_RefusesToOverwriteASceneThatIsAlreadyThere()
    {
        using TemporaryFolder folder = new();
        string existing = Path.Combine(folder.Path, "Main.yaml");
        File.WriteAllText(existing, "GameObjects: []");

        Action write = () => SceneTemplate.Write(folder.Path, "Main");

        write.Should().Throw<IOException>().WithMessage("*already a scene called Main.yaml*");
        File.ReadAllText(existing).Should().Be("GameObjects: []", "the file that was there must not be touched");
    }

    [Fact]
    public void Write_LoadsBackIntoTheSameScene()
    {
        using TemporaryFolder folder = new();
        string path = SceneTemplate.Write(folder.Path, "Level");

        // The fixture scene holds no renderer, so nothing here reaches the resource manager.
        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        List<GameObject> objects = loader.LoadSceneFromYaml(File.ReadAllText(path));

        objects.Should().HaveCount(2);

        GameObject camera = objects.Single(gameObject => gameObject.GetComponent<Camera>() is not null);
        camera.Name.Should().Be("Camera");

        // Through the file the numbers are text, so they are compared to a thousandth rather than bit for bit.
        camera.Transform.Position.X.Should().BeApproximately(SceneTemplate.DefaultCameraPosition.X, 0.001f);
        camera.Transform.Position.Y.Should().BeApproximately(SceneTemplate.DefaultCameraPosition.Y, 0.001f);
        camera.Transform.Position.Z.Should().BeApproximately(SceneTemplate.DefaultCameraPosition.Z, 0.001f);
        EngineMath.ToEulerAngles(camera.Transform.Rotation).X
            .Should().BeApproximately(EngineMath.ToEulerAngles(SceneTemplate.DefaultCameraRotation).X, 0.001f);

        GameObject sun = objects.Single(gameObject => gameObject.GetComponent<Light>() is not null);
        Light light = sun.GetComponent<Light>()!;
        light.Type.Should().Be(LightType.Directional);
        light.Intensity.Should().BeApproximately(1.1f, 0.001f);
        light.AmbientStrength.Should().BeApproximately(0.35f, 0.001f);
    }

    [Theory]
    [InlineData("Level", "Level.yaml")]
    [InlineData("  Level  ", "Level.yaml")]
    [InlineData("Level.yaml", "Level.yaml")]
    [InlineData("Level.yml", "Level.yml")]
    [InlineData("my_scene-2", "my_scene-2.yaml")]
    public void TryNormalizeName_AddsTheExtensionTheSceneLoaderLooksFor(string typed, string expected)
    {
        SceneTemplate.TryNormalizeName(typed, out string fileName, out string error).Should().BeTrue();

        fileName.Should().Be(expected);
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".hidden")]
    [InlineData("folder/Level")]
    [InlineData("folder\\Level")]
    [InlineData("Level:1")]
    [InlineData("What?")]
    public void TryNormalizeName_RefusesWhatIsNotAFileNameInTheProject(string typed)
    {
        SceneTemplate.TryNormalizeName(typed, out string fileName, out string error).Should().BeFalse();

        fileName.Should().BeEmpty();
        error.Should().NotBeEmpty("a refusal has to say why");
    }

    /// <summary>A folder that cleans itself up, so the tests leave nothing behind.</summary>
    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cherris-scene-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary folder is not worth failing a test over.
            }
        }
    }
}
