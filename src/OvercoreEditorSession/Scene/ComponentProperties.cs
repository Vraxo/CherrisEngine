using Overcore.Attributes;
using Overcore.Components;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace OvercoreEditorSession;

/// <summary>The kinds of value the inspector knows how to draw and edit.</summary>
public enum PropertyKind
{
    /// <summary>A string.</summary>
    Text,

    /// <summary>A float; <see cref="PropertySnapshot.Minimum"/> and <see cref="PropertySnapshot.Maximum"/> are set when it has a range.</summary>
    Number,

    /// <summary>An integer.</summary>
    Integer,

    /// <summary>A toggle.</summary>
    Boolean,

    /// <summary>One name from <see cref="PropertySnapshot.Choices"/>, i.e. an enum.</summary>
    Choice,

    /// <summary>Two floats.</summary>
    Vector2,

    /// <summary>Three floats; <see cref="PropertySnapshot.IsColor"/> marks a colour.</summary>
    Vector3
}

/// <summary>
/// One editable property of a component, flattened for the inspector.
/// </summary>
/// <param name="Name">Property name, which the edit command quotes back.</param>
/// <param name="Kind">How to draw it.</param>
/// <param name="Text">
/// The value in the format an edit sends back: the string itself, <c>true</c>/<c>false</c>, an enum name, or
/// the components separated by ", ". Kept as text because that is what the UI collects and what a command
/// carries; parsing happens once, on the render thread.
/// </param>
/// <param name="Components">Numeric components: none for text, toggle and choice; one or more otherwise.</param>
/// <param name="Minimum">Range lower bound, when the property declares one.</param>
/// <param name="Maximum">Range upper bound, when the property declares one.</param>
/// <param name="IsColor">True when a <see cref="Vector3"/> is marked <see cref="ColorUsageAttribute"/>.</param>
/// <param name="Choices">Enum names, for <see cref="PropertyKind.Choice"/>.</param>
/// <param name="DefaultText">
/// The value a freshly constructed component of this type has, in the same format as <paramref name="Text"/>, or
/// null when the type cannot be constructed and so has no default to offer. The reset button restores it.
/// </param>
public sealed record PropertySnapshot(
    string Name,
    PropertyKind Kind,
    string Text,
    IReadOnlyList<float> Components,
    IReadOnlyList<string> Choices,
    float? Minimum = null,
    float? Maximum = null,
    bool IsColor = false,
    string? DefaultText = null)
{
    /// <summary>True when a toggle is on, so markup can bind it without comparing strings.</summary>
    public bool IsOn => String.Equals(Text, "true", StringComparison.Ordinal);

    /// <summary>
    /// True when the property differs from the value a new component would have, which is when resetting it does
    /// something. Compared as text, because that is what an edit carries and what the field shows.
    /// </summary>
    public bool CanReset => DefaultText is not null && !String.Equals(Text, DefaultText, StringComparison.Ordinal);

    /// <summary>
    /// True when the declared range is one a slider can show. <c>[Range(0, float.MaxValue)]</c> (Light's
    /// intensity) is a clamp, not a slider, so such a property keeps the plain number field.
    /// </summary>
    public bool HasBoundedRange =>
        TryGetRange(out float minimum, out float maximum) &&
        Single.IsFinite(maximum) && maximum - minimum < 1_000_000f;

    /// <summary>The declared bounds, when the property declares both.</summary>
    /// <param name="minimum">Lower bound, or 0 when there is none.</param>
    /// <param name="maximum">Upper bound, or 0 when there is none.</param>
    public bool TryGetRange(out float minimum, out float maximum)
    {
        minimum = Minimum ?? 0f;
        maximum = Maximum ?? 0f;

        return Minimum is not null && Maximum is not null;
    }

    /// <summary>
    /// Per-pixel speed for dragging the value horizontally, mirroring the ImGui inspector: 0.01 for a plain
    /// float (its <c>DragFloat</c>), the spread of a bounded range over 200 pixels (so a 0…1 volume does not
    /// jump from end to end in ten), 1 for an integer (its <c>DragInt</c>), 0.1 for one vector component
    /// (its <c>DragFloat3</c>) and a 0…1 colour step of 1/255 for a colour (its <c>ColorEdit3</c>).
    /// </summary>
    public float DragSpeed => Kind switch
    {
        PropertyKind.Integer => 1f,
        PropertyKind.Number when HasBoundedRange && TryGetRange(out float minimum, out float maximum)
            => (maximum - minimum) / 200f,
        PropertyKind.Number => 0.01f,
        PropertyKind.Vector3 when IsColor => 1f / 255f,
        _ => 0.1f
    };
}

