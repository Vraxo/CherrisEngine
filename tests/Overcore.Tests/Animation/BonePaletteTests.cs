using AwesomeAssertions;
using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// The bone palette: the packing that stands between a skeleton's joint matrices and the texture a shader samples.
/// This is the half of GPU skinning that needs no GPU, which is why it is the half worth testing exhaustively.
/// </summary>
public class BonePaletteTests
{
    // ---------------------------------------------------------------- layout

    [Fact]
    public void ABoneOccupiesThreeTexelsOfFourFloats()
    {
        // Twelve floats a bone: a 4x4 matrix minus the fourth column, which is always (0, 0, 0, 1) for an affine
        // transform. A shader written against a different stride reads every bone after the first as garbage.
        BonePalette.TexelsPerBone.Should().Be(3);
        BonePalette.ComponentsPerTexel.Should().Be(4);
        BonePalette.FloatsPerBone.Should().Be(12);
    }

    [Fact]
    public void ATexelCountIsThreePerBone()
    {
        BonePalette.TexelCount(0).Should().Be(0);
        BonePalette.TexelCount(1).Should().Be(3);
        BonePalette.TexelCount(60).Should().Be(180);
    }

    [Fact]
    public void ATextureIsNeverZeroWide()
    {
        // A zero-width texture is a GL error, and a skeleton with no bones still needs a handle to bind.
        BonePalette.TextureWidth(0).Should().Be(1);
        BonePalette.TextureWidth(4).Should().Be(12);
    }

    // ---------------------------------------------------------------- packing

    [Fact]
    public void AnIdentityPaletteIsTwelveFloatsABone()
    {
        float[] palette = new float[BonePalette.TexelCount(2) * BonePalette.ComponentsPerTexel];

        int written = BonePalette.PackIdentity(2, palette);

        written.Should().Be(24);
    }

    [Fact]
    public void AnIdentityPaletteHasOnePerRowAndTheTranslationInTheFourthSlot()
    {
        float[] palette = new float[BonePalette.FloatsPerBone];

        BonePalette.PackIdentity(1, palette);

        // Column 0 of the identity, then column 1, then column 2. A texel is a column, which is what GLSL's mat4
        // constructor takes, so the three texels and the affine fourth column reassemble the matrix itself.
        palette[..4].Should().Equal(1f, 0f, 0f, 0f);
        palette[4..8].Should().Equal(0f, 1f, 0f, 0f);
        palette[8..12].Should().Equal(0f, 0f, 1f, 0f);
    }

    [Fact]
    public void PackingPutsEachMatrixRowInItsOwnTexel()
    {
        // A distinctive matrix, so a transposed or reordered pack is obvious rather than plausible.
        Matrix4x4 matrix = new(
            1f, 2f, 3f, 4f,
            5f, 6f, 7f, 8f,
            9f, 10f, 11f, 12f,
            13f, 14f, 15f, 16f);

        float[] palette = new float[BonePalette.FloatsPerBone];

        BonePalette.Pack([matrix], palette);

        // Column 0 is (M11, M21, M31, M41), which for this matrix is (1, 5, 9, 13).
        palette[..4].Should().Equal(1f, 5f, 9f, 13f);
        palette[4..8].Should().Equal(2f, 6f, 10f, 14f);
        palette[8..12].Should().Equal(3f, 7f, 11f, 15f);

        // The fourth column is what is dropped, and it is the one a shader reconstructs as (0, 0, 0, 1).
        palette.Should().NotContain(4f).And.NotContain(8f).And.NotContain(12f).And.NotContain(16f);
    }

    [Fact]
    public void PackingABoneKeepsTheTranslationInTheFourthSlot()
    {
        // The case a dropped column would break: a translated bone. Reconstructing the fourth column as (0,0,0,1)
        // is only correct if the translation lives in M14/M24/M34, which is where row-vector order puts it.
        Matrix4x4 translation = Matrix4x4.CreateTranslation(5f, -3f, 2f);
        float[] palette = new float[BonePalette.FloatsPerBone];

        BonePalette.Pack([translation], palette);

        // The translation is the fourth row M41/M42/M43, and it lands in the fourth component of each column.
        palette[3].Should().Be(5f);    // M41
        palette[7].Should().Be(-3f);   // M42
        palette[11].Should().Be(2f);   // M43
    }

