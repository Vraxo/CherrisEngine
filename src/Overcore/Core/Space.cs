namespace Overcore.Core;

/// <summary>The frame a translation or rotation is measured in, Unity's <c>Space</c>.</summary>
public enum Space
{
    /// <summary>The object's own axes, so a translation follows the object as it turns.</summary>
    Self,

    /// <summary>The world's axes.</summary>
    World
}
