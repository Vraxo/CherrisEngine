using OvercoreEditorPhotino.Services;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;

namespace OvercoreEditorPhotino;

/// <summary>
/// Entry point for the PhotinoX-hosted editor: a native Photino window whose webview runs the Blazor UI,
/// with the Overcore render loop in the same process. The app opens on the project launcher; picking a
/// project starts a render session for it.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        LaunchOptions launch = LaunchOptions.Parse(args);

        EditorHostService editorHost = new(
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            launch.Width,
            launch.Height,
            launch.TargetFrameRate)
        {
            PendingProject = launch.ProjectPath
        };

        try
        {
            var appBuilder = PhotinoBlazorApp.CreateBuilder(args);

            // The Blazor root component matches the <app> element in wwwroot/index.html.
            appBuilder.RootComponents.Add<App>("app");
            appBuilder.Services.AddSingleton(editorHost);

            // One snapshot poller and one selection shared by the hierarchy, inspector and viewport, so the
            // panels agree on the frame and the selected object. The selection is built here rather than by the
            // container because the host needs it too: a click in the viewport is picked on the render thread and
            // handed straight to it.
            SceneSelection selection = new();

            // Themes are data: the catalog reads the shipped Themes folder and the user's, and applying one
            // sets CSS custom properties on the document (wwwroot/js/theme.js).
            appBuilder.Services.AddSingleton<ThemeService>();
            appBuilder.Services.AddSingleton<SceneStore>();
            appBuilder.Services.AddSingleton(selection);

            editorHost.SelectionSink = selection.Select;

            // Scene previews are read from the scene files themselves, on a worker thread, and reused until the
            // files change.
            appBuilder.Services.AddSingleton<ScenePreviewStore>();

            var app = appBuilder.Build();

            app.MainWindow
                .SetTitle("Overcore Editor")
                .SetSize(1600, 900);

            // The launcher's Browse buttons use the window's native folder dialog, which only exists once
            // the window does. Without it the launcher still accepts typed paths.
            editorHost.FolderPicker = (title, startIn) => app.MainWindow.ShowOpenFolderAsync(title, startIn);

            // File > Exit closes the window the same way the window's own close button does.
            editorHost.CloseRequested = app.MainWindow.Close;

            AppDomain.CurrentDomain.UnhandledException += (_, error) =>
                app.MainWindow.ShowMessage(
                    "Overcore Editor",
                    error.ExceptionObject?.ToString() ?? "Unknown fatal exception.");

            // Runs the native message loop and returns when the window closes. The window's own native
            // resources go away with the process; the editor host is torn down explicitly because it owns
            // a GL context, a render thread and a socket listener that outlive the window otherwise.
            app.Run();

            return editorHost.FailureMessage is null ? 0 : 1;
        }
        finally
        {
            editorHost.Dispose();
        }
    }
}
