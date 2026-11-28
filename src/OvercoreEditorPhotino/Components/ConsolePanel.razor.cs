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
using using LogLevel = Overcore.Core.LogLevel;

namespace OvercoreEditorPhotino.Components;

/// <summary>
/// Code-behind for <see cref="ConsolePanel"/>: the log rows, level filter and search.
/// The markup lives in <c>ConsolePanel.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class ConsolePanel
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private IReadOnlyList<EditorLogEntry> _entries = [];
    private bool _autoScroll = true;
    private bool _needsScroll;
    private Timer? _timer;

    protected override void OnInitialized()
    {
        _entries = Host.LogEntries;
        _needsScroll = _autoScroll;
        _timer = new Timer(_ => _ = PollAsync(), null, PollInterval, PollInterval);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_needsScroll)
        {
            return;
        }

        _needsScroll = false;

        try
        {
            await JS.InvokeVoidAsync("overcoreUi.scrollToEnd", "console-log");
        }
        catch (JSDisconnectedException)
        {
            // The window is closing; the log goes with it.
        }
    }

    private async Task PollAsync()
    {
        IReadOnlyList<EditorLogEntry> entries = Host.LogEntries;

        if (entries.Count == _entries.Count && (entries.Count == 0 || ReferenceEquals(entries[^1], _entries[^1])))
        {
            return;
        }

        await InvokeAsync(() =>
        {
            _entries = entries;
            _needsScroll = _autoScroll;
            StateHasChanged();
        });
    }

    private void Clear()
    {
        Host.ClearLog();
        _entries = Host.LogEntries;
    }

    private static string LevelClass(LogLevel level) => level switch
    {
        LogLevel.Warning => "log-warning",
        LogLevel.Error => "log-error",
        _ => "log-info"
    };

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
