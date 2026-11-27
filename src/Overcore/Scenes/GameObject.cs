using Overcore.Components;
using Overcore.Serialization;
using System.Numerics;

namespace Overcore.Core;

public class GameObject
{
    /// <summary>The name layer comparisons use: <c>GameObject.CompareTag("Player")</c>.</summary>
    public const string Untagged = "Untagged";

    public string Name { get; set; }
    public Guid Id { get; }
    public Transform Transform { get; }

    /// <summary>
    /// The object's tag, Unity's <c>GameObject.tag</c>: a plain string, "Untagged" when nothing set one.
    /// </summary>
    public string Tag { get; set; } = Untagged;

    /// <summary>
    /// The object's layer, Unity's <c>GameObject.layer</c>: a number from 0 to 31 that physics and queries use to
    /// decide what collides with what (<see cref="Physics.IgnoreLayerCollision(int, int, bool)"/>).
    /// </summary>
    public int Layer
    {
        get => _layer;
        set
        {
            if (value is < 0 or > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A layer is a number from 0 to 31.");
            }

            _layer = value;
        }
    }

    /// <summary>The scene the object lives in, or null while it is still loose — Unity's <c>gameObject.scene</c>.</summary>
    public Scene? scene { get; internal set; }

    /// <summary>
    /// The object's home in a prefab asset, or null when it is not part of one. An instantiated object keeps
    /// the link on every object of the instance, so the editor can tell a prefab's objects from loose ones.
    /// </summary>
    public PrefabSource? PrefabSource { get; set; }

    /// <summary>True while the object itself is switched on, whatever its parents say — Unity's <c>activeSelf</c>.</summary>
    public bool ActiveSelf => _activeSelf;

