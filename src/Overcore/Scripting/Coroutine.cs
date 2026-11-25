using System.Collections;

namespace Overcore.Components;

/// <summary>Which part of the frame a coroutine is being offered.</summary>
internal enum CoroutinePhase
{
    /// <summary>After every script's <c>Update</c>: the usual place a coroutine carries on.</summary>
    Update,

    /// <summary>Inside a fixed physics step, before the step is simulated.</summary>
    Fixed,

    /// <summary>The very end of the frame, after everything else.</summary>
    EndOfFrame
}

/// <summary>What a coroutine is waiting for.</summary>
internal enum CoroutineWait
{
    /// <summary>Nothing: the coroutine is not running any more.</summary>
    None,

    /// <summary>The next frame.</summary>
    NextFrame,

    /// <summary>A length of scaled time.</summary>
    Seconds,

    /// <summary>The next fixed step.</summary>
    FixedUpdate,

    /// <summary>The end of this frame.</summary>
    EndOfFrame,

    /// <summary>A condition that has to become true.</summary>
    Until,

    /// <summary>A condition that has to become false.</summary>
    While,

    /// <summary>Another coroutine that has to finish.</summary>
    Nested
}

/// <summary>
/// A running routine started with <see cref="Script.StartCoroutine(IEnumerator)"/>, Unity's <c>Coroutine</c>:
/// hand it back to <see cref="Script.StopCoroutine(Coroutine)"/> to stop it, and read <see cref="IsFinished"/> to
/// see whether it has run out on its own.
/// </summary>
public sealed class Coroutine
{
    // Nested coroutines (a routine that yields another routine) are a stack: the innermost one runs.
    private readonly Stack<IEnumerator> _routine = new();

    // The routine the coroutine was started from, so it can be stopped by handing the same object back.
    private readonly IEnumerator _source;

    internal Script Owner { get; }
    internal CoroutineWait Wait { get; private set; } = CoroutineWait.NextFrame;
    internal float ResumeAt { get; private set; }
    internal Func<bool>? Condition { get; private set; }
    internal Coroutine? Nested { get; private set; }

    /// <summary>True once the routine has run out its last statement, or been stopped.</summary>
    public bool IsFinished { get; private set; }

    internal Coroutine(IEnumerator routine, Script owner)
    {
        ArgumentNullException.ThrowIfNull(routine);
        Owner = owner;
        _source = routine;
        _routine.Push(routine);
    }

    /// <summary>True when this is the frame (or the step) the coroutine should carry on in.</summary>
    /// <param name="phase">Which part of the frame is running.</param>
    /// <param name="time">The scene's scaled clock, in seconds.</param>
    internal bool ShouldStep(CoroutinePhase phase, float time)
    {
        if (IsFinished)
        {
            return false;
        }

        return Wait switch
        {
            CoroutineWait.NextFrame => phase == CoroutinePhase.Update,
            CoroutineWait.Seconds => phase == CoroutinePhase.Update && time >= ResumeAt,
            CoroutineWait.EndOfFrame => phase == CoroutinePhase.EndOfFrame,
            CoroutineWait.FixedUpdate => phase == CoroutinePhase.Fixed,
            CoroutineWait.Until => phase == CoroutinePhase.Update && Condition?.Invoke() == true,
            CoroutineWait.While => phase == CoroutinePhase.Update && Condition?.Invoke() == false,
            CoroutineWait.Nested => phase == CoroutinePhase.Update && Nested?.IsFinished != false,
            _ => false
        };
    }

    /// <summary>Runs the routine up to its next yield and records what it is waiting for.</summary>
    /// <param name="time">The scene's scaled clock, in seconds, that time-based waits count from.</param>
    internal void Advance(float time)
    {
        if (IsFinished)
        {
            return;
        }

        // A finished nested routine is dropped so the one that yielded it carries on in the same call.
        Wait = CoroutineWait.NextFrame;
        Condition = null;
        Nested = null;

        while (_routine.Count > 0)
        {
            IEnumerator current = _routine.Peek();

            if (current.MoveNext())
            {
                if (!ReadYield(current.Current, time))
                {
                    return;
                }

                // ReadYield asked for another routine to be entered; loop and step into it.
                continue;
            }

            _routine.Pop();

            if (_routine.Count > 0)
            {
                continue;
            }

            IsFinished = true;
            Wait = CoroutineWait.None;
            return;
        }
    }

