using System;
using System.Collections.Generic;
using System.Linq;

namespace Cherris;

public class GameObject
{
    public string Name { get; }
    public Transform Transform { get; }
    private readonly List<Component> _components = new List<Component>();

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
}