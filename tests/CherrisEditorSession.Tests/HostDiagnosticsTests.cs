using AwesomeAssertions;
using CherrisEditorSession.Host;
using Xunit;

namespace CherrisEditorSession.Tests;

public class HostDiagnosticsTests
{
    [Fact]
    public void Build_ListsTheEnvironmentTheUserNeedsToReport()
    {
        HostDiagnosticsInfo info = new(
            Runtime: ".NET 11.0.0 on Windows 11 (X64)",
            BaseDirectory: @"C:\apps\CherrisEditorPhotino",
            WebRoot: @"C:\apps\CherrisEditorPhotino\wwwroot",
            WebRootExists: true,
            EditorResourcesPresent: true,
            HostUrl: "http://localhost:53112/",
            RendererInfo: "NVIDIA GeForce RTX 3060 · GL 4.6.0",
            Failure: null,
            LogFilePath: @"C:\Users\dev\AppData\Local\Cherris\editor-host.log");

        string text = HostDiagnostics.Build(info);

        text.Should().Contain(".NET 11.0.0 on Windows 11 (X64)");
        text.Should().Contain(@"C:\apps\CherrisEditorPhotino");
        text.Should().Contain("http://localhost:53112/");
        text.Should().Contain("NVIDIA GeForce RTX 3060 · GL 4.6.0");
        text.Should().Contain("editor-host.log");
        text.Should().Contain("(none)");
    }

    [Fact]
    public void Build_MarksMissingPiecesAndKeepsTheFailureVerbatim()
    {
        const string failure = "OpenTK.Windowing.GraphicsLibraryFramework.GLFWException: GLFW can only be " +
            "called from the main thread!";

        HostDiagnosticsInfo info = new(
            Runtime: ".NET 11.0.0 on Linux 6.8",
            BaseDirectory: "/opt/cherris",
            WebRoot: "/opt/cherris/wwwroot",
            WebRootExists: false,
            EditorResourcesPresent: false,
            HostUrl: null,
            RendererInfo: null,
            Failure: failure,
            LogFilePath: null);

        string text = HostDiagnostics.Build(info);

        text.Should().Contain("EditorResources MISSING");
        text.Should().Contain("(missing)");
        text.Should().Contain("(not listening)");
        text.Should().Contain("(not initialised)");
        text.Should().Contain("(not written)");
        text.Should().Contain(failure);
        text.Should().NotContain("(none)");
    }
}
