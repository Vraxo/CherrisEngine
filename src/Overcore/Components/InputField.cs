using Overcore.Core.Logging;
using System.Globalization;
using System.Text;

namespace Overcore.Components;

/// <summary>A single-line text entry control. Attach a <see cref="Text"/> for its value and an optional <see cref="Image"/> background.</summary>
public sealed class InputField : Component
{
    private string _value = String.Empty;
    private int _maxLength = 256;

    public string Value
    {
        get => _value;
        set => SetValue(Sanitize(value));
    }

    public string Placeholder { get; set; } = String.Empty;
    public System.Numerics.Vector3 PlaceholderColor { get; set; } = new(0.55f, 0.55f, 0.58f);
    public bool Interactable { get; set; } = true;
    public bool ReadOnly { get; set; }

    /// <summary>Maximum UTF-16 code units; zero means unlimited.</summary>
    public int MaxLength
    {
        get => _maxLength;
        set
        {
            _maxLength = Math.Max(0, value);
            Value = _value;
        }
    }

    /// <summary>Current insertion point in UTF-16 code units; runtime-only.</summary>
    internal int CaretPosition { get; private set; }

    /// <summary>Raised when the text value changes.</summary>
    public event Action<string>? ValueChanged;

    /// <summary>Raised when editing ends, for example after the user presses Enter or focuses another control.</summary>
    public event Action<string>? EndEdit;

    internal void InsertText(string text)
    {
        if (ReadOnly || String.IsNullOrEmpty(text))
        {
            return;
        }

        StringBuilder inserted = new();
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                continue;
            }

            string value = rune.ToString();
            if (_maxLength == 0 || _value.Length + inserted.Length + value.Length <= _maxLength)
            {
                inserted.Append(value);
            }
        }

        if (inserted.Length == 0)
        {
            return;
        }

        string updated = _value.Insert(CaretPosition, inserted.ToString());
        CaretPosition += inserted.Length;
        SetValue(updated, updateCaret: false);
    }

    internal void HandleKey(Key key, IReadOnlyList<int>? visualCaretStops = null)
    {
        switch (key)
        {
            case Key.Left:
                MoveCaret(visualCaretStops, -1, () => PreviousCharacterIndex(_value, CaretPosition));
                break;
            case Key.Right:
                MoveCaret(visualCaretStops, 1, () => NextCharacterIndex(_value, CaretPosition));
                break;
            case Key.Home:
                CaretPosition = 0;
                break;
            case Key.End:
                CaretPosition = _value.Length;
                break;
            case Key.BackSpace when !ReadOnly && CaretPosition > 0:
            {
                int previous = PreviousCharacterIndex(_value, CaretPosition);
                string withoutPrevious = _value.Remove(previous, CaretPosition - previous);
                CaretPosition = previous;
                SetValue(withoutPrevious, updateCaret: false);
                break;
            }
            case Key.Delete when !ReadOnly && CaretPosition < _value.Length:
            {
                int next = NextCharacterIndex(_value, CaretPosition);
                SetValue(_value.Remove(CaretPosition, next - CaretPosition), updateCaret: false);
                break;
            }
        }
    }

    internal void MoveCaretToEnd() => CaretPosition = _value.Length;

    private void MoveCaret(IReadOnlyList<int>? visualStops, int direction, Func<int> logicalFallback)
    {
        if (visualStops is null || visualStops.Count == 0)
        {
            CaretPosition = logicalFallback();
            return;
        }

        int current = 0;
        for (int index = 0; index < visualStops.Count; index++)
        {
            if (visualStops[index] == CaretPosition) { current = index; break; }
            if (Math.Abs(visualStops[index] - CaretPosition) < Math.Abs(visualStops[current] - CaretPosition)) current = index;
        }
        CaretPosition = visualStops[Math.Clamp(current + direction, 0, visualStops.Count - 1)];
    }

    internal void FinishEditing() => Notify(EndEdit, _value, "EndEdit");

    private void SetValue(string value, bool updateCaret = true)
    {
        if (_maxLength > 0 && value.Length > _maxLength)
        {
            int end = 0;
            foreach (int elementStart in StringInfo.ParseCombiningCharacters(value))
            {
                int next = StringInfo.GetNextTextElement(value, elementStart).Length + elementStart;
                if (next > _maxLength) break;
                end = next;
            }
            value = value[..end];
        }

        if (_value == value)
        {
            if (updateCaret)
            {
                CaretPosition = Math.Clamp(CaretPosition, 0, _value.Length);
            }
            return;
        }

        _value = value;
        if (updateCaret)
        {
            CaretPosition = _value.Length;
        }
        else
        {
            CaretPosition = Math.Clamp(CaretPosition, 0, _value.Length);
        }
        Notify(ValueChanged, _value, "ValueChanged");
    }

    private static string Sanitize(string? value)
    {
        if (String.IsNullOrEmpty(value))
        {
            return String.Empty;
        }

        StringBuilder sanitized = new();
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (!Rune.IsControl(rune))
            {
                sanitized.Append(rune.ToString());
            }
        }
        return sanitized.ToString();
    }

    private static int PreviousCharacterIndex(string value, int position)
    {
        int previous = 0;
        foreach (int elementStart in StringInfo.ParseCombiningCharacters(value))
        {
            if (elementStart >= position) break;
            previous = elementStart;
        }
        return previous;
    }

    private static int NextCharacterIndex(string value, int position)
    {
        foreach (int elementStart in StringInfo.ParseCombiningCharacters(value))
        {
            if (elementStart > position) return elementStart;
            if (elementStart == position)
            {
                return StringInfo.GetNextTextElement(value, elementStart).Length + elementStart;
            }
        }
        return value.Length;
    }

    private void Notify(Action<string>? handlers, string value, string eventName)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action<string> handler in handlers.GetInvocationList().Cast<Action<string>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                Logger.Error($"[{GameObject?.Name ?? "Unassigned"}:InputField] {eventName} handler threw: {exception.Message}");
            }
        }
    }
}
