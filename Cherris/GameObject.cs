using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Cherris;

public class GameObject
{
    public string Name { get; }
    public Transform Transform { get; }
    private readonly List<Component> _components = new List<Component>();

    // ADDED: Public accessor for the component list
    public IEnumerable<Component> Components => _components;

    public GameObject(string name = "GameObject")
    {
        Name = name;
        Transform = new Transform();
    }

    public T AddComponent<T>(T component) where T : Component
    {
        component.GameObject = this;
        _components.Add(component);
        return component;
    }

    public T GetComponent<T>() where T : Component
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
        if (componentToRemove is not null)
        {
            _components.Remove(componentToRemove);
        }
    }

    public BoundingBox GetWorldSpaceAABB()
    {
        var meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer?.Mesh is null)
        {
            return new BoundingBox(Transform.Position, Transform.Position);
        }

        var localAABB = meshRenderer.Mesh.AABB;
        var worldTransform = Transform.GetModelMatrix();

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

        var worldMin = new Vector3(float.MaxValue);
        var worldMax = new Vector3(float.MinValue);

        foreach (var corner in corners)
        {
            var transformedCorner = Vector3.Transform(corner, worldTransform);
            worldMin = Vector3.Min(worldMin, transformedCorner);
            worldMax = Vector3.Max(worldMax, transformedCorner);
        }

        return new BoundingBox(worldMin, worldMax);
    }
}