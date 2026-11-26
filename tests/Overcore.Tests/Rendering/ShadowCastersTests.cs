using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Rendering.Shadows;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// Which objects the depth pass draws, and from which side of their faces. Every rule here is one that looks obviously
/// right inside a draw loop and is wrong in one of six ways, so each is pinned without a GPU.
/// </summary>
public class ShadowCastersTests
{
    private static GameObject Renderer(string name = "caster", ShadowCastingMode mode = ShadowCastingMode.On)
    {
        GameObject gameObject = new(name);

        // Vertex is a struct of positional fields, so it is built through its constructor rather than an initializer.
        Mesh mesh = new(
            [
                new Vertex(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ, default, default),
                new Vertex(System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitZ, default, default),
                new Vertex(System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ, default, default)
            ],
            [0u, 1u, 2u]);

        gameObject.AddComponent(new MeshRenderer
        {
            Mesh = mesh,
            MeshName = "triangle",
            CastShadows = mode
        });

        return gameObject;
    }

    // ---------------------------------------------------------------- what casts

    [Fact]
    public void AnOrdinaryRendererCastsAndIsVisible()
    {
        ShadowCasters.RoleOf(Renderer()).Should().Be(CasterRole.CasterAndVisible);
    }

    [Fact]
    public void ADisabledRendererCastsNothing()
    {
        GameObject gameObject = Renderer();
        gameObject.GetComponent<MeshRenderer>()!.Enabled = false;

        ShadowCasters.IsCaster(gameObject).Should().BeFalse();
    }

    [Fact]
    public void AnObjectWithNoRendererCastsNothing()
    {
        ShadowCasters.IsCaster(new GameObject("empty")).Should().BeFalse();
    }

    [Fact]
    public void OffCastsNothingAndStaysVisible()
    {
        GameObject gameObject = Renderer(mode: ShadowCastingMode.Off);

        ShadowCasters.IsCaster(gameObject).Should().BeFalse();
        ShadowCasters.IsVisible(gameObject).Should().BeTrue();
    }

    [Fact]
    public void ShadowsOnlyCastsWithoutBeingDrawn()
    {
        // The mode's entire purpose: an object that occludes but is not itself seen. Getting this backwards is a
        // ghost object in the colour pass with no shadow under it.
        GameObject gameObject = Renderer(mode: ShadowCastingMode.ShadowsOnly);

        ShadowCasters.IsCaster(gameObject).Should().BeTrue();
        ShadowCasters.IsVisible(gameObject).Should().BeFalse();
        ShadowCasters.RoleOf(gameObject).Should().Be(CasterRole.Caster);
    }

    [Fact]
    public void TheSkyboxIsNeverACaster()
    {
        // The sky is a source of light, not an occluder. A depth map holding the sky is a depth map in which nothing
        // outside the sky's silhouette exists — and a scene drawn entirely in shade.
        GameObject gameObject = Renderer("sky");
        gameObject.AddComponent(new Skybox(new FakeTexture(), "sky"));

        ShadowCasters.IsCaster(gameObject).Should().BeFalse();
        ShadowCasters.RoleOf(gameObject).Should().Be(CasterRole.NotACaster);
    }

    /// <summary>A texture with no backend, which is all the caster rules ever ask of one.</summary>
    private sealed class FakeTexture : ITexture
    {
        public object GetBackendHandle() => new object();

        public void Dispose() { }
    }

    // ---------------------------------------------------------------- the hierarchy

    [Fact]
    public void AChildOfAnInactiveObjectCastsNothing()
    {
        // SetActive is not inherited into a child's own flag: the child's ActiveSelf stays true while nothing on it
        // is drawn. A pass that checks ActiveSelf is a pass that draws shadows for a hidden subtree.
        GameObject parent = Renderer("parent");
        GameObject child = Renderer("child");
        child.Transform.Parent = parent.Transform;
        parent.SetActive(false);

        child.ActiveSelf.Should().BeTrue();
        child.ActiveInHierarchy.Should().BeFalse();
        ShadowCasters.IsCaster(child).Should().BeFalse();
    }

    [Fact]
    public void ReactivatingTheParentBringsTheChildsShadowBack()
    {
        GameObject parent = Renderer("parent");
        GameObject child = Renderer("child");
        child.Transform.Parent = parent.Transform;

        parent.SetActive(false);
        ShadowCasters.IsCaster(child).Should().BeFalse();

        parent.SetActive(true);
        ShadowCasters.IsCaster(child).Should().BeTrue();
    }

    // ---------------------------------------------------------------- which faces

    [Fact]
    public void AnOrdinaryCasterCullsItsFrontFaces()
    {
        // Front-face culling moves the recorded depth behind the surface, which is the change that buys the receiver
        // the most freedom in its bias.
        ShadowCasters.CullsBackFaces(Renderer()).Should().BeTrue();
    }

    [Fact]
    public void ATwoSidedCasterCullsNothing()
    {
        // A foliage card has no back face to cull to, and culling one removes the shadow entirely from one side.
        ShadowCasters.CullsBackFaces(Renderer(mode: ShadowCastingMode.TwoSided)).Should().BeFalse();
    }

    // ---------------------------------------------------------------- the list

    [Fact]
    public void CollectKeepsSceneOrderAndDropsTheNonCasters()
    {
        GameObject first = Renderer("first");
        GameObject second = Renderer("second", ShadowCastingMode.ShadowsOnly);
        GameObject hidden = Renderer("off", ShadowCastingMode.Off);

        List<GameObject> casters = [.. ShadowCasters.Collect([first, hidden, second])];

        casters.Should().Equal(first, second);
    }

    [Fact]
    public void CollectOfNothingIsNothing()
    {
        ShadowCasters.Collect(null).Should().BeEmpty();
        ShadowCasters.Collect([]).Should().BeEmpty();
    }
}
