using Overcore.Core.Logging;
using System.Numerics;

namespace Overcore.Components;

/// <summary>A pointer-activated control. Attach an <see cref="Image"/> to show its state.</summary>
public sealed class Button : Component
{
    public bool Interactable { get; set; } = true;
    public Vector3 NormalColor { get; set; } = Vector3.One;
    public Vector3 HoverColor { get; set; } = new(0.88f, 0.92f, 1f);
    public Vector3 PressedColor { get; set; } = new(0.68f, 0.78f, 0.95f);
    public Vector3 DisabledColor { get; set; } = new(0.5f, 0.5f, 0.5f);

    /// <summary>Raised when a left-button press begins and ends over this button.</summary>
    public event Action? Clicked;

    /// <summary>Invokes the click handlers. Normally called by the scene's UI event system.</summary>
    public void Click()
    {
        if (Clicked is not { } handlers)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Logger.Error($"[{GameObject?.Name ?? "Unassigned"}:Button] Clicked handler threw: {exception.Message}");
            }
        }
    }
}
