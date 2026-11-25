using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Physics;
using System.Collections;
using Xunit;

namespace Overcore.Tests.Lifecycle;

/// <summary>
/// Coroutines and timed calls: a routine that spans frames, waits on time or a condition, nests, and can be
/// stopped — and <c>Invoke</c>, which calls a method once the wait is over.
/// </summary>
public class CoroutineTests
{
    private const float Frame = 0.05f;

    /// <summary>A script that starts a routine when it starts, and records what the routine does.</summary>
    private sealed class RoutineRunner : Script
    {
        public List<string> Log { get; } = [];

        public Func<IEnumerator>? Routine { get; set; }

        public Coroutine? Running { get; private set; }

        public int Invoked { get; private set; }

        public override void Start()
        {
            if (Routine is not null)
            {
                Running = StartCoroutine(Routine());
            }
        }

        public void Boom() => Invoked++;
    }

    private static Scene StartScene(PhysicsSystem physics, AudioSystem audio, params GameObject[] gameObjects)
    {
        Scene scene = new(string.Empty, [.. gameObjects]);
        scene.Start(physics, audio);

        return scene;
    }

    [Fact]
    public void ARoutineRunsToItsFirstYieldAndThenAFrameAtATime()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        RoutineRunner runner = new();

        runner.Routine = () => Steps(runner.Log);
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);

        runner.Log.Should().Equal("before", "the first yield stops here");

        scene.Update(Frame);
        runner.Log.Should().HaveCount(3).And.EndWith("after one frame");

        scene.Update(Frame);
        runner.Log.Should().HaveCount(4).And.EndWith("done");
    }

    private static IEnumerator Steps(List<string> log)
    {
        log.Add("before");
        yield return null;
        log.Add("after one frame");
        yield return null;
        log.Add("done");
    }

    [Fact]
    public void WaitForSecondsCountsTheScenesOwnClock()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        RoutineRunner runner = new();
        runner.Routine = () => Waits(runner.Log);
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);
        runner.Log.Should().BeEmpty();

        // The first frame starts the wait from the scene's clock; a fifth of a second is four frames of 0.05.
        scene.Update(Frame);
        runner.Log.Should().BeEmpty("a fifth of a second has not passed yet");

        scene.Update(Frame);
        scene.Update(Frame);
        runner.Log.Should().BeEmpty();

        scene.Update(Frame);
        scene.Update(Frame);

        runner.Log.Should().Equal("waited");
    }

    private static IEnumerator Waits(List<string> log)
    {
        yield return new WaitForSeconds(0.2f);
        log.Add("waited");
    }

    [Fact]
    public void ARoutineThatRunsOutReportsItselfFinished()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        RoutineRunner runner = new();
        runner.Routine = () => OneStep();
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);
        runner.Running.Should().NotBeNull();

        Coroutine running = runner.Running!;
        running.IsFinished.Should().BeFalse("it has one more statement to run");

        scene.Update(Frame);
        scene.Update(Frame);

        running.IsFinished.Should().BeTrue();
    }

    private static IEnumerator OneStep()
    {
        yield return null;
        yield break;
    }

    [Fact]
    public void WaitUntilWaitsForItsCondition()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        bool ready = false;
        RoutineRunner runner = new();
        runner.Routine = () => Until(runner.Log, () => ready);
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);

        scene.Update(Frame);
        scene.Update(Frame);
        runner.Log.Should().BeEmpty();

        ready = true;
        scene.Update(Frame);

        runner.Log.Should().Equal("ready");
    }

    private static IEnumerator Until(List<string> log, Func<bool> ready)
    {
        yield return new WaitUntil(ready);
        log.Add("ready");
    }

    [Fact]
    public void ANestedRoutineRunsInsideTheOuterOne()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        RoutineRunner runner = new();
        runner.Routine = () => Outer(runner.Log);
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);

        runner.Log.Should().Equal("outer start", "inner start");

        scene.Update(Frame);
        runner.Log.Should().Equal("outer start", "inner start", "inner end", "inner end seen");

        scene.Update(Frame);
        runner.Log.Should().Equal("outer start", "inner start", "inner end", "inner end seen", "outer end");
    }

    private static IEnumerator Outer(List<string> log)
    {
        log.Add("outer start");
        yield return Inner(log);

        // The outer routine carries on in the same frame the inner one runs out.
        log.Add("inner end seen");
        yield return null;
        log.Add("outer end");
    }

    private static IEnumerator Inner(List<string> log)
    {
        log.Add("inner start");
        yield return null;
        log.Add("inner end");
    }

    [Fact]
    public void WaitForFixedUpdateWaitsForAPhysicsStep()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        RoutineRunner runner = new();
        runner.Routine = () => FixedWait(runner.Log);
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);

        scene.Update(Frame);
        runner.Log.Should().BeEmpty("no physics step has run");

        physics.Update(1.0f / 60.0f);

        runner.Log.Should().Equal("stepped");
    }

    private static IEnumerator FixedWait(List<string> log)
    {
        yield return new WaitForFixedUpdate();
        log.Add("stepped");
    }

    [Fact]
    public void StopCoroutineEndsARoutineWhereItIs()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        RoutineRunner runner = new();
        runner.Routine = () => Forever(runner.Log);
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);

        scene.Update(Frame);
        runner.Log.Should().Equal("tick");

        runner.Running.Should().NotBeNull();

        Coroutine running = runner.Running!;
        runner.StopCoroutine(running);

        running.IsFinished.Should().BeTrue();

        scene.Update(Frame);
        scene.Update(Frame);
        runner.Log.Should().ContainSingle("a stopped routine never runs again").Which.Should().Be("tick");
    }

    private static IEnumerator Forever(List<string> log)
    {
        while (true)
        {
            yield return null;
            log.Add("tick");
            yield return null;
            log.Add("a stopped routine never runs again");
        }
    }

    [Fact]
    public void InvokeCallsAMethodOnceTheWaitIsOver()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        RoutineRunner runner = new();
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);

        runner.Invoke(nameof(RoutineRunner.Boom), 0.1f);
        runner.IsInvoking(nameof(RoutineRunner.Boom)).Should().BeTrue();

        scene.Update(Frame);
        runner.Invoked.Should().Be(0);

        scene.Update(Frame);
        scene.Update(Frame);

        runner.Invoked.Should().Be(1, "a tenth of a second is two frames of 0.05");
        runner.IsInvoking(nameof(RoutineRunner.Boom)).Should().BeFalse();
    }

    [Fact]
    public void InvokeRepeatingKeepsCalling()
    {
        using PhysicsSystem physics = new();
        using AudioSystem audio = new();

        RoutineRunner runner = new();
        GameObject thing = new("Thing");
        thing.AddComponent(runner);

        Scene scene = StartScene(physics, audio, thing);

        runner.InvokeRepeating(nameof(RoutineRunner.Boom), 0.0f, 0.05f);

        scene.Update(Frame);
        scene.Update(Frame);

        runner.Invoked.Should().BeGreaterThan(1);

        runner.CancelInvoke(nameof(RoutineRunner.Boom));

        int calls = runner.Invoked;
        scene.Update(Frame);

        runner.Invoked.Should().Be(calls);
    }
}
