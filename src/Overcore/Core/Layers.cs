namespace Overcore.Core;

/// <summary>
/// The 32 layers a <see cref="GameObject"/> can be on, by number and by name. Layers decide what collides with
/// what (<see cref="Physics.IgnoreLayerCollision(int, int, bool)"/>) and what a query looks at.
/// </summary>
/// <remarks>
/// The first eight numbers carry Unity's names, so a project moved from Unity keeps its meaning. The rest are
/// unnamed until something names them; a layer with no name is named <c>"Layer N"</c> for display.
/// </remarks>
public static class Layers
{
    /// <summary>Layer 0, where an object starts.</summary>
    public const int Default = 0;

    /// <summary>Layer 1.</summary>
    public const int TransparentFX = 1;

    /// <summary>Layer 2, the layer Unity's default raycasts leave out.</summary>
    public const int IgnoreRaycast = 2;

    /// <summary>Layer 4.</summary>
    public const int Water = 4;

    /// <summary>Layer 5.</summary>
    public const int UI = 5;

    /// <summary>How many layers there are.</summary>
    public const int Count = 32;

    private static readonly string?[] Names = new string?[Count];

    static Layers()
    {
        Names[Default] = "Default";
        Names[TransparentFX] = "TransparentFX";
        Names[IgnoreRaycast] = "Ignore Raycast";
        Names[4] = "Water";
        Names[UI] = "UI";
    }

    /// <summary>The name of a layer, or <c>"Layer N"</c> when nothing named it.</summary>
    /// <param name="layer">A layer number from 0 to 31.</param>
    public static string NameOf(int layer)
    {
        if (layer is < 0 or >= Count)
        {
            return $"Layer {layer}";
        }

        return Names[layer] ?? $"Layer {layer}";
    }

    /// <summary>The number of a layer by name, or −1 when no layer has that name.</summary>
    /// <param name="name">The name to look for, compared exactly.</param>
    public static int NameToLayer(string name)
    {
        for (int layer = 0; layer < Count; layer++)
        {
            if (Names[layer] is { } existing && string.Equals(existing, name, StringComparison.Ordinal))
            {
                return layer;
            }
        }

        return -1;
    }

    /// <summary>Names a layer, which is what the inspector shows.</summary>
    /// <param name="layer">A layer number from 0 to 31.</param>
    /// <param name="name">The name to give it, or null to take the name away.</param>
    public static void SetName(int layer, string? name)
    {
        if (layer is < 0 or >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "A layer is a number from 0 to 31.");
        }

        Names[layer] = string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
