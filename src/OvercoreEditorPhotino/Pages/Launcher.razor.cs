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
/// Code-behind for <see cref="Launcher"/>: recent projects, folder picking and the start commands.
/// The markup lives in <c>Launcher.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class Launcher
{
    private enum LauncherTab
    {
        Projects,
        NewProject
    }

    private static readonly TimeSpan HealthInterval = TimeSpan.FromMilliseconds(500);

    private LauncherTab Tab { get; set; } = LauncherTab.Projects;

    // File > New Project… in the editor links here as /?tab=new.
    [SupplyParameterFromQuery(Name = "tab")]
    public string? TabQuery { get; set; }

    private IReadOnlyList<RecentProjectEntry> _recent = [];
    private string _search = string.Empty;
    private string _openPath = string.Empty;
    private string _newProjectName = "MyGame";
    private string _selectedTemplateId = ProjectTemplateCatalog.BlankId;
    private string _location = DefaultLocation();
    private string? _error;
    private bool _busy;
    private string _busyText = string.Empty;
    private bool _recovered;
    private Timer? _healthTimer;

    private IReadOnlyList<RecentProjectEntry> Matches
    {
        get
        {
            string query = _search.Trim();

            return query.Length == 0
                ? _recent
                : _recent.Where(entry => entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }

    private string ProjectFolderPreview
    {
        get
        {
            try
            {
                return ProjectWorkspace.GetNewProjectPath(_location, _newProjectName);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return "Choose a valid name and location to preview the folder.";
            }
        }
    }

    protected override void OnInitialized()
    {
        if (string.Equals(TabQuery, "new", StringComparison.OrdinalIgnoreCase))
        {
            Tab = LauncherTab.NewProject;
        }

        RefreshRecent();

        // A session can die while the launcher is showing (for example after a driver reset), and the
        // launcher's buttons are the only way out of that state, so watch for it.
        _healthTimer = new Timer(_ => _ = CheckRecoveryAsync(), null, HealthInterval, HealthInterval);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        string? pending = Host.PendingProject;

        if (pending is not null)
        {
            Host.PendingProject = null;
            await OpenAsync(pending);
            return;
        }

        // A session survived a navigation (or was started before the router ran): show it.
        if (Host.State == EditorSessionState.Running)
        {
            Nav.NavigateTo("/editor");
        }
    }

    private async Task CheckRecoveryAsync()
    {
        if (Host.State != EditorSessionState.Failed || _recovered)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            _recovered = true;
            _error ??= Host.FailureMessage;
            StateHasChanged();
        });
    }

    private void SetTab(LauncherTab tab)
    {
        Tab = tab;
        _error = null;
    }

    private void StartFromTemplate(ProjectTemplate template)
    {
        _selectedTemplateId = template.Id;
        _newProjectName = template.Name;
        SetTab(LauncherTab.NewProject);
    }

    private void RefreshRecent()
    {
        _recent = Host.RecentProjects;
    }

    private void Forget(string path)
    {
        Host.ForgetProject(path);
        RefreshRecent();
    }

    private async Task BrowseOpenAsync()
    {
        string? folder = await BrowseAsync("Open a Overcore project folder", _openPath);

        if (folder is not null)
        {
            _openPath = folder;
            _error = null;
        }
    }

    private async Task BrowseLocationAsync()
    {
        string? folder = await BrowseAsync("Choose where the project folder goes", _location);

        if (folder is not null)
        {
            _location = folder;
            _error = null;
        }
    }

    private async Task<string?> BrowseAsync(string title, string? startIn)
    {
        if (Host.FolderPicker is null)
        {
            _error = "The folder dialog is unavailable here; type the path instead.";
            return null;
        }

        try
        {
            return await Host.PickFolderAsync(title, string.IsNullOrWhiteSpace(startIn) ? null : startIn);
        }
        catch (Exception exception)
        {
            _error = $"The folder dialog failed: {exception.Message}";
            return null;
        }
    }

    private async Task CreateAsync()
    {
        if (_busy)
        {
            return;
        }

        string root;

        try
        {
            root = ProjectWorkspace.CreateFromTemplate(_location, _newProjectName, _selectedTemplateId);
        }
        catch (Exception exception)
        {
            _error = exception.Message;
            return;
        }

        await OpenAsync(root);
    }

    private async Task OpenDemoAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _error = null;
        _busyText = "Starting the demo scene…";

        try
        {
            if (await Host.OpenDemoSceneAsync())
            {
                Nav.NavigateTo("/editor");
                return;
            }

            _error = Host.FailureMessage ?? "The demo scene could not be started.";
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }

    private async Task OpenAsync(string path)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _error = null;
        _busyText = $"Opening {ProjectWorkspace.DisplayName(path.Trim())}…";
        StateHasChanged();

        try
        {
            if (await Host.OpenProjectAsync(path))
            {
                Nav.NavigateTo("/editor");
                return;
            }

            _error = Host.FailureMessage ?? "The project could not be opened.";
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _busy = false;
            RefreshRecent();
            StateHasChanged();
        }
    }

    private static string DefaultLocation()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        return string.IsNullOrEmpty(documents) || !Directory.Exists(documents)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : documents;
    }

    public void Dispose()
    {
        _healthTimer?.Dispose();
        _healthTimer = null;
    }
}
