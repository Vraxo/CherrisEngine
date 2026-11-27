using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Rendering.Shadows;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Overcore.Rendering.OpenTK;

public class OpenTKRenderer : IRenderer, IDisposable
{
    private readonly ImGuiController? _imGuiController;
    // Sub-renderers
    private readonly OpenGLSceneRenderer _sceneRenderer;
    private readonly OpenGLSkyboxRenderer _skyboxRenderer;
    private readonly OpenGLPostProcessor _postProcessor;
    private readonly OpenGLDebugRenderer _debugRenderer;
    private readonly OpenGLGizmoRenderer _gizmoRenderer;
    private readonly OpenGLGridRenderer _gridRenderer;
    private readonly OpenGLShadowRenderer _shadowRenderer;
    private readonly OpenGLUIRenderer _uiRenderer;
    private readonly bool _renderImGui;
    private Vector2i _viewportSize = new(1, 1);
    private bool _viewportConfiguredThisFrame;
    private System.Numerics.Vector2 _viewportScreenOrigin;

    /// <summary>Whether pointer events should reach UI controls in a non-standalone editor preview.</summary>
    public bool UIInputEnabled { get; set; }

    public bool ShowGrid { get; set; } = true;
    public bool ShowPhysicsColliders { get; set; } = true;
    /// <param name="imGuiController">
    /// Controller for the font atlas and optional in-engine ImGui UI. Pass a controller with
    /// <paramref name="renderImGui"/> false when an off-screen host needs the shared UI text renderer only.
    /// </param>
    public OpenTKRenderer(ImGuiController? imGuiController = null, IResourceManager? resourceManager = null,
        bool renderImGui = true)
    {
        _imGuiController = imGuiController;
        _renderImGui = renderImGui;
        _sceneRenderer = new OpenGLSceneRenderer();
        _skyboxRenderer = new OpenGLSkyboxRenderer();
        _postProcessor = new OpenGLPostProcessor();
        _debugRenderer = new OpenGLDebugRenderer();
        _gizmoRenderer = new OpenGLGizmoRenderer(_debugRenderer);
        _gridRenderer = new OpenGLGridRenderer();
        _shadowRenderer = new OpenGLShadowRenderer();
        _uiRenderer = new OpenGLUIRenderer(_imGuiController, resourceManager);
        GL.FrontFace(FrontFaceDirection.Cw);
    }
    public IntPtr GetSceneTextureHandle()
    {
        return _postProcessor.FinalSceneTexture;
    }
    public void SetViewportSize(System.Numerics.Vector2 size)
    {
        var newSize = new Vector2i((int)Math.Max(size.X, 1), (int)Math.Max(size.Y, 1));
        if (newSize != _viewportSize)
        {
            _viewportSize = newSize;
            _postProcessor.OnResize(_viewportSize.X, _viewportSize.Y);
        }
        _viewportConfiguredThisFrame = true;
        _viewportScreenOrigin = System.Numerics.Vector2.Zero;
    }

    /// <summary>Sets the viewport's top-left point in window coordinates for local UI pointer mapping.</summary>
    public void SetViewportPosition(System.Numerics.Vector2 screenOrigin) => _viewportScreenOrigin = screenOrigin;

    [Obsolete]
    public void RenderFrame(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float windowWidth, float windowHeight, float exposure)
    {
        bool isRuntimeMode = !_viewportConfiguredThisFrame;
        if (isRuntimeMode)
        {
            var newSize = new Vector2i((int)Math.Max(windowWidth, 1), (int)Math.Max(windowHeight, 1));
            if (_viewportSize != newSize)
            {
                _viewportSize = newSize;
                _postProcessor.OnResize(_viewportSize.X, _viewportSize.Y);
            }
        }
        bool sceneRendered = false;
        if (mainCamera is not null)
        {
            RenderScenePass(mainCamera, skybox, gameObjects, lights, selectedObject, exposure, isRuntimeMode);
            sceneRendered = true;
        }
        // Composite UI into the post-processed scene texture before it is shown or captured. This keeps the same
        // image in the standalone runtime, native editor viewport and headless/web editor viewport paths.
        System.Numerics.Vector2 pointerPosition = Input.MousePosition;
        if (!isRuntimeMode && _renderImGui && _imGuiController is not null)
        {
            pointerPosition -= _viewportScreenOrigin;
        }

        _uiRenderer.Render(gameObjects, _viewportSize.X, _viewportSize.Y,
            _postProcessor.CompositeFramebuffer, clearTarget: !sceneRendered,
            pointerPosition: pointerPosition, inputEnabled: isRuntimeMode || UIInputEnabled);

        if (isRuntimeMode)
        {
            _postProcessor.BlitToScreen();
        }

        // Runtime already blitted either the scene or the cleared UI canvas to the window; editor mode clears the
        // native window behind its ImGui panels while the game view remains in the off-screen texture.
        RenderUIPass((int)windowWidth, (int)windowHeight, clearScreen: !isRuntimeMode);
        _viewportConfiguredThisFrame = false;
    }