    /// <summary>True when this coroutine was started from that exact routine object.</summary>
    /// <param name="routine">The routine to compare against.</param>
    internal bool Matches(IEnumerator routine) => ReferenceEquals(_source, routine);

    /// <summary>Stops the coroutine where it is. The routine never runs again.</summary>
    public void Stop()
    {
        _routine.Clear();
        IsFinished = true;
        Wait = CoroutineWait.None;
        Condition = null;
        Nested = null;
    }

    /// <summary>
    /// Reads what a routine yielded. Returns true when the value was another routine that has been pushed and
    /// should be stepped into right away, false when the coroutine is to wait.
    /// </summary>
    private bool ReadYield(object? yielded, float time)
    {
        switch (yielded)
        {
            case null:
                Wait = CoroutineWait.NextFrame;
                return false;

            case WaitForSeconds seconds:
                Wait = CoroutineWait.Seconds;
                ResumeAt = time + MathF.Max(seconds.Seconds, 0.0f);
                return false;

            case WaitForEndOfFrame:
                Wait = CoroutineWait.EndOfFrame;
                return false;

            case WaitForFixedUpdate:
                Wait = CoroutineWait.FixedUpdate;
                return false;

            case WaitUntil until:
                Wait = CoroutineWait.Until;
                Condition = until.Predicate;
                return false;

            case WaitWhile waitWhile:
                Wait = CoroutineWait.While;
                Condition = waitWhile.Predicate;
                return false;

            case Coroutine other:
                Wait = CoroutineWait.Nested;
                Nested = other;
                return false;

            case IEnumerator nested:
                _routine.Push(nested);
                return true;

            default:
                // A yield of something the engine does not know still costs a frame, which is what Unity does.
                Wait = CoroutineWait.NextFrame;
                return false;
        }
    }
}

/// <summary>The base of the values a coroutine can yield, Unity's <c>YieldInstruction</c>.</summary>
public abstract class YieldInstruction
{
}

/// <summary>Waits a number of scaled seconds — Unity's <c>WaitForSeconds</c>.</summary>
public sealed class WaitForSeconds : YieldInstruction
{
    /// <summary>How long the wait lasts, in scaled seconds.</summary>
    public float Seconds { get; }

    /// <summary>Creates a wait of the given length.</summary>
    /// <param name="seconds">How long to wait, in scaled seconds.</param>
    public WaitForSeconds(float seconds) => Seconds = seconds;
}

/// <summary>Waits until the end of the frame — Unity's <c>WaitForEndOfFrame</c>.</summary>
public sealed class WaitForEndOfFrame : YieldInstruction
{
}

/// <summary>Waits for the next fixed physics step — Unity's <c>WaitForFixedUpdate</c>.</summary>
public sealed class WaitForFixedUpdate : YieldInstruction
{
}

/// <summary>Waits until a condition is true — Unity's <c>WaitUntil</c>.</summary>
public sealed class WaitUntil : YieldInstruction
{
    internal Func<bool> Predicate { get; }

    /// <summary>Creates the wait.</summary>
    /// <param name="predicate">The condition to wait for.</param>
    public WaitUntil(Func<bool> predicate) => Predicate = predicate;

    /// <summary>True once the condition holds.</summary>
    public bool IsReady => Predicate();
}

/// <summary>Waits until a condition is false — Unity's <c>WaitWhile</c>.</summary>
public sealed class WaitWhile : YieldInstruction
{
    internal Func<bool> Predicate { get; }

    /// <summary>Creates the wait.</summary>
    /// <param name="predicate">The condition to wait out.</param>
    public WaitWhile(Func<bool> predicate) => Predicate = predicate;
}
