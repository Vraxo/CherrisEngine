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
/// Code-behind for <see cref="ViewportPanel"/>: the frame bridge calls and the pick/overlay state.
/// The markup lives in <c>ViewportPanel.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class ViewportPanel
{
    /// <summary>WebSocket endpoint of the in-process editor host.</summary>
    [Parameter]
    public string WebSocketUrl { get; set; } = string.Empty;

    private bool _mounted;
    private bool _optionsOpen;

    private string SceneName => Store.Current?.SceneName ?? "Viewport";

    private int ObjectCount => Store.Current?.Nodes.Count ?? 0;

    private void ToggleOptions()
    {
        _optionsOpen = !_optionsOpen;
    }

    /// <summary>
    /// Turns the grid on or off. The session's render loop reads the setting on its next frame, so the tick in this
    /// menu and the picture in the viewport are the same value.
    /// </summary>
    private void ToggleGrid()
    {
        Host.ShowGrid = !Host.ShowGrid;
        _optionsOpen = false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _mounted || string.IsNullOrEmpty(WebSocketUrl))
        {
            return;
        }

        // "First render only" is not enough on its own: the interop call is async, so the flag also stops a
        // second render pass from mounting the bridge twice.
        _mounted = true;

        try
        {
            await JS.InvokeVoidAsync("overcoreViewport.mount", "viewport-canvas", WebSocketUrl);
        }
        catch (JSDisconnectedException)
        {
            _mounted = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_mounted)
        {
            return;
        }

        _mounted = false;

        try
        {
            await JS.InvokeVoidAsync("overcoreViewport.dispose");
        }
        catch (Exception)
        {
            // The webview can already be gone while the window is closing; the bridge dies with it.
        }
    }
}
