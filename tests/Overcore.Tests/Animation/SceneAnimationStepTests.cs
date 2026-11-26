using AwesomeAssertions;
using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// The frame step: <see cref="Scene.Update"/> advances animation between <c>Update</c> and <c>LateUpdate</c>, which is
/// Unity's placement and the only order that lets a script move a bone and then have a clip override it.
/// </summary>
public class SceneAnimationStepTests
{
    [Fact]
    public void Update_AdvancesAnAnimationsClock()
    {
        GameObject root = new("Root");
        GameObject hips = new("Hips");
        hips.Transform.Parent = root.Transform;

        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        Scene scene = Start(root, physics, audio);

        AnimationClip clip = HeightOf("Hips", (0f, 0f), (2f, 2f));
        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = clip });

        scene.Update(0.5f);

        animation.Time.Should().BeApproximately(0.5f, 1e-5f);
        hips.Transform.Position.Y.Should().BeApproximately(0.5f, 1e-5f);
    }

    [Fact]
    public void Update_RunsTheClipAfterUpdate_SoAClipOverridesAScriptThatMovedTheBone()
    {
        // The placement that matters: a script writing a bone in Update is overridden by the clip that drives it.
        GameObject root = new("Root");
        GameObject hips = new("Hips");
        hips.Transform.Parent = root.Transform;

        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        Scene scene = Start(root, physics, audio);

        AnimationClip clip = HeightOf("Hips", (0f, 7f), (1f, 7f));
        root.AddComponent(new AnimationPlayer { Clip = clip, WrapModeOverride = AnimationWrapMode.ClampForever });
        root.AddComponent(new ScriptThatMovesBones(hips));

        scene.Update(0.1f);

        hips.Transform.Position.Y.Should().Be(7f);
    }

    [Fact]
    public void Update_SkipsAnInactiveObject()
    {
        GameObject root = new("Root");

        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        Scene scene = Start(root, physics, audio);

        AnimationPlayer animation = root.AddComponent(new AnimationPlayer { Clip = HeightOf("Bone", (0f, 5f), (1f, 5f)) });

        // Switched off after it was running, which is the case a pause or a deactivated enemy hits.
        root.SetActive(false);

        // An object that is switched off is not advanced, and its clock does not move.
        scene.Invoking(s => s.Update(0.5f)).Should().NotThrow();
        animation.Time.Should().Be(0f);
    }

    [Fact]
    public void Update_LeavesThePoseForLateUpdateToRead()
    {
        // The other half of the placement: a script in LateUpdate sees the frame's pose, so a camera that follows a
        // bone does not lag a frame behind it.
        GameObject root = new("Root");
        GameObject hips = new("Hips");
        hips.Transform.Parent = root.Transform;

        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        Scene scene = Start(root, physics, audio);

        root.AddComponent(new AnimationPlayer { Clip = HeightOf("Hips", (0f, 4f), (1f, 4f)) });

        Reader reader = root.AddComponent(new Reader(hips));

        scene.Update(0.1f);

        reader.Seen.Should().Be(4f);
    }

    /// <summary>
    /// Opens a scene around one object and starts it, which is what wakes the components added to it. The physics and
    /// audio systems belong to the caller and must outlive the updates that follow: the scene keeps the physics system
    /// for its fixed steps, so disposing it here would fail the frame rather than the test.
    /// </summary>
    private static Scene Start(GameObject root, PhysicsSystem physics, AudioSystem audio)
    {
        Scene scene = new("Test.scene", [root]);
        scene.Start(physics, audio);

        return scene;
    }

    private static AnimationClip HeightOf(string bonePath, params (float Time, float Value)[] keys)
    {
        AnimationCurve curve = new(bonePath, AnimationProperty.LocalPosition, AnimationAxis.Y);

        foreach ((float time, float value) in keys)
        {
            curve.AddKey(time, value);
        }

        AnimationClip clip = new("Height") { WrapMode = AnimationWrapMode.ClampForever };
        clip.AddCurve(curve);

        return clip;
    }

    /// <summary>Reads a bone's height in <c>LateUpdate</c>, to prove the animation step ran before it.</summary>
    private sealed class Reader(GameObject target) : Script
    {
        public float Seen { get; private set; }

        public override void LateUpdate() => Seen = target.Transform.Position.Y;
    }

    /// <summary>Moves a bone every frame in <c>Update</c>, to prove the animation step runs after it.</summary>
    private sealed class ScriptThatMovesBones(GameObject target) : Script
    {
        public override void Update(float deltaTime) => target.Transform.Position = new(0f, -1f, 0f);
    }
}
