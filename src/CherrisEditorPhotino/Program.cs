using CherrisEditorPhotino.Services;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;

namespace CherrisEditorPhotino;

/// <summary>
/// Entry point for the PhotinoX-hosted editor: a native Photino window whose webview runs the Blazor UI,
/// with the Cherris render loop in the same process.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        EditorHostService editorHost = EditorHostService.Start(args);

        try
        {
            var appBuilder = PhotinoBlazorApp.CreateBuilder(args);

            // The Blazor root component matches the <app> element in wwwroot/index.html.
            appBuilder.RootComponents.Add<App>("app");
            appBuilder.Services.AddSingleton(editorHost);

            var app = appBuilder.Build();

            app.MainWindow
                .SetTitle("Cherris Editor")
                .SetSize(1600, 900);

            AppDomain.CurrentDomain.UnhandledException += (_, error) =>
                app.MainWindow.ShowMessage(
                    "Cherris Editor",
                    error.ExceptionObject?.ToString() ?? "Unknown fatal exception.");

            // Runs the native message loop and returns when the window closes. The window's own native
            // resources go away with the process; the editor host is torn down explicitly because it owns
            // a GL context and a socket listener that outlive the window otherwise.
            app.Run();

            return editorHost.FailureMessage is null ? 0 : 1;
        }
        finally
        {
            editorHost.Dispose();
        }
    }
}
