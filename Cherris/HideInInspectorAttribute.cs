namespace Cherris;

/// <summary>
/// When placed on a public property of a Component, this attribute prevents
/// the property from being displayed in the editor's inspector panel.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class HideInInspectorAttribute : Attribute
{
}