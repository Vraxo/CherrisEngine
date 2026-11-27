using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace OvercoreEditorSession;

/// <summary>What a web editor session's transport is doing.</summary>
public enum EditorPlayState
{
    /// <summary>Authoring: nothing advances, and the scene sits where it was left.</summary>
    Editing,

    /// <summary>The scene is running: physics steps and every enabled script ticks.</summary>
    Playing,

    /// <summary>The scene is frozen mid-run; play carries on from there, stop puts it back.</summary>
    Paused
}

/// <summary>What a transport button asks the running session to do.</summary>
public enum PlaybackRequest
{
    /// <summary>Start running, or carry on after a pause.</summary>
    Play,

    /// <summary>Freeze the scene where it is.</summary>
    Pause,

    /// <summary>Put the scene back to how it was authored and go back to editing.</summary>
    Stop,

    /// <summary>Put the scene back, then play it again.</summary>
    Restart
}

/// <summary>
/// Play mode for a web editor session: what "running" means when the UI is on the other side of a thread.
/// </summary>
/// <remarks>
/// <para>
/// The desktop editor's <c>PlayModeManager</c> stops the world by reloading the scene from a YAML snapshot,
/// which also hot-reloads compiled C# scripts. A web session keeps the scene and only decides whether it advances,
/// so this takes the same idea without the reload: the authored values are captured when play starts and written
/// back when it stops, in place. Ids survive, so the selection, the hierarchy and every panel keep working across
/// play and stop.
/// </para>
/// <para>
/// "Authored" means everything the inspector can change: the name, the transform, parenting, which objects
/// exist, which components are on them, whether each of them is enabled, and every editable property of those
/// components. What it does not
/// capture is script-private state: a script keeps its own fields, so a second play picks up where the first
/// one left off rather than from a fresh instance. The desktop editor gets that from its reload; a web session
/// cannot, because its reload compiles for the next load rather than swapping code under a live component.
/// </para>
/// <para>
/// Nothing here starts anything: the engine starts the scene when it loads it, exactly as a game run would.
/// Play only decides whether the world advances. That is also why stopping cannot re-run <c>Start()</c> a
/// second time; doing so would register a second physics body for every <see cref="RigidBody"/>.
/// </para>
/// </remarks>
public sealed class EditorPlaySession
{
    private readonly SceneManager _scenes;
    private readonly Action<string> _log;

    private SceneValues? _values;

    /// <param name="scenes">The engine's scenes; play advances whichever scene is active.</param>
    /// <param name="log">Where the session explains itself, usually <c>Logger.Info</c>.</param>
    public EditorPlaySession(SceneManager scenes, Action<string>? log = null)
    {
        _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        _log = log ?? (_ => { });
    }

    /// <summary>Where the transport is; only ever changed by <see cref="Apply"/>.</summary>
    public EditorPlayState State { get; private set; } = EditorPlayState.Editing;

    /// <summary>True while the world advances, i.e. physics and scripts are being stepped.</summary>
    public bool IsRunning => State == EditorPlayState.Playing;

    /// <summary>True while there is a scene the transport can put back.</summary>
    public bool HasSnapshot => _values is not null;

    /// <summary>
    /// Carries out a transport action, returning false when there was nothing to do: playing twice, pausing
    /// while editing, or stopping when nothing ran.
    /// </summary>
    public bool Apply(PlaybackRequest request) => request switch
    {
        PlaybackRequest.Play => Play(),
        PlaybackRequest.Pause => Pause(),
        PlaybackRequest.Stop => Stop(),
        PlaybackRequest.Restart => Restart(),
        _ => false
    };

    /// <summary>Advances physics, audio and scripts, but only while the scene is playing.</summary>
    public void Tick(float deltaTime)
    {
        if (IsRunning)
        {
            _scenes.Update(deltaTime);
        }
    }

    /// <summary>Starts running, or carries on after a pause. The snapshot is taken on the way out of editing.</summary>
    public bool Play()
    {
        if (State == EditorPlayState.Playing)
        {
            return false;
        }

        if (_scenes.ActiveScene is null)
        {
            _log("[play] There is no scene open to play.");
            return false;
        }

        bool resuming = State == EditorPlayState.Paused;

        // A resume keeps the snapshot: the values it holds are still the ones stop has to put back.
        if (!resuming && _values is null)
        {
            _values = Capture(_scenes.ActiveScene);
            _log($"[play] Captured {_values.Objects.Count} objects; playing.");
        }
        else
        {
            _log("[play] Playing.");
        }

        State = EditorPlayState.Playing;

        return true;
    }

    /// <summary>Freezes the scene. Only a running scene can be paused.</summary>
    public bool Pause()
    {
        if (State != EditorPlayState.Playing)
        {
            return false;
        }

        State = EditorPlayState.Paused;
        _log("[play] Paused.");

        return true;
    }

    /// <summary>Puts the authored values back and returns to editing.</summary>
    public bool Stop()
    {
        if (State == EditorPlayState.Editing)
        {
            return false;
        }

        Restore();
        State = EditorPlayState.Editing;
        _values = null;

        _log("[play] Stopped; the scene is back to how it was authored.");

        return true;
    }