    /// <summary>
    /// True while the object and every ancestor are switched on — Unity's <c>activeInHierarchy</c>. Components
    /// only run, and only draw, while this is true.
    /// </summary>
    public bool ActiveInHierarchy
    {
        get
        {
            for (Transform? transform = Transform; transform is not null; transform = transform.Parent)
            {
                if (!transform.GameObject._activeSelf)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool _activeSelf = true;
    private int _layer;
    private bool _componentsAwoken;

    private readonly List<Component> _components = new List<Component>();

    public IEnumerable<Component> Components => _components;

    public GameObject(string name = "GameObject", Guid? id = null)
    {
        Name = name;
        Id = id ?? Guid.NewGuid();
        Transform = new(this);
    }

    /// <summary>
    /// Switches the object, and everything under it, on or off — Unity's <c>SetActive</c>. Off means no
    /// <c>Update</c>, no rendering, and <c>OnDisable</c> down the hierarchy; on again means <c>OnEnable</c>.
    /// </summary>
    /// <param name="value">True to switch it on, false to switch it off.</param>
    public void SetActive(bool value)
    {
        if (_activeSelf == value)
        {
            return;
        }

        _activeSelf = value;
        RefreshActivation(AncestorsAreActive());
    }

    /// <summary>The name of the layer the object is on, from <see cref="Layers"/>.</summary>
    public string LayerName => Layers.NameOf(Layer);

    /// <summary>True when the object's tag is <paramref name="tag"/>.</summary>
    /// <param name="tag">The tag to compare against.</param>
    public bool CompareTag(string tag) => string.Equals(Tag, tag, StringComparison.Ordinal);

    public T AddComponent<T>(T component) where T : Component
    {
        ArgumentNullException.ThrowIfNull(component);

        component.GameObject = this;
        _components.Add(component);

        // Unity wakes a component the moment it is added to an object that is already running.
        if (_componentsAwoken && ActiveInHierarchy)
        {
            component.AwakeComponent();
        }

        return component;
    }

    public T AddComponent<T>() where T : Component, new() => AddComponent(new T());

    public T? GetComponent<T>() where T : Component
    {
        return _components.OfType<T>().FirstOrDefault();
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        return _components.OfType<T>();
    }

    /// <summary>
    /// Finds a component of the given type on this object, or failing that on any child — Unity's
    /// <c>GetComponentInChildren</c>, which searches the object itself first.
    /// </summary>
    /// <param name="includeInactive">True to look inside objects that are switched off.</param>
    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component
    {
        T? own = GetComponent<T>();

        if (own is not null)
        {
            return own;
        }

        foreach (Transform child in Transform.Children)
        {
            GameObject childObject = child.GameObject;

            if (!includeInactive && !childObject.ActiveInHierarchy)
            {
                continue;
            }

            if (childObject.GetComponentInChildren<T>(includeInactive) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Every component of the given type on this object and everything under it.</summary>
    /// <param name="includeInactive">True to look inside objects that are switched off.</param>
    public IEnumerable<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
    {
        if (includeInactive || ActiveInHierarchy)
        {
            foreach (T component in GetComponents<T>())
            {
                yield return component;
            }
        }

        foreach (Transform child in Transform.Children)
        {
            foreach (T component in child.GameObject.GetComponentsInChildren<T>(includeInactive))
            {
                yield return component;
            }
        }
    }

    /// <summary>Finds a component of the given type on the object or the nearest ancestor that has one.</summary>
    public T? GetComponentInParent<T>() where T : Component
    {
        for (Transform? transform = Transform; transform is not null; transform = transform.Parent)
        {
            if (transform.GameObject.GetComponent<T>() is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Every component of the given type on the object and each of its ancestors.</summary>
    public IEnumerable<T> GetComponentsInParent<T>() where T : Component
    {
        for (Transform? transform = Transform; transform is not null; transform = transform.Parent)
        {
            foreach (T component in transform.GameObject.GetComponents<T>())
            {
                yield return component;
            }
        }
    }

    public void RemoveComponent<T>() where T : Component
    {
        var componentToRemove = GetComponent<T>();

        if (componentToRemove is null)
        {
            return;
        }

        RemoveComponent(componentToRemove);
    }

    public void RemoveComponent(Component componentToRemove)
    {
        if (componentToRemove is null || !_components.Remove(componentToRemove))
        {
            return;
        }

        componentToRemove.DestroyComponent();
        componentToRemove.GameObject = null;
    }

    /// <summary>True once the object has been destroyed; a destroyed object still answers, but is not in any scene.</summary>
    public bool IsDestroyed { get; internal set; }

    /// <summary>
    /// Clones an object, everything under it and every component on it, into the scene — Unity's <c>Instantiate</c>.
    /// The clone gets fresh ids and its own components, but the same links to a prefab asset and the same shared
    /// assets (meshes, materials, clips), which is what makes instancing cheap.
    /// </summary>
    /// <param name="original">The object to copy.</param>
    /// <returns>The new object.</returns>
    public static GameObject Instantiate(GameObject original)
    {
        ArgumentNullException.ThrowIfNull(original);

        GameObject clone = CloneHierarchy(original);
        (original.scene ?? Scene.Active)?.AddGameObject(clone);
        clone.AssignScene(original.scene ?? Scene.Active);
        WakeClone(clone);

        return clone;
    }

    /// <summary>Clones an object and puts the clone under a parent, keeping its local place — Unity's <c>Instantiate</c>.</summary>
    /// <param name="original">The object to copy.</param>
    /// <param name="parent">The parent to put the clone under.</param>
    public static GameObject Instantiate(GameObject original, Transform parent) =>
        Instantiate(original, parent, worldPositionStays: false);

    /// <summary>Clones an object and puts the clone under a parent.</summary>
    /// <param name="original">The object to copy.</param>
    /// <param name="parent">The parent to put the clone under.</param>
    /// <param name="worldPositionStays">
    /// True keeps the clone where the original is in the world; false keeps the original's local values, so the
    /// clone moves with the new parent.
    /// </param>
    public static GameObject Instantiate(GameObject original, Transform parent, bool worldPositionStays)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(parent);

        GameObject clone = Instantiate(original);
        clone.Transform.SetParent(parent, worldPositionStays);

        return clone;
    }

    /// <summary>Clones an object and puts the clone at a place and angle — Unity's <c>Instantiate</c>.</summary>
    /// <param name="original">The object to copy.</param>
    /// <param name="position">Where the clone goes, in world space.</param>
    /// <param name="rotation">How the clone is turned, in world space.</param>
    public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation)
    {
        GameObject clone = Instantiate(original);
        clone.Transform.WorldPosition = position;
        clone.Transform.WorldRotation = rotation;

        return clone;
    }

    /// <summary>Clones an object and hands back a component of the given type on the clone — Unity's generic <c>Instantiate</c>.</summary>
    /// <param name="original">The component whose object is copied.</param>
    /// <returns>The same kind of component on the new object.</returns>
    public static T Instantiate<T>(T original) where T : Component
    {
        ArgumentNullException.ThrowIfNull(original);

        GameObject clone = Instantiate(original.GameObject
            ?? throw new InvalidOperationException("The component to instantiate is not on a GameObject."));

        return clone.GetComponent<T>()
            ?? throw new InvalidOperationException($"The clone has no {typeof(T).Name}, so it could not be copied.");
    }

    /// <summary>Destroys an object at the end of the frame — Unity's <c>Destroy</c>.</summary>
    /// <param name="target">The object to destroy.</param>
    public static void Destroy(GameObject target) => PendingDestruction.Enqueue(target, 0.0f);

    /// <summary>Destroys an object after a delay — Unity's <c>Destroy(Object, float)</c>.</summary>
    /// <param name="target">The object to destroy.</param>
    /// <param name="delay">Seconds to wait, in scaled time.</param>
    public static void Destroy(GameObject target, float delay) => PendingDestruction.Enqueue(target, delay);

    /// <summary>Takes a component off its object at the end of the frame — Unity's <c>Destroy</c>.</summary>
    /// <param name="target">The component to destroy.</param>
    public static void Destroy(Component target) => PendingDestruction.Enqueue(target, 0.0f);

    /// <summary>Takes a component off its object after a delay.</summary>
    /// <param name="target">The component to destroy.</param>
    /// <param name="delay">Seconds to wait, in scaled time.</param>
    public static void Destroy(Component target, float delay) => PendingDestruction.Enqueue(target, delay);

    /// <summary>Destroys an object on the spot rather than at the end of the frame — Unity's <c>DestroyImmediate</c>.</summary>
    /// <param name="target">The object to destroy.</param>
    public static void DestroyImmediate(GameObject target) => PendingDestruction.DestroyNow(target);

    /// <summary>Takes a component off its object on the spot — Unity's <c>DestroyImmediate</c>.</summary>
    /// <param name="target">The component to destroy.</param>
    public static void DestroyImmediate(Component target) => PendingDestruction.DestroyNow(target);

    /// <summary>
    /// Gives the object and everything under it a scene, which is what a clone needs before it can be destroyed or
    /// saved.
    /// </summary>
    /// <param name="owner">The scene to belong to, or null to belong to none.</param>
    internal void AssignScene(Scene? owner)
    {
        scene = owner;

        foreach (Transform child in Transform.Children)
        {
            child.GameObject.AssignScene(owner);
        }
    }

    /// <summary>Wakes a fresh clone if it landed somewhere active, as Unity does the moment it is instantiated.</summary>
    /// <param name="clone">The clone to wake.</param>
    private static void WakeClone(GameObject clone)
    {
        if (clone.ActiveInHierarchy)
        {
            clone.AwakeComponents();
        }
    }

    /// <summary>Clones a prefab asset into the active scene — Unity's <c>Instantiate(prefab)</c>.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        return prefab.Instantiate();
    }

    /// <summary>Clones a prefab asset at a world position and rotation.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab, Vector3 position, Quaternion rotation, Transform? parent = null)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        return prefab.Instantiate(position, rotation, parent);
    }

    /// <summary>Clones a prefab asset under <paramref name="parent"/>.</summary>
    public static GameObject? Instantiate(PrefabAsset prefab, Transform parent, bool worldPositionStays = false)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        return prefab.Instantiate(parent, worldPositionStays);
    }

    /// <summary>Copies an object and everything under it. Components are copied by value, runtime state and all cleared.</summary>
    /// <param name="source">The object to copy.</param>
    private static GameObject CloneHierarchy(GameObject source)
    {
        Dictionary<GameObject, GameObject> map = [];
        GameObject clone = CloneHierarchyNode(source, map);
        ObjectReferenceResolver.RemapClonedHierarchy(map);
        return clone;
    }

    private static GameObject CloneHierarchyNode(GameObject source, Dictionary<GameObject, GameObject> map)
    {
        GameObject clone = new(source.Name)
        {
            Tag = source.Tag,
            Layer = source.Layer,
            PrefabSource = source.PrefabSource
        };

        map[source] = clone;

        clone.SetActiveWithoutNotification(source.ActiveSelf);
        clone.Transform.Position = source.Transform.Position;
        clone.Transform.Rotation = source.Transform.Rotation;
        clone.Transform.Scale = source.Transform.Scale;

        foreach (Component component in source.Components)
        {
            clone.AddComponent(PendingDestruction.CloneComponent(component));
        }

        foreach (Transform child in source.Transform.Children)
        {
            GameObject childClone = CloneHierarchyNode(child.GameObject, map);
            childClone.Transform.Parent = clone.Transform;
        }

        return clone;
    }

    /// <summary>Finds an object in the active scene by name, Unity's <c>GameObject.Find</c>.</summary>
    /// <param name="name">The name to look for, compared exactly.</param>
    /// <returns>The first match, or null when nothing has that name.</returns>
    public static GameObject? Find(string name)
    {
        if (string.IsNullOrEmpty(name) || Scene.Active is not { } scene)
        {
            return null;
        }

        return scene.GameObjects.FirstOrDefault(gameObject => gameObject.Name == name);
    }

    /// <summary>Finds an object in the active scene by tag, Unity's <c>GameObject.FindWithTag</c>.</summary>
    /// <param name="tag">The tag to look for.</param>
    public static GameObject? FindWithTag(string tag) => FindGameObjectsWithTag(tag).FirstOrDefault();

    /// <summary>Finds every object in the active scene that carries a tag, Unity's <c>FindGameObjectsWithTag</c>.</summary>
    /// <param name="tag">The tag to look for.</param>
    public static GameObject[] FindGameObjectsWithTag(string tag)
    {
        if (string.IsNullOrEmpty(tag) || Scene.Active is not { } scene)
        {
            return [];
        }

        return [.. scene.GameObjects.Where(gameObject => gameObject.CompareTag(tag))];
    }

    /// <summary>
    /// Switches every component on the object and under it on or off to match the hierarchy. The scene starts
    /// objects this way, and <see cref="SetActive"/> does the same when a switch moves.
    /// </summary>
    /// <param name="ancestorActive">Whether the parents above the object are all switched on.</param>
    internal void RefreshActivation(bool ancestorActive)
    {
        bool active = ancestorActive && _activeSelf;

        foreach (Component component in _components)
        {
            if (active)
            {
                component.ActivateComponent();
            }
            else
            {
                component.DeactivateComponent();
            }
        }

        foreach (Transform child in Transform.Children)
        {
            child.GameObject.RefreshActivation(active);
        }
    }

    /// <summary>
    /// Runs <c>Awake</c> on every component of the object and under it, marking them awoken, and then switches
    /// the ones that are on. The scene does this once, before any script's <c>Start</c>.
    /// </summary>
    internal void AwakeComponents()
    {
        foreach (Component component in _components)
        {
            component.AwakeComponent();
        }

        _componentsAwoken = true;

        foreach (Transform child in Transform.Children)
        {
            child.GameObject.AwakeComponents();
        }
    }

    /// <summary>Marks the object as already woken, without running anything: used when a clone copies a hierarchy.</summary>
    internal void MarkAwoken() => _componentsAwoken = true;

    /// <summary>Switches the object on or off without touching its components, for building a hierarchy first.</summary>
    internal void SetActiveWithoutNotification(bool value) => _activeSelf = value;

    /// <summary>Brings every component of the object and under it down: <c>OnDisable</c>, <c>OnDestroy</c>.</summary>
    internal void DestroyComponents()
    {
        foreach (Transform child in Transform.Children.ToList())
        {
            child.GameObject.DestroyComponents();
        }

        foreach (Component component in _components.ToList())
        {
            component.DestroyComponent();
        }
    }

    private bool AncestorsAreActive()
    {
        for (Transform? transform = Transform.Parent; transform is not null; transform = transform.Parent)
        {
            if (!transform.GameObject._activeSelf)
            {
                return false;
            }
        }

        return true;
    }

    public BoundingBox GetWorldSpaceAABB()
    {
        var meshRenderer = GetComponent<MeshRenderer>();
        
        if (meshRenderer?.Mesh is null)
        {
            // If there's no mesh, check for other components that should be selectable
            if (GetComponent<Light>() is not null)
            {
                const float selectionVolumeSize = 0.5f;
                Vector3 halfSize = new(selectionVolumeSize / 2);

                return new()
                {
                    Min = Transform.Position - halfSize,
                    Max = Transform.Position + halfSize
                };
            }

            // Default fallback for objects with no visible/selectable component
            return new()
            {
                Min = Transform.Position,
                Max = Transform.Position
            };
        }

        BoundingBox localAABB = meshRenderer.Mesh.AABB;
        Matrix4x4 worldTransform = Transform.GetModelMatrix();

        Vector3[] corners = {
            new(localAABB.Min.X, localAABB.Min.Y, localAABB.Min.Z),
            new(localAABB.Max.X, localAABB.Min.Y, localAABB.Min.Z),
            new(localAABB.Min.X, localAABB.Max.Y, localAABB.Min.Z),
            new(localAABB.Min.X, localAABB.Min.Y, localAABB.Max.Z),
            new(localAABB.Max.X, localAABB.Max.Y, localAABB.Min.Z),
            new(localAABB.Min.X, localAABB.Max.Y, localAABB.Max.Z),
            new(localAABB.Max.X, localAABB.Min.Y, localAABB.Max.Z),
            new(localAABB.Max.X, localAABB.Max.Y, localAABB.Max.Z)
        };

        Vector3 worldMin = new(float.MaxValue);
        Vector3 worldMax = new(float.MinValue);

        foreach (Vector3 corner in corners)
        {
            Vector3 transformedCorner = Vector3.Transform(corner, worldTransform);
            worldMin = Vector3.Min(worldMin, transformedCorner);
            worldMax = Vector3.Max(worldMax, transformedCorner);
        }

        return new()
        {
            Min = worldMin,
            Max = worldMax
        };
    }
}
