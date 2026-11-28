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

namespace OvercoreEditorPhotino.Shared;

/// <summary>
/// Code-behind for <see cref="MainLayout"/>: the theme service hookup.
/// The markup lives in <c>MainLayout.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class MainLayout
{
    private bool IsEditorRoute
    {
        get
        {
            string path = new Uri(Nav.Uri).AbsolutePath.TrimEnd('/');

            return path.EndsWith("/editor", StringComparison.OrdinalIgnoreCase);
        }
    }

    protected override void OnInitialized()
    {
        Nav.LocationChanged += OnLocationChanged;
    }

    // The stylesheet ships the default theme so the first frame is already styled; this swaps in whichever
    // theme the editor has selected, for the launcher and the editor alike.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            await Themes.ApplyAsync(JS);
        }
        catch (JSDisconnectedException)
        {
            // The window went away before the first render finished.
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Nav.LocationChanged -= OnLocationChanged;
    }
}
