using Cherris.Components;
using Cherris.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Cherris.Rendering.OpenTK;

public class OpenTKRenderer : IRenderer, IDisposable
{
    private readonly ImGuiController _imGuiController;
    // Sub-renderers
    private readonly OpenGLSceneRenderer _sceneRenderer;
    private readonly OpenGLSkyboxRenderer _skyboxRenderer;
    private readonly OpenGLPostProcessor _postProcessor;
    private readonly OpenGLDebugRenderer _debugRenderer;
    private readonly OpenGLGizmoRenderer _gizmoRenderer;
    private readonly OpenGLGridRenderer _gridRenderer;
    private Vector2i _viewportSize = new(1, 1);
    private bool _viewportConfiguredThisFrame;
    public bool ShowGrid { get; set; } = true;
    public bool ShowPhysicsColliders { get; set; } = true;
    public OpenTKRenderer(ImGuiController imGuiController)
    {
        _imGuiController = imGuiController;
        _sceneRenderer = new OpenGLSceneRenderer();
        _skyboxRenderer = new OpenGLSkyboxRenderer();
        _postProcessor = new OpenGLPostProcessor();
        _debugRenderer = new OpenGLDebugRenderer();
        _gizmoRenderer = new OpenGLGizmoRenderer(_debugRenderer);
        _gridRenderer = new OpenGLGridRenderer();
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
    }

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
        if (isRuntimeMode && sceneRendered)
        {
            _postProcessor.BlitToScreen();
        }
        bool shouldClearScreen = !isRuntimeMode || !sceneRendered;
        RenderUIPass((int)windowWidth, (int)windowHeight, shouldClearScreen);
        _viewportConfiguredThisFrame = false;
    }

    [Obsolete]
    private void RenderScenePass(Camera mainCamera, Skybox skybox, IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, GameObject selectedObject, float exposure, bool isRuntimeMode)
    {
        GL.Enable(EnableCap.FramebufferSrgb);
        _postProcessor.BeginFrame();
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
        GL.Disable(EnableCap.Blend);
        var view = ToOpenTKMatrix(mainCamera.GetViewMatrix());
        var projection = Matrix4.CreatePerspectiveFieldOfView(
            mainCamera.FieldOfView * (float)Math.PI / 180.0f,
            (float)_viewportSize.X / _viewportSize.Y,
            mainCamera.NearClipPlane,
            mainCamera.FarClipPlane);
        if (skybox?.CubeMapTexture is not null)
        {
            _skyboxRenderer.Render(skybox, view, projection);
        }
        if (!isRuntimeMode)
        {
            if (ShowGrid)
            {
                var cameraPos = view.Inverted().Row3.Xyz;
                _gridRenderer.Render(view, projection, cameraPos);
            }
        }
        _sceneRenderer.Render(gameObjects, lights, view, projection);
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
        _skyboxRenderer.Dispose();
        _postProcessor.Dispose();
        _debugRenderer.Dispose();
        _gridRenderer.Dispose();
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