/// <summary>
/// The reflection bridge between a live <see cref="Component"/> and the inspector: which properties it shows,
/// how to read one as text, and how to write one back from text.
/// </summary>
/// <remarks>
/// <para>
/// The rules are the ImGui inspector's (<c>DefaultInspector</c>, <c>PropertyDrawer</c>), so both windows list
/// the same properties in the same order: public instance properties with a getter and a setter, minus
/// <see cref="HideInInspectorAttribute"/>.
/// </para>
/// <para>
/// Reading and writing both go through text, because the UI is a text form and a command is a message; the
/// value is parsed exactly once, on the render thread, where a failure is a logged line rather than an
/// exception in a render.
/// </para>
/// </remarks>
public static class ComponentProperties
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

    // The scene is snapshotted every frame, so the reflection walk is done once per type rather than per frame.
    // A component's editable properties do not change at runtime: the attributes are on the type.
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> EditableCache = new();

    // What a reset puts back: the value a freshly constructed component of the type has, which is the rule the
    // ImGui inspector's reset button follows (Activator.CreateInstance, then read the property). Cached per type
    // because the snapshot is rebuilt every frame and constructing a component is not free.
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, string>> DefaultsCache = new();

    // Handed to the readers that build the cache: the defaults of a default instance are what is being computed.
    private static readonly IReadOnlyDictionary<string, string> NoDefaults = new Dictionary<string, string>();

    /// <summary>The properties the inspector shows for a component type, in declaration order.</summary>
    /// <param name="componentType">A concrete <see cref="Component"/> type.</param>
    public static IReadOnlyList<PropertyInfo> Editable(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        return EditableCache.GetOrAdd(componentType, static type => type.GetProperties(Flags)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => !property.IsDefined(typeof(HideInInspectorAttribute), false))
            .Where(property => KindOf(property.PropertyType) is not null)
            .ToArray());
    }

    /// <summary>Reads every editable property of a component.</summary>
    /// <param name="component">The component.</param>
    public static IReadOnlyList<PropertySnapshot> Read(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        List<PropertySnapshot> properties = [];
        IReadOnlyDictionary<string, string> defaults = Defaults(component.GetType());

        foreach (PropertyInfo property in Editable(component.GetType()))
        {
            if (Read(component, property, defaults) is { } snapshot)
            {
                properties.Add(snapshot);
            }
        }

        return properties;
    }

    /// <summary>The default of every editable property of a component type, as text, keyed by property name.</summary>
    /// <param name="componentType">A concrete <see cref="Component"/> type.</param>
    /// <remarks>
    /// The defaults come from one instance constructed the way the Add menu constructs a component, so they are
    /// the values its own initialisers write. A type that cannot be constructed that way has no defaults, and its
    /// properties offer no reset button rather than a wrong value.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Defaults(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        return DefaultsCache.GetOrAdd(componentType, BuildDefaults);
    }

    /// <summary>Reads the value a freshly constructed component of this type has for one property.</summary>
    /// <param name="componentType">A concrete <see cref="Component"/> type.</param>
    /// <param name="propertyName">Property name.</param>
    /// <param name="text">The default, in the format an edit sends.</param>
    /// <returns>False when the type has no default for that property.</returns>
    public static bool TryReadDefaultText(Type componentType, string propertyName, out string text)
    {
        if (Defaults(componentType).TryGetValue(propertyName, out string? value))
        {
            text = value;
            return true;
        }

        text = string.Empty;
        return false;
    }

    /// <summary>Reads one property, or null when it is not one the inspector shows.</summary>
    /// <param name="component">The component.</param>
    /// <param name="propertyName">Property name.</param>
    public static PropertySnapshot? Read(Component component, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(component);

        PropertyInfo? property = Editable(component.GetType())
            .FirstOrDefault(candidate => String.Equals(candidate.Name, propertyName, StringComparison.Ordinal));

        return property is null ? null : Read(component, property, Defaults(component.GetType()));
    }

    /// <summary>Reads one property as the text an edit would send back.</summary>
    /// <param name="component">The component.</param>
    /// <param name="propertyName">Property name.</param>
    /// <param name="text">The value as text.</param>
    /// <returns>False when the component has no such editable property.</returns>
    public static bool TryReadText(Component component, string propertyName, out string text)
    {
        if (Read(component, propertyName) is { } snapshot)
        {
            text = snapshot.Text;
            return true;
        }

        text = string.Empty;
        return false;
    }

    /// <summary>Writes a property from the text an edit carries.</summary>
    /// <param name="component">The component to change.</param>
    /// <param name="propertyName">Property name.</param>
    /// <param name="text">The new value, in the format <see cref="PropertySnapshot.Text"/> uses.</param>
    /// <param name="error">Why the text was rejected, or null.</param>
    /// <returns>True when the property now holds the value.</returns>
    public static bool TryApply(Component component, string propertyName, string text, out string? error)
    {
        ArgumentNullException.ThrowIfNull(component);

        PropertyInfo? property = Editable(component.GetType())
            .FirstOrDefault(candidate => String.Equals(candidate.Name, propertyName, StringComparison.Ordinal));

        if (property is null)
        {
            error = $"'{component.GetType().Name}' has no editable property named '{propertyName}'.";
            return false;
        }

        string trimmed = (text ?? string.Empty).Trim();

        PropertyKind? kind = KindOf(property.PropertyType);

        switch (kind)
        {
            case PropertyKind.Text:
                property.SetValue(component, trimmed);
                error = null;
                return true;

            case PropertyKind.Boolean:
                if (!Boolean.TryParse(trimmed, out bool flag))
                {
                    error = $"'{property.Name}' is a toggle, so it wants true or false, not '{text}'.";
                    return false;
                }

                property.SetValue(component, flag);
                error = null;
                return true;

            case PropertyKind.Choice:
                object? parsed = ParseChoice(property.PropertyType, trimmed);

                if (parsed is null)
                {
                    error = $"'{property.Name}' does not have a value called '{text}'. " +
                        $"It takes one of: {String.Join(", ", Enum.GetNames(property.PropertyType))}.";
                    return false;
                }

                property.SetValue(component, parsed);
                error = null;
                return true;

            case PropertyKind.Integer:
            case PropertyKind.Number:
            case PropertyKind.Vector2:
            case PropertyKind.Vector3:
                string[] parts = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                int expected = kind == PropertyKind.Vector3 ? 3 : kind == PropertyKind.Vector2 ? 2 : 1;

                if (parts.Length != expected)
                {
                    error = $"'{property.Name}' wants {expected} number{(expected == 1 ? string.Empty : "s")}, not '{text}'.";
                    return false;
                }

                float[] numbers = new float[expected];

                for (int index = 0; index < expected; index++)
                {
                    if (!Single.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]))
                    {
                        error = $"'{property.Name}' wants numbers, and '{parts[index]}' is not one.";
                        return false;
                    }
                }

                RangeAttribute? range = property.GetCustomAttribute<RangeAttribute>();

                for (int index = 0; index < expected; index++)
                {
                    numbers[index] = range is null ? numbers[index] : Math.Clamp(numbers[index], range.Min, range.Max);
                }

                property.SetValue(component, kind switch
                {
                    PropertyKind.Integer => (object)(int)MathF.Round(numbers[0]),
                    PropertyKind.Number => numbers[0],
                    _ => BuildValue(property.PropertyType, numbers)
                });
                error = null;
                return true;

            default:
                error = $"'{property.Name}' is a {property.PropertyType.Name}, which the inspector cannot edit.";
                return false;
        }
    }

    private static PropertyKind? KindOf(Type type)
    {
        if (type == typeof(string))
        {
            return PropertyKind.Text;
        }

        if (type == typeof(bool))
        {
            return PropertyKind.Boolean;
        }

        if (type == typeof(int))
        {
            return PropertyKind.Integer;
        }

        if (type == typeof(float))
        {
            return PropertyKind.Number;
        }

        if (type.IsEnum)
        {
            return PropertyKind.Choice;
        }

        if (type == typeof(Vector2))
        {
            return PropertyKind.Vector2;
        }

        return type == typeof(Vector3) ? PropertyKind.Vector3 : null;
    }

    private static object? ParseChoice(Type type, string name)
    {
        return Enum.GetNames(type).Contains(name, StringComparer.Ordinal)
            ? Enum.Parse(type, name)
            : null;
    }

    private static object BuildValue(Type type, float[] numbers)
    {
        return type == typeof(Vector2)
            ? new Vector2(numbers[0], numbers[1])
            : new Vector3(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>Reads one property and states the default it would be reset to, when its type has one.</summary>
    private static PropertySnapshot? Read(
        Component component, PropertyInfo property, IReadOnlyDictionary<string, string> defaults)
    {
        if (Read(component, property) is not { } snapshot)
        {
            return null;
        }

        return defaults.TryGetValue(property.Name, out string? text) ? snapshot with { DefaultText = text } : snapshot;
    }

    /// <summary>Reads every editable property of a default instance, as the text a reset would write.</summary>
    private static IReadOnlyDictionary<string, string> BuildDefaults(Type componentType)
    {
        Dictionary<string, string> defaults = [];

        Component? instance;

        try
        {
            instance = Activator.CreateInstance(componentType) as Component;
        }
        catch (Exception)
        {
            // No parameterless constructor, or one that cannot run without a file: nothing to reset to.
            return defaults;
        }

        if (instance is null)
        {
            return defaults;
        }

        foreach (PropertyInfo property in Editable(componentType))
        {
            try
            {
                if (Read(instance, property, NoDefaults) is { } snapshot)
                {
                    defaults[property.Name] = snapshot.Text;
                }
            }
            catch (Exception)
            {
                // A getter that throws on a bare instance: that property keeps no default.
            }
        }

        return defaults;
    }

    private static PropertySnapshot? Read(Component component, PropertyInfo property)
    {
        PropertyKind? kind = KindOf(property.PropertyType);
        object? value = property.GetValue(component);

        if (kind is null || value is null)
        {
            return null;
        }

        RangeAttribute? range = property.GetCustomAttribute<RangeAttribute>();
        bool isColor = property.GetCustomAttribute<ColorUsageAttribute>() is not null;

        return kind switch
        {
            PropertyKind.Text => new PropertySnapshot(property.Name, kind.Value, (string)value, [], [], range?.Min, range?.Max),

            PropertyKind.Boolean => new PropertySnapshot(
                property.Name, kind.Value, (bool)value ? "true" : "false", [], [], range?.Min, range?.Max),

            PropertyKind.Choice => new PropertySnapshot(
                property.Name, kind.Value, value.ToString() ?? string.Empty, [], Enum.GetNames(property.PropertyType),
                range?.Min, range?.Max),

            PropertyKind.Integer => new PropertySnapshot(
                property.Name, kind.Value, Format(((int)value).ToString(CultureInfo.InvariantCulture)), [(int)value], [],
                range?.Min, range?.Max),

            PropertyKind.Number => new PropertySnapshot(
                property.Name, kind.Value, Format(Number((float)value)), [(float)value], [], range?.Min, range?.Max),

            PropertyKind.Vector2 => new PropertySnapshot(
                property.Name, kind.Value, Format(Number(((Vector2)value).X), Number(((Vector2)value).Y)),
                [((Vector2)value).X, ((Vector2)value).Y], [], range?.Min, range?.Max),

            _ => new PropertySnapshot(
                property.Name, kind.Value,
                Format(Number(((Vector3)value).X), Number(((Vector3)value).Y), Number(((Vector3)value).Z)),
                [((Vector3)value).X, ((Vector3)value).Y, ((Vector3)value).Z], [], range?.Min, range?.Max, isColor)
        };
    }

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Format(params string[] parts) => String.Join(", ", parts);
}
