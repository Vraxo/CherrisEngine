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
/// Code-behind for <see cref="ToolBar"/>: the transport buttons and their enabled state.
/// The markup lives in <c>ToolBar.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class ToolBar
{
    // A session without a published snapshot has no scene to play yet, so the transport waits for one rather
    // than offering a button that would do nothing.
    private bool CanPlay => Host.State == EditorSessionState.Running && Host.LatestScene is not null;
    private bool IsPlaying => CanPlay && Host.PlayState == EditorPlayState.Playing;
    private bool CanStop => CanPlay && Host.PlayState != EditorPlayState.Editing;

    private string PlayLabel => IsPlaying ? "Pause" : "Play";

    private string PlayTitle
    {
        get
        {
            if (!CanPlay)
            {
                return "Play — no scene is open yet.";
            }

            if (IsPlaying)
            {
                return "Pause — freeze the scene where it is.";
            }

            return Host.PlayState == EditorPlayState.Paused
                ? "Resume — carry on from where you paused."
                : "Play — run the scene: physics and scripts start ticking.";
        }
    }

    private string StopTitle => CanStop
        ? "Stop — put the scene back to how it was authored."
        : "Stop — nothing is running to stop.";

    private string RestartTitle => CanStop
        ? "Restart — start again from the authored scene."
        : "Restart — nothing is running to restart.";

    protected override void OnInitialized()
    {
        // Play state can also change without a click, when a session starts or closes.
        Store.Changed += OnSceneChanged;
    }

    private void OnSceneChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void TogglePlayPause()
    {
        if (IsPlaying)
        {
            Host.Pause();
        }
        else
        {
            Host.Play();
        }

        StateHasChanged();
    }

    private void StopPlayback()
    {
        Host.Stop();
        StateHasChanged();
    }

    private void RestartPlayback()
    {
        Host.Restart();
        StateHasChanged();
    }

    public void Dispose()
    {
        Store.Changed -= OnSceneChanged;
    }
}
