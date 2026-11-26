using Overcore.Components;
using Overcore.Core;
using Overcore.Core.Logging;
using System.Reflection;

namespace Overcore.Animation;

/// <summary>
/// Fires a clip's events on the objects they belong to, exactly as <c>Invoke(nameof(...), delay)</c> already resolves a
/// method name on a <see cref="Script"/>: by reflection, against the scripts on the object a clip drives.
/// </summary>
/// <remarks>
/// <para>
/// Events are addressed by time, so what matters is which ones a frame <em>crossed</em> — not which ones happen to be
/// near the playhead. A frame that jumps over three events fires all three, in time order, once each. A clip that
/// loops fires the events it crosses each time round.
/// </para>
/// <para>
/// A method that cannot be found is reported once and then remembered, so a missing handler costs one line in the log
/// rather than one per frame for the life of the clip.
/// </para>
/// </remarks>
public sealed class AnimationEventDispatch
{
    /// <summary>Parameter shapes an event method may have, tried in this order — Unity's order.</summary>
    private static readonly Type[] ParameterShapes = [typeof(float), typeof(int), typeof(string), Type.EmptyTypes];

    /// <summary>Events already reported as missing, so a bad handler is logged once rather than every frame.</summary>
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>How many events have actually reached a method, across this dispatcher's life.</summary>
    public int FiredCount { get; private set; }

    /// <summary>
    /// Fires every event the playhead crossed between two times. Handles a clip that looped within the frame by
    /// walking forward rather than wrapping, so no event is skipped and none fires twice.
    /// </summary>
    /// <param name="clip">The clip whose events to fire.</param>
    /// <param name="root">The object the events are addressed to and their methods looked up on.</param>
    /// <param name="previousTime">Where the playhead was.</param>
    /// <param name="currentTime">Where it is now, after wrapping.</param>
    /// <param name="wrapped">
    /// True when the clock passed the clip's end and started again inside this frame; the interval then runs from
    /// <paramref name="previousTime"/> to the clip's length and again from zero to <paramref name="currentTime"/>.
    /// </param>
    public int Fire(AnimationClip clip, GameObject root, float previousTime, float currentTime, bool wrapped)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(root);

        if (clip.Events.Count == 0)
        {
            return 0;
        }

        int before = FiredCount;

        if (wrapped)
        {
            FireRange(clip, root, previousTime, clip.Length);
            FireRange(clip, root, 0f, currentTime);
        }
        else
        {
            FireRange(clip, root, previousTime, currentTime);
        }

        return FiredCount - before;
    }

    /// <summary>
    /// Fires the events in a half-open interval <c>(from, to]</c>. Half-open is what makes a loop work: time zero's
    /// event belongs to the frame that arrives at it, not to the one that leaves it, so it fires once per cycle.
    /// </summary>
    private void FireRange(AnimationClip clip, GameObject root, float from, float to)
    {
        if (to <= from)
        {
            return;
        }

        // Events are kept in time order by the clip, so the first one past the start of the frame is where to begin.
        foreach (AnimationEvent animationEvent in clip.Events)
        {
            if (animationEvent.Time <= from || animationEvent.Time > to)
            {
                continue;
            }

            FireOne(animationEvent, root);
        }
    }

    /// <summary>Calls one event on every script of the object that offers a matching method.</summary>
    private void FireOne(AnimationEvent animationEvent, GameObject root)
    {
        if (string.IsNullOrWhiteSpace(animationEvent.Function))
        {
            return;
        }

        bool called = false;

        foreach (Script script in root.GetComponents<Script>())
        {
            MethodInfo? method = FindMethod(script.GetType(), animationEvent.Function);

            if (method is null)
            {
                continue;
            }

            if (Call(script, method, animationEvent))
            {
                called = true;
            }
        }

        if (called)
        {
            return;
        }

        // Reported against the object, not the script: the point of the message is that the object has no handler.
        string key = $"{root.Id}:{animationEvent.Function}";

        if (_reported.Add(key))
        {
            Logger.Warning(
                $"[AnimationEvent] '{root.Name}' has no script method called '{animationEvent.Function}'.");
        }
    }

    /// <summary>
    /// Finds a method by name, preferring one that takes the event's parameter type. Private methods are included, the
    /// same way <c>Invoke</c> resolves them.
    /// </summary>
    private static MethodInfo? FindMethod(Type type, string name)
    {
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (Type parameter in ParameterShapes)
        {
            foreach (MethodInfo method in methods)
            {
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();

                if (parameters.Length != (parameter == Type.EmptyTypes ? 0 : 1))
                {
                    continue;
                }

                if (parameters.Length == 1 && parameters[0].ParameterType != parameter)
                {
                    continue;
                }

                return method;
            }
        }

        return null;
    }

    /// <summary>Invokes one method with the event's payload, logging rather than throwing when the script throws.</summary>
    private bool Call(Script script, MethodInfo method, AnimationEvent animationEvent)
    {
        object?[]? arguments = method.GetParameters().Length switch
        {
            0 => null,
            1 when method.GetParameters()[0].ParameterType == typeof(float) => [animationEvent.FloatParameter],
            1 when method.GetParameters()[0].ParameterType == typeof(int) => [animationEvent.IntParameter],
            1 => [animationEvent.StringParameter],
            _ => null
        };

        try
        {
            method.Invoke(script, arguments);
            FiredCount++;

            return true;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            Logger.Error(
                $"[{script.GameObject?.Name ?? "Unassigned"}:{script.GetType().Name}] " +
                $"{animationEvent.Function} threw: {exception.InnerException.Message}");
        }
        catch (Exception exception)
        {
            Logger.Error(
                $"[{script.GameObject?.Name ?? "Unassigned"}:{script.GetType().Name}] " +
                $"{animationEvent.Function} could not be called: {exception.Message}");
        }

        return false;
    }

    /// <summary>Forgets which handlers were missing, so a re-import reports them again.</summary>
    public void ResetReported() => _reported.Clear();
}
