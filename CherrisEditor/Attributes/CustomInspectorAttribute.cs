using System;
using Cherris;

namespace CherrisEditor;

/// <summary>
/// Marks a class as a custom inspector for a specific component type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CustomInspectorAttribute : Attribute
{
    public Type InspectedType { get; }

    public CustomInspectorAttribute(Type inspectedType)
    {
        InspectedType = inspectedType;
    }
}