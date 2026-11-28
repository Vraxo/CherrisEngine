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
/// Code-behind for <see cref="StatusBar"/>: the viewport numbers and connection state it shows.
/// The markup lives in <c>StatusBar.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class StatusBar
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private ViewportStats _stats = new(false, 0, 0, 0, 0, "idle", "");
    private int _protocol;
    private Timer? _timer;

    private string StatusText => _stats.Status switch
    {
        "connected" => "connected",
        "connecting" => "connecting…",
        "disconnected" => "disconnected — retrying",
        "error" => "viewport error",
        _ => "idle"
    };

    private string StatusClass => _stats.Status switch
    {
        "connected" => "status-ok",
        "connecting" or "idle" => "status-pending",
        _ => "status-bad"
    };

    private string EngineText => Host.State == EditorSessionState.Running ? "engine running" : "engine stopped";

    private string RendererText => Host.RendererInfo ?? "renderer starting";

    private string SessionText => Host.ProjectRoot ?? "demo scene";

    private string SceneText => Host.LatestScene is SceneSnapshot scene
        ? $"{scene.SceneName ?? "untitled"} · {scene.Nodes.Count} objects"
        : "no snapshot";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _protocol = await JS.InvokeAsync<int>("overcoreViewport.getProtocol");
        }
        catch (Exception)
        {
            // The bridge script is missing or the webview is not ready; the status line shows idle instead.
        }

        _timer = new Timer(_ => _ = PollAsync(), null, PollInterval, PollInterval);
    }

    private async Task PollAsync()
    {
        ViewportStats stats;

        try
        {
            stats = await JS.InvokeAsync<ViewportStats>("overcoreViewport.getStats");
        }
        catch (Exception)
        {
            // Interop fails once the window is closing; stop polling rather than spamming exceptions.
            return;
        }

        await InvokeAsync(() =>
        {
            _stats = stats;
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
