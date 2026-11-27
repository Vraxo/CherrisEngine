using Overcore.Core.Logging;
using System.Numerics;

namespace Overcore.Components;

/// <summary>A pointer-activated two-state control. Attach an <see cref="Image"/> to show its state.</summary>
public sealed class Toggle : Component
{
    private bool _isOn;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
            {
                return;
            }

            _isOn = value;
            NotifyValueChanged(value);
        }
    }

    public bool Interactable { get; set; } = true;
    public Vector3 OnColor { get; set; } = Vector3.One;
    public Vector3 OffColor { get; set; } = new(0.55f, 0.55f, 0.55f);
    public Vector3 DisabledColor { get; set; } = new(0.4f, 0.4f, 0.4f);

    /// <summary>Raised when <see cref="IsOn"/> changes.</summary>
    public event Action<bool>? ValueChanged;

    /// <summary>Flips the value if the control is enabled and interactable.</summary>
    public void ToggleValue()
    {
        if (Enabled && Interactable)
        {
            IsOn = !IsOn;
        }
    }

    private void NotifyValueChanged(bool value)
    {
        if (ValueChanged is not { } handlers)
        {
            return;
        }

        foreach (Action<bool> handler in handlers.GetInvocationList().Cast<Action<bool>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                Logger.Error($"[{GameObject?.Name ?? "Unassigned"}:Toggle] ValueChanged handler threw: {exception.Message}");
            }
        }
    }
}
