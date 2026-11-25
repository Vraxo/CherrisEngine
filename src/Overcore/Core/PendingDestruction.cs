using Overcore.Components;
using System.Reflection;

namespace Overcore.Core;

/// <summary>
/// The objects and components that have been destroyed but not yet taken away, Unity's deferred <c>Destroy</c>:
/// nothing goes until the frame ends, so a script can destroy something it is still holding and finish the frame in
/// one piece.
/// </summary>
internal static class PendingDestruction
{
    private sealed class Entry
    {
        public object Target { get; }
        public float Remaining { get; set; }

        public Entry(object target, float delay)
        {
            Target = target;
            Remaining = MathF.Max(delay, 0.0f);
        }
    }

    private static readonly List<Entry> Entries = [];
    private static readonly MethodInfo CloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>How many things are waiting to go, for tests and diagnostics.</summary>
    public static int Count => Entries.Count;

    /// <summary>Queues an object to be destroyed at the end of the frame, or after a delay.</summary>
    /// <param name="target">The object to destroy.</param>
    /// <param name="delay">Seconds to wait before it goes; zero means the end of this frame.</param>
    public static void Enqueue(GameObject target, float delay)
    {
        ArgumentNullException.ThrowIfNull(target);
        Entries.Add(new Entry(target, delay));
    }

    /// <summary>Queues a component to be taken off its object at the end of the frame, or after a delay.</summary>
    /// <param name="target">The component to destroy.</param>
    /// <param name="delay">Seconds to wait before it goes; zero means the end of this frame.</param>
    public static void Enqueue(Component target, float delay)
    {
        ArgumentNullException.ThrowIfNull(target);
        Entries.Add(new Entry(target, delay));
    }

    /// <summary>Runs the queue: whatever has waited long enough goes now.</summary>
    /// <param name="deltaTime">Seconds since the previous frame, in scaled time.</param>
    public static void Flush(float deltaTime)
    {
        if (Entries.Count == 0)
        {
            return;
        }

        for (int index = Entries.Count - 1; index >= 0; index--)
        {
            Entry entry = Entries[index];
            entry.Remaining -= deltaTime;

            if (entry.Remaining > 0.0f)
            {
                continue;
            }

            Entries.RemoveAt(index);
            DestroyNow(entry.Target);
        }
    }

    /// <summary>Forgets everything queued, without destroying it. A closed scene does this: its objects are already gone.</summary>
    public static void Clear() => Entries.Clear();

    /// <summary>Takes something out of the scene at once, which is what a flush does when the wait is over.</summary>
    /// <param name="target">The object or component to remove.</param>
    public static void DestroyNow(object target)
    {
        switch (target)
        {
            case GameObject gameObject when !gameObject.IsDestroyed:
                gameObject.IsDestroyed = true;

                if (gameObject.scene is { } owner)
                {
                    owner.RemoveGameObject(gameObject);
                }
                else
                {
                    gameObject.Transform.Parent = null!;
                    gameObject.DestroyComponents();
                }

                break;

            case Component component:
                component.GameObject?.RemoveComponent(component);
                break;
        }
    }

    /// <summary>Clones a component, which is how an instantiated object gets its own copy of every one of them.</summary>
    /// <param name="source">The component to copy.</param>
    /// <returns>A new component carrying the original's settings but none of its runtime state.</returns>
    public static Component CloneComponent(Component source)
    {
        Component copy = (Component)CloneMethod.Invoke(source, null)!;
        copy.OnInstantiated();
        return copy;
    }
}
