using Overcore.Components;
using Overcore.Core.Logging;
using System.Numerics;

namespace Overcore.Animation;

/// <summary>
/// A set of bone transforms sampled out of one or more clips and blended together: the thing a clip is evaluated
/// into, and the thing a skeleton is then driven from.
/// </summary>
/// <remarks>
/// <para>
/// A pose is keyed by the hierarchy path a clip's curves are addressed by (<c>"Hips/Spine/Chest"</c>), not by a
/// <see cref="Transform"/> reference, so sampling needs no scene and no object graphs — which is what lets the whole
/// thing be tested as arithmetic. <see cref="ApplyTo"/> is the step that turns those paths into transforms.
/// </para>
/// <para>
/// A bone the pose has never been given a value for is "not in the pose", and sampling a clip that does not address
/// it leaves it that way. That distinction is the whole point of a mask: a clip that moves a hand must not reset the
/// rest of the body to whatever the pose happened to be holding.
/// </para>
/// <para>
/// Blending is linear and identity-preserving: position and scale interpolate, rotation slerps, and a weight of one
/// overwrites while a weight of zero changes nothing. Two clips at half weight each land halfway between them.
/// </para>
/// </remarks>
public class AnimationPose
{
    /// <summary>One bone's local transform, as the pose holds it.</summary>
    public struct BonePose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public static BonePose Identity => new()
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        };

        /// <summary>The bone's local matrix, in the order <see cref="Transform.GetModelMatrix"/> uses.</summary>
        public readonly Matrix4x4 GetLocalMatrix() =>
            Matrix4x4.CreateScale(Scale) *
            Matrix4x4.CreateFromQuaternion(Rotation) *
            Matrix4x4.CreateTranslation(Position);
    }

    private readonly Dictionary<string, BonePose> _bones = new(StringComparer.Ordinal);

    /// <summary>The paths the pose actually holds a value for, in no particular order.</summary>
    public IEnumerable<string> BonePaths => _bones.Keys;

    public int BoneCount => _bones.Count;

    /// <summary>True when the pose has been given a value for <paramref name="bonePath"/>.</summary>
    public bool Contains(string bonePath) => _bones.ContainsKey(bonePath);

    /// <summary>
    /// The pose a bone holds, or the identity when it holds none — which is a transform at the object's origin with
    /// no rotation and unit scale, the same values a fresh <see cref="Transform"/> starts with.
    /// </summary>
    public BonePose Get(string bonePath) =>
        _bones.TryGetValue(bonePath, out BonePose pose) ? pose : BonePose.Identity;

    /// <summary>Where a bone sits in the pose, whether or not it has been given a value.</summary>
    public Vector3 GetPosition(string bonePath) => Get(bonePath).Position;

    public Quaternion GetRotation(string bonePath) => Get(bonePath).Rotation;

    public Vector3 GetScale(string bonePath) => Get(bonePath).Scale;

    /// <summary>
    /// Sets a bone outright, replacing whatever the pose held. This is the "overwrite" a weight of one means, and the
    /// first write into an otherwise untouched pose.
    /// </summary>
    public void Set(string bonePath, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        _bones[bonePath ?? string.Empty] = new BonePose
        {
            Position = position,
            Rotation = rotation,
            Scale = scale
        };
    }

    /// <summary>Sets a bone's whole transform from a live <see cref="Transform"/> — the "capture the bind pose" case.</summary>
    public void Set(string bonePath, Transform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        Set(bonePath, transform.Position, transform.Rotation, transform.Scale);
    }

    /// <summary>
    /// Blends a sampled value into a bone. <paramref name="weight"/> of one overwrites, zero is a no-op, and anything
    /// between lands proportionally between what the pose held and what was sampled.
    /// </summary>
    /// <remarks>
    /// A bone the pose has not seen yet is blended against the identity rather than against "nothing", so the first
    /// weighted contribution into an empty pose is still a sane transform instead of an undefined one.
    /// </remarks>
    public void Blend(string bonePath, Vector3 position, Quaternion rotation, Vector3 scale, float weight)
    {
        if (!(weight > 0f) || !float.IsFinite(weight))
        {
            return;
        }

        string path = bonePath ?? string.Empty;

        if (weight >= 1f)
        {
            Set(path, position, rotation, scale);
            return;
        }

        BonePose current = Get(path);

        _bones[path] = new BonePose
        {
            Position = Vector3.Lerp(current.Position, position, weight),
            Rotation = Quaternion.Slerp(current.Rotation, rotation, weight),
            Scale = Vector3.Lerp(current.Scale, scale, weight)
        };
    }

    /// <summary>Blends one bone's transform from a live <see cref="Transform"/>, for the same reasons as the setter.</summary>
    public void Blend(string bonePath, Transform transform, float weight)
    {
        ArgumentNullException.ThrowIfNull(transform);

        Blend(bonePath, transform.Position, transform.Rotation, transform.Scale, weight);
    }

    /// <summary>
    /// Blends another pose into this one, bone by bone. A bone the other pose does not address is left alone, which is
    /// what makes a partial clip usable as a layer.
    /// </summary>
    public void Blend(AnimationPose other, float weight)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!(weight > 0f) || !float.IsFinite(weight))
        {
            return;
        }

        foreach ((string path, BonePose bone) in other._bones)
        {
            Blend(path, bone.Position, bone.Rotation, bone.Scale, weight);
        }
    }

    /// <summary>Copies another pose into this one outright, replacing every bone the other pose holds.</summary>
    public void CopyFrom(AnimationPose other)
    {
        ArgumentNullException.ThrowIfNull(other);

        _bones.Clear();

        foreach ((string path, BonePose bone) in other._bones)
        {
            _bones[path] = bone;
        }
    }

    /// <summary>Forgets every bone, leaving the pose empty rather than filled with identities.</summary>
    public void Clear() => _bones.Clear();

    /// <summary>
    /// Writes the pose onto a hierarchy: every path it holds is resolved from <paramref name="root"/> and the matching
    /// transform is set. Paths that do not resolve are reported and skipped, because a clip authored for one rig
    /// should not throw when it is played on another.
    /// </summary>
    /// <param name="root">The object the paths are relative to — the clip's root, not usually the scene root.</param>
    /// <param name="warnOnMissing">
    /// True to log a path that does not resolve. Sampling a clip outside its own rig is a real mistake worth seeing;
    /// sampling one of many layered clips that legitimately address a different subtree is not.
    /// </param>
    /// <returns>How many transforms were written.</returns>
    public int ApplyTo(Transform root, bool warnOnMissing = true)
    {
        ArgumentNullException.ThrowIfNull(root);

        int applied = 0;

        foreach ((string path, BonePose bone) in _bones)
        {
            Transform? target = Resolve(root, path);

            if (target is null)
            {
                if (warnOnMissing)
                {
                    Logger.Warning($"[AnimationPose] No transform at '{path}' under '{root.GameObject.Name}'.");
                }

                continue;
            }

            target.Position = bone.Position;
            target.Rotation = bone.Rotation;
            target.Scale = bone.Scale;

            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Fills the pose with the transforms a hierarchy currently holds, so a clip can be sampled on top of the pose
    /// the bones are already in. Walking from <paramref name="root"/> means the paths this produces are exactly the
    /// ones a clip addresses.
    /// </summary>
    /// <param name="root">The object the paths should be relative to.</param>
    /// <returns>The same pose, for chaining.</returns>
    public AnimationPose CaptureFrom(Transform root)
    {
        ArgumentNullException.ThrowIfNull(root);

        Clear();
        CaptureInto(root, string.Empty);

        return this;
    }

    private void CaptureInto(Transform transform, string path)
    {
        // The root object is the clip's root, not a bone of it: its own place in the world is what the paths hang
        // off, so it is never written back, and its children are the first level of the pose.
        foreach (Transform child in transform.Children)
        {
            string childPath = string.IsNullOrEmpty(path) ? child.GameObject.Name : path + "/" + child.GameObject.Name;

            Set(childPath, child);
            CaptureInto(child, childPath);
        }
    }

    /// <summary>
    /// Resolves a hierarchy path against a root. The empty path is the root itself, which is how a clip addresses the
    /// object it is played on rather than one of its bones.
    /// </summary>
    /// <remarks>
    /// A segment is matched against each child's name in order, so the first child with that name wins. A hierarchy
    /// with two children of the same name is ambiguous by nature; a rig from a file is a tree of unique names.
    /// </remarks>
    public static Transform? Resolve(Transform root, string bonePath)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (string.IsNullOrEmpty(bonePath))
        {
            return root;
        }

        Transform current = root;
        ReadOnlySpan<char> remaining = bonePath;

        while (!remaining.IsEmpty)
        {
            int separator = remaining.IndexOf('/');

            ReadOnlySpan<char> name = separator < 0 ? remaining : remaining[..separator];
            remaining = separator < 0 ? default : remaining[(separator + 1)..];

            if (name.IsEmpty)
            {
                continue;
            }

            Transform? next = null;

            foreach (Transform child in current.Children)
            {
                if (name.SequenceEqual(child.GameObject.Name))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }
}
