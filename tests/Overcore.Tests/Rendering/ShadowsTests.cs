using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
// Deliberately not `using Overcore.Rendering;`: inside `Overcore.Tests.Rendering` a bare `Shadows` would bind to the
// `Overcore.Rendering.Shadows` namespace instead of the component in it, and every use below names that component.
using Overcore.Rendering.Shadows;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// The per-scene shadow settings, and the one property that makes them survivable: <see cref="Shadows.Cascades"/> owns
/// <see cref="Shadows.Splits"/>, so a loaded component cannot disagree with itself.
/// </summary>
/// <remarks>
/// No GPU and no serializer file on disk: these are the rules a scene depends on, stated where a change to them fails
/// a build instead of producing a scene whose shadow distance silently reverted to the default.
/// </remarks>
public class ShadowsTests
{
    // ---------------------------------------------------------------- cascades own splits

    [Fact]
    public void AFreshComponentHasTheDerivedSplits()
    {
        Shadows shadows = new();

        shadows.Cascades.Should().Be(CascadeSplits.DefaultCascades);
        shadows.Splits.Should().Equal(CascadeSplits.Derive(CascadeSplits.DefaultCascades));
    }

    [Fact]
    public void SettingCascadesRewritesTheSplits()
    {
        Shadows shadows = new() { Cascades = 2 };

        shadows.Splits.Should().Equal(CascadeSplits.Derive(2));
    }

    [Fact]
    public void SettingCascadesToOneLeavesNoSplits()
    {
        // One cascade has no boundary to record, and an empty list is what tells the renderer so. A single [1.0]
        // would compare as a boundary at the far plane and cull the last cascade's fragment to nothing.
        Shadows shadows = new() { Cascades = 1 };

        shadows.Splits.Should().BeEmpty();
        shadows.FarDepths().Should().Equal(shadows.Distance);
    }

    [Fact]
    public void ThreeCascadesIsRoundedToFour()
    {
        Shadows shadows = new() { Cascades = 3 };

        shadows.Cascades.Should().Be(4);
        shadows.Splits.Should().HaveCount(3);
    }

    [Fact]
    public void SettingCascadesToItsCurrentValueKeepsAHandEditedSplitList()
    {
        // The setter is what the inspector writes on every keystroke, so it must not throw away an edit that is
        // still consistent with the count. Only a change of count invalidates the list.
        Shadows shadows = new();
        shadows.Splits = [0.2f, 0.5f, 0.8f];

        shadows.Cascades = 4;

        shadows.Splits.Should().Equal(0.2f, 0.5f, 0.8f);
    }

    // ---------------------------------------------------------------- the round-trip

    [Fact]
    public void ALoadedComponentAgreesWithTheOneThatWasSaved()
    {
        // The property the design exists for. A serializer writes only what differs from a new instance, so a fresh
        // instance and a loaded one must compare equal property by property — Splits included, which is why the
        // Cascades setter re-derives them rather than the constructor seeding a list that depends on Distance.
        Shadows saved = new() { Distance = 220f, Cascades = 2 };
        Shadows loaded = new();

        // What the serializer would write, applied in the order it appears in the dictionary.
        loaded.Cascades = saved.Cascades;
        loaded.Distance = saved.Distance;

        loaded.Splits.Should().Equal(saved.Splits);
        loaded.FarDepths().Should().Equal(saved.FarDepths());
    }

    [Fact]
    public void ADistanceThatDiffersFromTheDefaultStillRoundTripsItsSplits()
    {
        Shadows saved = new() { Distance = 40f };
        Shadows loaded = new() { Distance = saved.Distance, Cascades = saved.Cascades };

        loaded.FarDepths().Should().Equal(saved.FarDepths());

        // The fractions are the same at any distance — that is the point of storing fractions — so the depths move
        // and the splits do not.
        loaded.Splits.Should().Equal(CascadeSplits.Derive(CascadeSplits.DefaultCascades));
        loaded.FarDepths()[^1].Should().Be(40f);
    }

    [Fact]
    public void ANullSplitListFallsBackToTheDerivedOnes()
    {
        // A hand-written scene file with an empty ``Splits:`` key handed the setter a null. It used to leave the
        // component holding one and every call after it dereferencing it.
        Shadows shadows = new() { Splits = null! };

        shadows.Splits.Should().Equal(CascadeSplits.Derive(shadows.Cascades));
    }

    // ---------------------------------------------------------------- resolution

    [Theory]
    [InlineData(ShadowResolution.Low, 512)]
    [InlineData(ShadowResolution.Medium, 1024)]
    [InlineData(ShadowResolution.High, 2048)]
    [InlineData(ShadowResolution.VeryHigh, 4096)]
    public void AResolutionLevelIsTheTexelCountTheDocumentPromises(ShadowResolution level, int texels)
    {
        // docs/shadows.md §5.1: the four levels are 512, 1024, 2048 and 4096 texels a side.
        Shadows.TexelsOf(level).Should().Be(texels);
    }

    [Fact]
    public void AnUnrecognisedResolutionLevelFallsBackToTheDefault()
    {
        // A cast from an integer the document did not define. Falling back is the choice that renders; a throw here
        // would take the render loop with it over a setting nobody meant to make fatal.
        Shadows.TexelsOf((ShadowResolution)99).Should().Be(Shadows.DefaultResolution);
    }

    // ---------------------------------------------------------------- texel size

    [Fact]
    public void TheInnerTexelSizeShrinksAsTheMapGrows()
    {
        Shadows shadows = new();

        float atDefault = shadows.InnerTexelSize(0.1f, 16f / 9f, MathF.PI / 3f);
        float atDouble = shadows.InnerTexelSize(0.1f, 16f / 9f, MathF.PI / 3f, 4096);

        atDefault.Should().BeGreaterThan(0f);

        // Twice the texels a side is half the world per texel. This is the relation the normal offset is scaled by,
        // so a mistaken factor here is a shadow that detaches from its caster at every resolution but one.
        (atDefault / 2f).Should().BeApproximately(atDouble, 0.001f);
    }

    // ---------------------------------------------------------------- resolution of the component

    [Fact]
    public void ResolveFindsTheFirstEnabledComponentOnAnActiveObject()
    {
        GameObject active = new("with shadows");
        Shadows wanted = new() { Distance = 90f };
        active.AddComponent(wanted);

        GameObject inactive = new("off");
        inactive.SetActive(false);
        inactive.AddComponent(new Shadows { Distance = 10f });

        Shadows.Resolve([inactive, active]).Distance.Should().Be(90f);
    }

    [Fact]
    public void ResolveFallsBackToTheDefaultsWhenEveryComponentIsDisabled()
    {
        GameObject gameObject = new("disabled");
        gameObject.AddComponent(new Shadows { Distance = 10f, Enabled = false });

        Shadows resolved = Shadows.Resolve([gameObject]);

        resolved.Distance.Should().Be(CascadeSplits.DefaultDistance);
    }

    [Fact]
    public void ResolveGivesEachCallerItsOwnFallback()
    {
        // A cached instance would let one caller's stray write change the defaults for the whole process.
        Shadows first = Shadows.Resolve([]);
        first.Distance = 5f;

        Shadows.Resolve([]).Distance.Should().Be(CascadeSplits.DefaultDistance);
    }
}