    /// <summary>Puts the authored values back and plays again, without capturing a second snapshot.</summary>
    public bool Restart()
    {
        if (_values is null)
        {
            // Nothing has run yet, so restarting is just playing.
            return Play();
        }

        Restore();
        State = EditorPlayState.Playing;
        _log("[play] Restarted from the captured scene.");

        return true;
    }

    /// <summary>Writes the captured values back into the live scene, in place.</summary>
    private void Restore()
    {
        if (_values is not { } values || _scenes.ActiveScene is not { } scene || values.SceneId != scene.Id)
        {
            // The snapshot belongs to a scene that is no longer open, so there is nothing safe to put back.
            _values = null;
            return;
        }

        HashSet<Guid> authored = [.. values.Objects.Select(state => state.GameObject.Id)];
        Dictionary<Guid, GameObject> byId = values.Objects
            .ToDictionary(state => state.GameObject.Id, state => state.GameObject);

        // Objects play created are not authored, so they go. Removing them through the manager also takes
        // their physics bodies out of the simulation.
        foreach (GameObject gameObject in scene.GameObjects.Where(go => !authored.Contains(go.Id)).ToArray())
        {
            _scenes.RemoveGameObject(gameObject);
        }

        foreach (ObjectValues state in values.Objects)
        {
            // Play can also take an object away, through undo of an add; re-add every missing object before
            // restoring component properties so cross-object references resolve regardless of hierarchy order.
            if (!scene.GameObjects.Contains(state.GameObject))
            {
                _scenes.AddGameObject(state.GameObject);
            }
        }

        foreach (ObjectValues state in values.Objects)
        {
            GameObject gameObject = state.GameObject;

            gameObject.Name = state.Name;
            gameObject.Transform.Position = state.Position;
            gameObject.Transform.Rotation = state.Rotation;
            gameObject.Transform.Scale = state.Scale;

            RestoreComponents(gameObject, state.Components);

            foreach (ParticleSystem ps in gameObject.GetComponents<ParticleSystem>())
            {
                ps.Stop(ParticleStopBehavior.StopEmittingAndClear);
            }

            foreach (NavMeshAgent agent in gameObject.GetComponents<NavMeshAgent>())
            {
                agent.ResetPath();
            }

            if (gameObject.GetComponent<RigidBody>() is { } body)
            {
                // Once physics has stepped the body is the authority, so the transform alone would be
                // overwritten on the next step. Teleport moves the body too, and a leftover velocity would
                // throw the object straight back out of position.
                body.Teleport(state.Position, state.Rotation);
                body.SetVelocity(Vector3.Zero);
            }
        }

        // Parenting last: a child can only be given back a parent that is already back in the scene.
        foreach (ObjectValues state in values.Objects)
        {
            state.GameObject.Transform.Parent =
                state.ParentId is { } parentId && byId.TryGetValue(parentId, out GameObject? parent)
                    ? parent.Transform
                    : null;
        }

        // Play can add a camera or a light, and take the main one away with it; the renderer needs a camera.
        scene.FindMainComponents();
        scene.IsDirty = true;
    }

    /// <summary>Drops the components play added, puts back the ones it removed, then writes the values.</summary>
    private static void RestoreComponents(GameObject gameObject, IReadOnlyList<ComponentValues> components)
    {
        HashSet<Component> live = [.. gameObject.Components];
        HashSet<Component> authored = [.. components.Select(state => state.Component)];

        foreach (ComponentValues state in components)
        {
            if (!live.Contains(state.Component))
            {
                gameObject.AddComponent(state.Component);
            }
        }

        foreach (Component component in live.Where(component => !authored.Contains(component)).ToArray())
        {
            gameObject.RemoveComponent(component);
        }

        foreach (ComponentValues state in components)
        {
            state.Component.Enabled = state.Enabled;

            foreach ((string name, string text) in state.Properties)
            {
                // A property that no longer exists is not a reason to abort the rest of the restore.
                ComponentProperties.TryApply(state.Component, name, text, out _);
            }
        }
    }

    /// <summary>Takes a copy of everything the inspector can change, by value.</summary>
    private static SceneValues Capture(Scene scene)
    {
        List<ObjectValues> objects = [];

        foreach (GameObject gameObject in scene.GameObjects)
        {
            List<ComponentValues> components = [.. gameObject.Components.Select(component => new ComponentValues(
                component,
                component.Enabled,
                [.. ComponentProperties.Read(component).Select(property => (property.Name, property.Text))]))];

            objects.Add(new ObjectValues(
                gameObject,
                gameObject.Name,
                gameObject.Transform.Position,
                gameObject.Transform.Rotation,
                gameObject.Transform.Scale,
                gameObject.Transform.Parent?.GameObject.Id,
                components));
        }

        return new SceneValues(scene.Id, objects);
    }

    /// <summary>The scene a snapshot was taken from, so it can never be written into a different one.</summary>
    private sealed record SceneValues(Guid SceneId, IReadOnlyList<ObjectValues> Objects);

    private sealed record ObjectValues(
        GameObject GameObject,
        string Name,
        Vector3 Position,
        Quaternion Rotation,
        Vector3 Scale,
        Guid? ParentId,
        IReadOnlyList<ComponentValues> Components);

    private sealed record ComponentValues(
        Component Component,
        bool Enabled,
        IReadOnlyList<(string Name, string Text)> Properties);
}
