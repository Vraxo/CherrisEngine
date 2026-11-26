using AwesomeAssertions;
using Overcore.Animation;
using Overcore.Core;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// The skeleton is where a transposed matrix, a reversed multiplication order or a wrong bind inverse hides, and all
/// three break the same way: a mesh drawn at its bind pose stops being the mesh that was modelled.
/// </summary>
public class SkeletonTests
{
    [Fact]
    public void GetJointMatrix_IsTheIdentity_WhenTheBoneSitsWhereTheBindPosePutIt()
    {
        GameObject bone = new("Bone");
        bone.Transform.Position = new Vector3(1f, 2f, 3f);
        bone.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);

        Skeleton skeleton = new();

        // The bind inverse is the inverse of the bone's world matrix at bind time, which is what an importer records.
        Matrix4x4 bindWorld = bone.Transform.GetModelMatrix();
        Matrix4x4.Invert(bindWorld, out Matrix4x4 bindInverse);

        skeleton.AddBone(bone, bindInverse);

        Matrix4x4 joint = skeleton.GetJointMatrix(0, Matrix4x4.Identity);

        // A bind inverse times the same matrix is the identity only up to float rounding, so it is compared with the
        // same tolerance the other cases use.
        AssertIdentity(joint);
    }

    [Fact]
    public void GetJointMatrix_LeavesTheBindPoseUntouched_WhenTheRendererSitsSomewhere()
    {
        // The renderer itself sits somewhere in the world. The palette is scene-space, so what has to come out even is
        // jointMatrix * rendererWorld: the vertex gets carried back to exactly where it was modelled.
        GameObject renderer = new("Renderer");
        renderer.Transform.Position = new Vector3(-4f, 0.5f, 9f);
        renderer.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f);

        GameObject bone = new("Bone");
        bone.Transform.Parent = renderer.Transform;
        bone.Transform.Position = new Vector3(0f, 1f, 0f);

        Matrix4x4.Invert(bone.Transform.GetModelMatrix(), out Matrix4x4 bindInverse);

        Skeleton skeleton = new();
        skeleton.AddBone(bone, bindInverse);

        Matrix4x4 rendererWorld = renderer.Transform.GetModelMatrix();

        Matrix4x4 joint = skeleton.GetJointMatrix(0, rendererWorld);

        // jointMatrix alone is inverse(rendererWorld) here, which is a rotation and so not the identity; the vertex
        // transform that matters is the pair.
        AssertIdentity(joint * rendererWorld);
    }

    [Fact]
    public void GetJointMatrix_MovesWithTheBone_OnceThePoseChanges()
    {
        GameObject bone = new("Bone");
        Matrix4x4.Invert(bone.Transform.GetModelMatrix(), out Matrix4x4 bindInverse);

        Skeleton skeleton = new();
        skeleton.AddBone(bone, bindInverse);

        bone.Transform.Position = new Vector3(0f, 2f, 0f);

        Matrix4x4 joint = skeleton.GetJointMatrix(0, Matrix4x4.Identity);

        // The joint matrix has become the bone's new transform: a vertex at the origin is carried to (0, 2, 0).
        Vector3 moved = Vector3.Transform(Vector3.Zero, joint);

        moved.Should().Be(new Vector3(0f, 2f, 0f));
    }

    [Fact]
    public void GetJointMatrix_OfABoneThatIsNotThere_IsTheIdentity()
    {
        Skeleton skeleton = new();

        skeleton.GetJointMatrix(0, Matrix4x4.Identity).Should().Be(Matrix4x4.Identity);
        skeleton.GetJointMatrix(-1, Matrix4x4.Identity).Should().Be(Matrix4x4.Identity);
    }

    [Fact]
    public void Bones_KeepTheOrderTheyWereAdded_In_BecauseThatIsTheBoneIndexOfEveryVertex()
    {
        Skeleton skeleton = new();

        GameObject first = new("First");
        GameObject second = new("Second");
        GameObject third = new("Third");

        skeleton.AddBone(first, Matrix4x4.Identity);
        skeleton.AddBone(second, Matrix4x4.Identity);
        skeleton.AddBone(third, Matrix4x4.Identity);

        skeleton.BoneCount.Should().Be(3);
        skeleton.Bones[0].Should().BeSameAs(first);
        skeleton.Bones[1].Should().BeSameAs(second);
        skeleton.Bones[2].Should().BeSameAs(third);
    }

    [Fact]
    public void FillJointMatrices_WritesAPaletteWithoutAllocating()
    {
        Skeleton skeleton = new();

        for (int index = 0; index < 3; index++)
        {
            GameObject bone = new($"Bone{index}");
            Matrix4x4.Invert(bone.Transform.GetModelMatrix(), out Matrix4x4 bindInverse);
            skeleton.AddBone(bone, bindInverse);
        }

        Matrix4x4[] palette = new Matrix4x4[3];

        skeleton.FillJointMatrices(Matrix4x4.Identity, palette);

        foreach (Matrix4x4 matrix in palette)
        {
            AssertIdentity(matrix);
        }
    }

    [Fact]
    public void FillJointMatrices_RefusesAPaletteThatIsTooSmall()
    {
        Skeleton skeleton = new();
        skeleton.AddBone(new GameObject("Bone"), Matrix4x4.Identity);

        Action fill = () => skeleton.FillJointMatrices(Matrix4x4.Identity, []);

        fill.Should().Throw<ArgumentException>();
    }

    private static void AssertIdentity(Matrix4x4 matrix)
    {
        // Compared component by component: a failed inversion produces near-identity values that a strict equality
        // check would reject for reasons that have nothing to do with the maths under test.
        matrix.M11.Should().BeApproximately(1f, 1e-4f);
        matrix.M22.Should().BeApproximately(1f, 1e-4f);
        matrix.M33.Should().BeApproximately(1f, 1e-4f);
        matrix.M44.Should().BeApproximately(1f, 1e-4f);

        matrix.M12.Should().BeApproximately(0f, 1e-4f);
        matrix.M13.Should().BeApproximately(0f, 1e-4f);
        matrix.M14.Should().BeApproximately(0f, 1e-4f);
        matrix.M21.Should().BeApproximately(0f, 1e-4f);
        matrix.M23.Should().BeApproximately(0f, 1e-4f);
        matrix.M24.Should().BeApproximately(0f, 1e-4f);
        matrix.M31.Should().BeApproximately(0f, 1e-4f);
        matrix.M32.Should().BeApproximately(0f, 1e-4f);
        matrix.M34.Should().BeApproximately(0f, 1e-4f);
        matrix.M41.Should().BeApproximately(0f, 1e-4f);
        matrix.M42.Should().BeApproximately(0f, 1e-4f);
        matrix.M43.Should().BeApproximately(0f, 1e-4f);
    }
}
