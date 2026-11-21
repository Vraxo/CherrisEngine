using Cherris.Rendering;
using ImGuiNET;

namespace CherrisEditor.UI.Gui;

public sealed class ImGuiController : IUIController
{
    private readonly ImGuiRenderer _renderer;
    private readonly ImGuiFontLoader _fontLoader;
    private bool _frameBegun;

    public ImGuiController(int width, int height)
    {
        IntPtr context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        ImGuiIOPtr io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        _fontLoader = new ImGuiFontLoader();
        _fontLoader.Load();

        _renderer = new ImGuiRenderer(width, height);

        SetPerFrameData(1.0f / 60.0f);
        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void WindowResized(int width, int height)
    {
        // Renderer handles projection matrix; IO handles display size
        ImGui.GetIO().DisplaySize = new System.Numerics.Vector2(width, height);
    }

    public void Update(float deltaTime)
    {
        if (_frameBegun)
        {
            ImGui.Render();
        }

        SetPerFrameData(deltaTime);

        _frameBegun = true;
        ImGui.NewFrame();
    }

    public void Render()
    {
        if (!_frameBegun)
        {
            return;
        }

        _frameBegun = false;
        ImGui.Render();
        _renderer.Render(ImGui.GetDrawData());
    }

    private static void SetPerFrameData(float deltaTime)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.DeltaTime = deltaTime;
    }

    public void Dispose()
    {
        _fontLoader.Dispose();
        _renderer.Dispose();
        ImGui.DestroyContext();
    }
}