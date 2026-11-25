namespace Overcore.Core;

/// <summary>
/// A set of the 32 layers, one bit each, Unity's <c>LayerMask</c>: what a query is allowed to look at, and what a
/// pair of layers is allowed to do.
/// </summary>
/// <remarks>
/// A mask converts to and from <see cref="int"/> implicitly, so every query takes a plain layer number, a mask, or
/// a mask built by name: <c>Physics.Raycast(origin, direction, out hit, 100f, LayerMask.GetMask("Ground"))</c>.
/// </remarks>
public readonly struct LayerMask : IEquatable<LayerMask>
{
    private readonly int _value;

    /// <summary>Wraps a set of layer bits.</summary>
    /// <param name="value">The bits, one per layer.</param>
    public LayerMask(int value) => _value = value;

    /// <summary>The bits as a number.</summary>
    public int Value => _value;

    /// <summary>A mask that holds everything.</summary>
    public static LayerMask Everything => new(~0);

    /// <summary>A mask that holds nothing.</summary>
    public static LayerMask Nothing => new(0);

    /// <summary>The mask of <see cref="Layers.IgnoreRaycast"/> alone.</summary>
    public static LayerMask IgnoreRaycast => new(1 << Layers.IgnoreRaycast);

    /// <summary>The bit of one layer.</summary>
    /// <param name="layer">A layer number from 0 to 31.</param>
    public static int ToMask(int layer) => layer is < 0 or >= Layers.Count ? 0 : 1 << layer;

    /// <summary>The mask of every layer named.</summary>
    /// <param name="layerNames">Layer names; names no layer has are skipped.</param>
    public static LayerMask GetMask(params string[] layerNames)
    {
        ArgumentNullException.ThrowIfNull(layerNames);

        int mask = 0;

        foreach (string name in layerNames)
        {
            int layer = Layers.NameToLayer(name);

            if (layer >= 0)
            {
                mask |= 1 << layer;
            }
        }

        return new(mask);
    }

    /// <summary>The layers in the mask, by name.</summary>
    public string[] LayersInMask()
    {
        List<string> names = [];

        for (int layer = 0; layer < Layers.Count; layer++)
        {
            if ((_value & (1 << layer)) != 0)
            {
                names.Add(Layers.NameOf(layer));
            }
        }

        return [.. names];
    }

    /// <summary>True when a layer is in the mask.</summary>
    /// <param name="layer">The layer to test.</param>
    public bool Contains(int layer) => layer is >= 0 and < Layers.Count && (_value & (1 << layer)) != 0;

    /// <summary>True when the two masks share at least one layer.</summary>
    /// <param name="other">The mask to test against.</param>
    public bool Overlaps(LayerMask other) => (_value & other._value) != 0;

    /// <summary>The mask as a number, so a query takes it without a cast.</summary>
    public static implicit operator int(LayerMask mask) => mask._value;

    /// <summary>Wraps a number as a mask, so a query takes a plain layer number.</summary>
    public static implicit operator LayerMask(int value) => new(value);

    /// <inheritdoc />
    public bool Equals(LayerMask other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LayerMask other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value;

    /// <summary>True when both masks hold the same layers.</summary>
    public static bool operator ==(LayerMask left, LayerMask right) => left.Equals(right);

    /// <summary>True when the masks hold different layers.</summary>
    public static bool operator !=(LayerMask left, LayerMask right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"LayerMask({Convert.ToString(_value, 2).PadLeft(Layers.Count, '0')})";
}
