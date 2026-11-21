using Cherris.Components;
using System.Numerics;

namespace Cherris.Core;

public class GameObject
{
    public string Name { get; set; }
    public Guid Id { get; }
    public Transform Transform { get; }
    private readonly List<Component> _components = new List<Component>();

    public IEnumerable<Component> Components => _components;

    public GameObject(string name = "GameObject", Guid? id = null)
    {
        Name = name;
        Id = id ?? Guid.NewGuid();
        Transform = new(this);
    }

    public T AddComponent<T>(T component) where T : Component
    {
        component.GameObject = this;
        _components.Add(component);
        return component;
    }

    public T? GetComponent<T>() where T : Component
    {
        return _components.OfType<T>().FirstOrDefault();
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        return _components.OfType<T>();
    }

    public void RemoveComponent<T>() where T : Component
    {
        var componentToRemove = GetComponent<T>();

        if (componentToRemove is null)
        {
            return;
        }

        _components.Remove(componentToRemove);
    }

    public void RemoveComponent(Component componentToRemove)
    {
        if (componentToRemove is null)
        {
            return;
        }

        _components.Remove(componentToRemove);
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