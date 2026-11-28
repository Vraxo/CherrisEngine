using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Overcore.Projects;
using OvercoreEditorPhotino;
using OvercoreEditorPhotino.Components;
using OvercoreEditorPhotino.Shared;
using OvercoreEditorPhotino.Services;
using OvercoreEditorSession;

namespace OvercoreEditorPhotino.Components;

/// <summary>
/// Code-behind for <see cref="ResetButton"/>: the reset affordance and the pending value it shows.
/// The markup lives in <c>ResetButton.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class ResetButton
{
    /// <summary>
    /// True when the value differs from the default. That is the only time the button works, and the only time it
    /// is drawn at full strength.
    /// </summary>
    [Parameter]
    public bool CanReset { get; set; }

    /// <summary>What resetting would restore, as the row shows it, for the tooltip. Null when there is none.</summary>
    [Parameter]
    public string? Default { get; set; }

    /// <summary>The row this button belongs to, so a screen reader reads "Position: reset to 0, 0, 0".</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>The edit to queue. It only runs while <see cref="CanReset"/> is true, because the button is disabled otherwise.</summary>
    [Parameter]
    public EventCallback OnReset { get; set; }

    private string Title => CanReset
        ? "Reset to " + (Default ?? "the default")
        : "Already at " + (Default ?? "the default");
}