    [Obsolete]
    private void RenderScenePass(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float exposure, bool isRuntimeMode)
    {
        // The depth pass runs first and owns its own framebuffer, so it has to finish before BeginFrame binds the
        // MSAA target the colour pass draws into. Sharing that target instead would leave it with a depth attachment
        // that has been resolved four times and can be neither read nor compared.
        float aspectRatio = (float)_viewportSize.X / MathF.Max(1f, _viewportSize.Y);
        List<GameObject> sceneObjects = [.. gameObjects];

        ShadowFrame shadows = ShadowFrame.Build(sceneObjects, mainCamera, aspectRatio);

        _shadowRenderer.Render(
            shadows,
            sceneObjects,
            _sceneRenderer.GetOrCreateBackendData,
            _sceneRenderer.GetOrCreateSkinnedData);

        GL.Enable(EnableCap.FramebufferSrgb);
        _postProcessor.BeginFrame();
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
        GL.Disable(EnableCap.Blend);
        var view = ToOpenTKMatrix(mainCamera.GetViewMatrix());

        // The camera keeps its own values inside what a perspective projection accepts — a near plane of -0.02
        // used to throw here and take the render loop with it — and the aspect is floored, so a viewport that
        // has not been measured yet cannot divide by zero either. The same aspect the cascades were fitted for, so
        // the receiver's cascades and the map's slices cover the same wedge of the world.
        var projection = Matrix4.CreatePerspectiveFieldOfView(
            mainCamera.FieldOfViewRadians,
            aspectRatio,
            mainCamera.NearClipPlane,
            mainCamera.FarClipPlane);
        // Drawn whether or not the scene brought a skybox: without this the background is a cleared buffer, and an
        // empty scene reads as a black void with a grid floating in it, in the editor and at runtime alike.
        _skyboxRenderer.Render(skybox, view, projection);
        if (!isRuntimeMode)
        {
            if (ShowGrid)
            {
                var cameraPos = view.Inverted().Row3.Xyz;
                _gridRenderer.Render(view, projection, cameraPos);
            }
        }
        _sceneRenderer.Render(sceneObjects, lights, view, projection, shadows, _shadowRenderer.Texture);
        if (!isRuntimeMode)
        {
            _gizmoRenderer.Render(gameObjects, selectedObject, ShowPhysicsColliders);
            _debugRenderer.Render(view, projection);
        }
        _postProcessor.ResolveMsaa();
        _postProcessor.RenderBloom();
        _postProcessor.Composite(exposure);
        GL.Disable(EnableCap.FramebufferSrgb);
    }
    private void RenderUIPass(int width, int height, bool clearScreen)
    {
        // Hosts without an in-engine UI (headless backends) render the scene pass only; there is no
        // window to present into and nothing to clear.
        if (!_renderImGui || _imGuiController is null)
        {
            return;
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, width, height);
        if (clearScreen)
        {
            GL.ClearColor(0.1f, 0.105f, 0.11f, 1.00f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        _imGuiController.Render();
        // Defensive: reset clear color to prevent leakage to next frame
        // ImGui or other external code may have modified GL state
        GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
    }
    public void OnWindowResized() { }
    public void RequestSnapshot(string path) { /* Not implemented */ }
    public void ProcessSnapshot() { /* Not implemented */ }
    public void Dispose()
    {
        _sceneRenderer.Dispose();
        _shadowRenderer.Dispose();
        _skyboxRenderer.Dispose();
        _postProcessor.Dispose();
        _debugRenderer.Dispose();
        _gridRenderer.Dispose();
        _uiRenderer.Dispose();
    }
    private static Matrix4 ToOpenTKMatrix(System.Numerics.Matrix4x4 m)
    {
        return new Matrix4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        );
    }
}