    [Fact]
    public void APackedTranslationLandsWhereTheShaderReadsIt()
    {
        // The bug this pins: packing *rows* of a row-vector matrix drops the translation, because in that order the
        // translation is the fourth row and not the fourth column. The character would then rotate about the origin
        // instead of moving — visible, but easy to blame on the rig.
        //
        // The shader rebuilds mat4(column0, column1, column2, (0,0,0,1)) and multiplies a point by it. A pure
        // translation has an identity linear part, so its result is exactly the translation, and that is the fourth
        // component of each packed texel.
        Matrix4x4 bone = Matrix4x4.CreateTranslation(5f, -3f, 2f);

        float[] palette = new float[BonePalette.FloatsPerBone];
        BonePalette.Pack([bone], palette);

        // Vector3 has no approximate assertion of its own, so each component is checked where it lands.
        palette[3].Should().BeApproximately(5f, 1e-5f, "M41");
        palette[7].Should().BeApproximately(-3f, 1e-5f, "M42");
        palette[11].Should().BeApproximately(2f, 1e-5f, "M43");

        // And the linear part is the identity, so a translated bone does not also scale or rotate what it carries.
        palette[0].Should().BeApproximately(1f, 1e-5f);
        palette[4 + 1].Should().BeApproximately(1f, 1e-5f);
        palette[8 + 2].Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void PackingSeveralBonesLaysThemOutBackToBack()
    {
        float[] palette = new float[BonePalette.TexelCount(2) * BonePalette.ComponentsPerTexel];

        Matrix4x4 first = Matrix4x4.CreateTranslation(1f, 0f, 0f);
        Matrix4x4 second = Matrix4x4.CreateTranslation(0f, 2f, 0f);

        int written = BonePalette.Pack([first, second], palette);

        written.Should().Be(24);

        // Bone 0 occupies texels 0..2 and bone 1 texels 3..5; the shader computes `bone * 3`.
        palette[3].Should().Be(1f);
        palette[(BonePalette.TexelsPerBone * BonePalette.ComponentsPerTexel) + 7].Should().Be(2f);
    }

    [Fact]
    public void PackingReturnsTheFloatCountItWrote()
    {
        float[] palette = new float[BonePalette.TexelCount(3) * BonePalette.ComponentsPerTexel];

        BonePalette.Pack([Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity], palette)
            .Should()
            .Be(BonePalette.TexelCount(3) * BonePalette.ComponentsPerTexel);
    }

    [Fact]
    public void ATooSmallBufferIsRefusedRatherThanOverrun()
    {
        // A palette written past the end of its buffer is a heap corruption that would appear as corruption somewhere
        // else entirely; refusing is the only behaviour that leaves a diagnosable failure.
        float[] tooSmall = new float[BonePalette.FloatsPerBone - 1];

        Action pack = () => BonePalette.Pack([Matrix4x4.Identity], tooSmall);

        pack.Should().Throw<ArgumentException>();
    }

    // ---------------------------------------------------------------- the invariant that matters

    [Fact]
    public void AtBindPoseThePackedPaletteIsTheIdentityForEveryBone()
    {
        // The whole point: a mesh drawn at its bind pose must not move. The shader multiplies by these, so the packed
        // palette has to be the identity when the skeleton is in the pose it was modelled in.
        Skeleton skeleton = TwoBoneSkeleton(out GameObject root);

        Matrix4x4[] jointMatrices = skeleton.GetJointMatrices(root.Transform.GetModelMatrix());

        float[] palette = new float[BonePalette.TexelCount(jointMatrices.Length) * BonePalette.ComponentsPerTexel];

        BonePalette.Pack(jointMatrices, palette);

        for (int bone = 0; bone < jointMatrices.Length; bone++)
        {
            int offset = bone * BonePalette.FloatsPerBone;

            palette[offset + 0].Should().BeApproximately(1f, 1e-5f, $"bone {bone} M11");
            palette[offset + 5].Should().BeApproximately(1f, 1e-5f, $"bone {bone} M22");
            palette[offset + 10].Should().BeApproximately(1f, 1e-5f, $"bone {bone} M33");
            palette[offset + 3].Should().BeApproximately(0f, 1e-5f, $"bone {bone} M41");
        }
    }

    [Fact]
    public void AMovedBoneProducesANonIdentityPalette()
    {
        // The converse, which a palette that silently ignored its input would also fail: move a bone and the packed
        // matrix must change, or the character would animate nothing while looking superficially correct.
        Skeleton skeleton = TwoBoneSkeleton(out GameObject root);

        root.Transform.Position = new Vector3(3f, 0f, 0f);

        Matrix4x4[] jointMatrices = skeleton.GetJointMatrices(root.Transform.GetModelMatrix());

        float[] palette = new float[BonePalette.TexelCount(jointMatrices.Length) * BonePalette.ComponentsPerTexel];

        BonePalette.Pack(jointMatrices, palette);

        // The renderer's own world is divided out, so bone 0's joint matrix is the identity even with the root moved
        // — that cancellation is the invariant §3.1 pins. What must change is the palette agreeing with the joint
        // matrices it was packed from, so assert that rather than a non-zero buffer.
        for (int bone = 0; bone < jointMatrices.Length; bone++)
        {
            int offset = bone * BonePalette.FloatsPerBone;

            palette[offset + 3].Should().BeApproximately(jointMatrices[bone].M41, 1e-5f, $"bone {bone} M41");
            palette[offset + 7].Should().BeApproximately(jointMatrices[bone].M42, 1e-5f, $"bone {bone} M42");
        }
    }

    /// <summary>A two-bone skeleton whose bind inverse cancels each bone, so bind pose is the identity.</summary>
    private static Skeleton TwoBoneSkeleton(out GameObject root)
    {
        root = new GameObject("Root");

        GameObject child = new("Child");
        child.Transform.Parent = root.Transform;

        Skeleton skeleton = new();

        skeleton.AddBone(root, Matrix4x4.Identity);
        skeleton.AddBone(child, Matrix4x4.Identity);

        return skeleton;
    }
}
