using System.Text;
using AwesomeAssertions;
using Overcore.Core;
using Xunit;

namespace Overcore.Tests;

public class AssetBundleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Overcore.Tests", Guid.NewGuid().ToString("N"));

    public AssetBundleTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void BundleRoundTripsFilesAndExcludesBuildOutputAndItself()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Textures"));
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        File.WriteAllText(Path.Combine(_root, "Textures", "sample.txt"), "asset contents");
        File.WriteAllBytes(Path.Combine(_root, "empty.dat"), []);
        File.WriteAllText(Path.Combine(_root, "project.yaml"), "config");
        File.WriteAllText(Path.Combine(_root, "bin", "generated.txt"), "generated");
        string bundlePath = Path.Combine(_root, "Assets.pak");

        AssetBundle.Create(bundlePath, _root);
        using var bundle = AssetBundle.Load(bundlePath);

        bundle.Should().NotBeNull();
        bundle!.HasFile("textures\\SAMPLE.txt").Should().BeTrue();
        bundle.HasFile("empty.dat").Should().BeTrue();
        bundle.HasFile("project.yaml").Should().BeFalse();
        bundle.HasFile("bin/generated.txt").Should().BeFalse();
        bundle.HasFile("Assets.pak").Should().BeFalse();
        bundle.Open("missing.txt").Should().BeNull();
        using var stream = bundle.Open("Textures/sample.txt");
        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        reader.ReadToEnd().Should().Be("asset contents");
        using var empty = bundle.Open("empty.dat");
        empty.Should().NotBeNull();
        empty!.Length.Should().Be(0);
    }

    [Fact]
    public void MissingAndInvalidBundlesAreRejected()
    {
        AssetBundle.Load(Path.Combine(_root, "missing.pak")).Should().BeNull();
        string path = Path.Combine(_root, "invalid.pak");
        File.WriteAllText(path, "invalid bundle");
        AssetBundle.Load(path).Should().BeNull();
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
