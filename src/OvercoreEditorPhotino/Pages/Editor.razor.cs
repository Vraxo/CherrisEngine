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

namespace OvercoreEditorPhotino.Pages;

/// <summary>
/// Code-behind for <see cref="Editor"/>: the dockspace state and the resize handling.
/// The markup lives in <c>Editor.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class Editor
{
    private static readonly TimeSpan HealthInterval = TimeSpan.FromMilliseconds(500);

    private Timer? _timer;
    private bool _copied;
    private bool _dockMounted;
    private string? _lastFailure;
    private EditorSessionState _lastState;
    private string _bottomTab = "content";

    protected override void OnInitialized()
    {
        _lastFailure = Host.FailureMessage;
        _lastState = Host.State;

        // Deep-linking to /editor with nothing open (or a reload after a crash) belongs on the launcher.
        if (Host.State == EditorSessionState.Idle)
        {
            Nav.NavigateTo("/");
        }

        // The render thread can die long after the window opened (a driver reset, a lost context), so the
        // page watches for the first failure instead of only checking once at startup.
        _timer = new Timer(_ => _ = CheckHealthAsync(), null, HealthInterval, HealthInterval);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_dockMounted || Host.State != EditorSessionState.Running)
        {
            return;
        }

        // mount() reports false when #dockspace is not in the DOM yet (the first render is still the
        // "starting" state), so stay unmounted and try again on the next render.
        try
        {
            _dockMounted = await JS.InvokeAsync<bool>("overcoreDock.mount");
        }
        catch (JSDisconnectedException)
        {
            // The webview is going away; nothing left to wire up.
        }
    }

    private async Task CheckHealthAsync()
    {
        if (Host.FailureMessage == _lastFailure && Host.State == _lastState)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            _lastFailure = Host.FailureMessage;
            _lastState = Host.State;
            StateHasChanged();
        });
    }

    private void BackToLauncher()
    {
        Host.CloseSession();
        Nav.NavigateTo("/");
    }

    private async Task CopyDiagnostics()
    {
        try
        {
            _copied = await JS.InvokeAsync<bool>("overcoreHost.copy", Host.Diagnostics);
        }
        catch (Exception)
        {
            _copied = false;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
