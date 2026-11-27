using Overcore.Core.Logging;
using System.Numerics;

namespace Overcore.Components;

/// <summary>A pointer-draggable numeric control. Its track, fill, and handle are rendered from this component.</summary>
public sealed class Slider : Component
{
    private float _value;

    public float MinValue { get; set; }
    public float MaxValue { get; set; } = 1f;
    public bool WholeNumbers { get; set; }
    public bool Interactable { get; set; } = true;
    public SliderDirection Direction { get; set; } = SliderDirection.Horizontal;
    public float TrackThickness { get; set; } = 6f;
    public float HandleSize { get; set; } = 18f;
    public Vector3 TrackColor { get; set; } = new(0.22f, 0.24f, 0.28f);
    public Vector3 FillColor { get; set; } = new(0.2f, 0.52f, 0.92f);
    public Vector3 HandleColor { get; set; } = Vector3.One;
    public Vector3 DisabledColor { get; set; } = new(0.45f, 0.45f, 0.45f);

    public float Value
    {
        get => _value;
        set
        {
            float min = MathF.Min(MinValue, MaxValue);
            float max = MathF.Max(MinValue, MaxValue);
            float next = float.IsFinite(value) ? Math.Clamp(value, min, max) : min;
            if (WholeNumbers)
            {
                next = Math.Clamp(MathF.Round(next), min, max);
            }

            if (_value == next)
            {
                return;
            }

            _value = next;
            NotifyValueChanged(next);
        }
    }

    /// <summary>Value mapped to `[0, 1]`; reversed ranges map from MinValue toward MaxValue.</summary>
    public float NormalizedValue
    {
        get
        {
            float range = MaxValue - MinValue;
            return MathF.Abs(range) < float.Epsilon ? 0f : Math.Clamp((Value - MinValue) / range, 0f, 1f);
        }
        set => Value = MinValue + (MaxValue - MinValue) * Math.Clamp(float.IsFinite(value) ? value : 0f, 0f, 1f);
    }

    /// <summary>Raised only when <see cref="Value"/> actually changes.</summary>
    public event Action<float>? ValueChanged;

    /// <summary>Maps a pointer position to the slider value, clamping drags to the track endpoints.</summary>
    public void SetValueFromPointer(Vector2 pointerPosition, RectF rect)
    {
        float handle = Math.Clamp(HandleSize, 0f, MathF.Max(0f, MathF.Min(rect.Width, rect.Height)));
        float normalized;
        if (Direction == SliderDirection.Vertical)
        {
            float travel = MathF.Max(0f, rect.Height - handle);
            float minCenterY = rect.Bottom - handle * 0.5f;
            normalized = travel <= 0f ? 0.5f : (minCenterY - pointerPosition.Y) / travel;
        }
        else
        {
            float travel = MathF.Max(0f, rect.Width - handle);
            float minCenterX = rect.X + handle * 0.5f;
            normalized = travel <= 0f ? 0.5f : (pointerPosition.X - minCenterX) / travel;
        }

        NormalizedValue = normalized;
    }

    private void NotifyValueChanged(float value)
    {
        if (ValueChanged is not { } handlers)
        {
            return;
        }

        foreach (Action<float> handler in handlers.GetInvocationList().Cast<Action<float>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                Logger.Error($"[{GameObject?.Name ?? "Unassigned"}:Slider] ValueChanged handler threw: {exception.Message}");
            }
        }
    }
}

public enum SliderDirection
{
    Horizontal,
    Vertical
}
